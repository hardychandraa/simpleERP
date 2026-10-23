using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class SaleService : ISaleService
{
    private readonly ISaleRepository            _sales;
    private readonly IProductRepository         _products;
    private readonly ICustomerRepository        _customers;
    private readonly IBranchRepository          _branches;
    private readonly IPaymentRecordRepository   _payments;
    private readonly IAuditLogRepository        _audit;
    private readonly InventoryService           _inventory;
    private readonly IAppSettingsRepository     _settings;
    private readonly IPaymentTermRepository     _terms;
    private readonly ISalesPersonRepository     _people;
    private readonly ICustomerReturnRepository  _returns;
    private readonly ICreditNoteRepository      _notes;
    private readonly IPaymentBatchRepository    _batches;
    private readonly CommissionService          _commissions;
    private readonly IUnitOfWork                _uow;
    private readonly ICreditNoteApplicationRepository _noteApplications;
    private readonly IPendingPaymentRepository _pending;

    private readonly PeriodLock _period;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<SaleService> _log;
    public SaleService(ISaleRepository sales, IProductRepository products,
        ICustomerRepository customers, IBranchRepository branches,
        IPaymentRecordRepository payments,
        IAuditLogRepository audit, InventoryService inventory,
        IAppSettingsRepository settings, IPaymentTermRepository terms,
        ISalesPersonRepository people, ICustomerReturnRepository returns,
        ICreditNoteRepository notes, ICreditNoteApplicationRepository noteApplications,
        IPaymentBatchRepository batches,
        CommissionService commissions, IUnitOfWork uow,
        PeriodLock period, IStringLocalizer<SharedResource> loc, ILogger<SaleService> log,
        IPendingPaymentRepository pending)
    { _sales=sales; _products=products; _customers=customers; _branches=branches;
      _payments=payments;
      _audit=audit; _inventory=inventory; _settings=settings; _terms=terms;
      _people=people; _returns=returns; _notes=notes; _noteApplications=noteApplications;
      _batches=batches;
      _commissions=commissions; _uow=uow;  _period = period; _loc = loc; _log = log; _pending = pending; }

    public async Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user, bool isAdmin)
    {
        if (await AlreadySubmittedAsync(dto) is { } done) return done;
        return await CreateCoreAsync(dto, user, replacesSaleId: null, isAdmin);
    }

    /// <summary>
    /// A second submit of the same New Sale form (double click, back + resubmit) returns the
    /// sale the first one made instead of posting it again (G9, HC 2026-10-14). The lock makes
    /// two simultaneous submits of one form wait for each other; the unique index backs it up.
    /// </summary>
    private async Task<ServiceResult<SaleDto>?> AlreadySubmittedAsync(CreateSaleDto dto)
    {
        if (dto.SubmissionId is not { } sid || sid == Guid.Empty) return null;
        await _uow.LockAsync(sid);
        if (await _sales.FindBySubmissionIdAsync(sid) is not { } existing) return null;
        _log.LogInformation("Sale form {SubmissionId} submitted again: the sale it already made is returned", sid);
        return ServiceResult<SaleDto>.Ok(MapDto((await _sales.GetByIdWithItemsAsync(existing))!));
    }

    /// <summary>
    /// Only Admin cancels or revises a sale (R3, HC 2026-10-16). Staff could cancel today's
    /// sales with a reason (G2, 2026-10-14), but that let a cash sale be rung up, the cash kept
    /// and the sale cancelled; a revise is a cancel plus a new invoice, and cancelling a
    /// delivered credit sale gives the goods away. Staff send a request instead
    /// (<see cref="RequestChangeAsync"/>) and Admin cancels or revises.
    /// </summary>
    private LocalizedString? StaffChangeRefusal(Sale sale, bool isAdmin, string? reason) =>
        isAdmin ? null
        : _loc["Only an administrator can cancel or revise {0}. Send a request from the invoice instead.", sale.InvoiceNumber];

    /// <summary>
    /// Staff ask Admin to cancel or revise a sale (R3, HC 2026-10-16): the sale is flagged
    /// Perlu dicek with who asked and why; nothing else changes until Admin acts.
    /// </summary>
    public async Task<ServiceResult> RequestChangeAsync(Guid saleId, string? reason, string user)
    {
        var why = SanitiseText(reason, 300);
        if (why == null) return _log.Refuse(_loc["Say what is wrong, so the administrator knows what to cancel or change."]);
        await _uow.LockAsync(saleId);
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return _log.Refuse(_loc["Sale not found."]);
        if (sale.Status == SaleStatus.Cancelled) return _log.Refuse(_loc["Sale is already cancelled."]);
        if (PendingChangeRequest(sale) != null)
            return _log.Refuse(_loc["A request to cancel or revise {0} is already waiting for an administrator.", sale.InvoiceNumber]);
        var line = SaleReview.Line(SaleReview.ChangeRequest, user, why);
        sale.ReviewReasons = string.IsNullOrEmpty(sale.ReviewReasons) || !sale.NeedsReview ? line : sale.ReviewReasons + "\n" + line;
        sale.NeedsReview = true; sale.ReviewedBy = null; sale.ReviewedAt = null;
        _sales.Update(sale);
        await _audit.LogAsync(user, "Sale.ChangeRequest", $"{sale.InvoiceNumber}: {why}");
        _log.LogInformation("Change requested on {InvoiceNumber} by {User}: {Reason}", sale.InvoiceNumber, user, why);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>The reason of a change request still waiting for Admin, or null.</summary>
    public static string? PendingChangeRequest(Sale sale) => !sale.NeedsReview ? null
        : SaleReview.Parse(sale.ReviewReasons).Where(r => r[0] == SaleReview.ChangeRequest).Select(r => r.Length > 2 ? r[2] : "").FirstOrDefault();

    public async Task<ServiceResult<SaleDto>> ReviseAsync(Guid originalId, CreateSaleDto dto, string user, bool isAdmin, string? reason)
    {
        if (await AlreadySubmittedAsync(dto) is { } done) return done;
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(originalId);
        var original = await _sales.GetByIdWithItemsAsync(originalId);
        if (original == null) return _log.Refuse<SaleDto>(_loc["Sale not found."]);
        if (original.Status == SaleStatus.Cancelled)
            return _log.Refuse<SaleDto>(_loc["{0} is already cancelled and cannot be revised.", original.InvoiceNumber]);
        if (StaffChangeRefusal(original, isAdmin, reason) is { } staffNo) return _log.Refuse<SaleDto>(staffNo);
        // Money already received belongs to this invoice; moving it is a decision, not a
        // side effect of fixing a typo.
        if (await _pending.AnyPendingAsync(originalId))
            return _log.Refuse<SaleDto>(_loc["A payment on {0} is waiting for verification. Verify or reject it first.", original.InvoiceNumber]);
        if (original.PaymentType == PaymentType.Due && original.AmountPaid > 0)
            return _log.Refuse<SaleDto>(_loc["Payments have been recorded against {0}, so it cannot be revised. Correct it with a return or a credit note instead.", original.InvoiceNumber]);
        // Cancelling only voids UNPAID commission, and the replacement would accrue again —
        // so a paid-out commission would be paid twice.
        if (await _commissions.HasPaidOutForSaleAsync(originalId))
            return _log.Refuse<SaleDto>(_loc["Commission on {0} has already been paid out, so it cannot be revised.", original.InvoiceNumber]);

        var originalNumber = original.InvoiceNumber;
        ServiceResult<SaleDto>? result = null;

        // One transaction: the original is cancelled (stock back in, unpaid commission
        // voided) and those writes are visible to the replacement's own stock checks — so
        // an unchanged line is not refused for stock the original is holding. If the
        // replacement is refused, the cancellation rolls back with it and the original is
        // untouched.
        await _uow.InTransactionAsync(async () =>
        {
            var cancelled = await CancelAsync(originalId, user, isAdmin, reason);
            if (!cancelled.Success) { result = ServiceResult<SaleDto>.Fail(cancelled.Error!); return false; }

            var created = await CreateCoreAsync(dto, user, replacesSaleId: originalId, isAdmin);
            if (!created.Success) { result = created; return false; }

            // Shown on Admin's "cancelled / revised today" list with the replacement's number.
            original.CancelReason = Sanitise($"Revised as {created.Data!.InvoiceNumber}" + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}"), 500);
            await _uow.SaveChangesAsync();

            await _audit.LogAsync(user, "Sale.Revise", $"{originalNumber} -> {created.Data!.InvoiceNumber}");
            _log.LogInformation("Sale {Original} revised as {Replacement}, by {User}",
                originalNumber, created.Data.InvoiceNumber, user);
            result = created;
            return true;
        });
        return result!;
    }

    private async Task<ServiceResult<SaleDto>> CreateCoreAsync(CreateSaleDto dto, string user, Guid? replacesSaleId, bool isAdmin)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return _log.Refuse<SaleDto>(_loc["Add at least one item."]);
        if (dto.CustomerId == Guid.Empty)
            return _log.Refuse<SaleDto>(_loc["Customer is required."]);

        // Checked up front, like every other counterparty: without this a bogus id only
        // failed at save time as an FK violation, and a deactivated customer could still
        // be invoiced — the one gap in the app's otherwise consistent inactive guards.
        var customer = await _customers.GetByIdAsync(dto.CustomerId);
        if (customer == null)   return _log.Refuse<SaleDto>(_loc["Customer not found."]);
        if (!customer.IsActive) return _log.Refuse<SaleDto>(_loc["Customer '{0}' is inactive.", customer.Name]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<SaleDto>(_loc["Default branch not found."]);

        // Attribution is optional, but a supplied one must be real and still active —
        // checked up front, before the ledger is touched.
        Guid? salesPersonId = null;
        if (dto.SalesPersonId.HasValue)
        {
            var person = await _people.GetByIdAsync(dto.SalesPersonId.Value);
            if (person == null)
                return _log.Refuse<SaleDto>(_loc["Selected sales person not found."]);
            if (!person.IsActive)
                return _log.Refuse<SaleDto>(_loc["Sales person '{0}' is no longer active.", person.Name]);
            salesPersonId = person.Id;
        }

        // The picked date is the business's *local* calendar day. Backdating is allowed so a
        // day's paperwork can be entered late; a future date is refused, because posting this
        // takes the stock off the shelf right now and the PPN belongs to the period the sale
        // actually happened in — a sale dated forward would misstate both.
        //
        // Compared local-to-local, so there is no UTC/local skew to absorb (unlike the older
        // `> UtcNow.Date.AddDays(1)` guard used elsewhere, whose one-day grace exists purely
        // to work around that skew and, as a side effect, lets tomorrow through).
        var todayLocal  = DateTime.Now.Date;
        var pickedLocal = dto.SaleDate?.Date ?? todayLocal;
        if (pickedLocal > todayLocal)
            return _log.Refuse<SaleDto>(_loc["Sale date cannot be in the future."]);
        if (await _period.NewDateAsync(pickedLocal) is { } closed) return _log.Refuse<SaleDto>(closed);
        // Staff sell dated today or yesterday; yesterday is flagged below (G5, HC 2026-10-14):
        // a sale dated further back lands in a day whose cash and stock were already counted.
        if (!isAdmin && pickedLocal < todayLocal.AddDays(-1))
            return _log.Refuse<SaleDto>(_loc["Staff can date a sale today or yesterday only. Ask an administrator for an older date."]);

        // Today keeps the real clock time, so same-day ordering and EndOfDay are unchanged.
        // A backdated entry anchors to that day's local midnight, converted to UTC — SaleDate
        // stays a genuine UTC instant either way, which is what every .ToLocalTime() display
        // and the day-boundary reports already assume.
        var saleDate = pickedLocal == todayLocal
            ? DateTime.UtcNow
            : DateTime.SpecifyKind(pickedLocal, DateTimeKind.Local).ToUniversalTime();

        // Validate all items BEFORE touching ledger
        foreach (var item in dto.Items)
        {
            if (item.Qty <= 0) return _log.Refuse<SaleDto>(_loc["All quantities must be > 0."]);
            if (!Limits.IsWholeUnits(item.Qty)) return _log.Refuse<SaleDto>(_loc["Quantities must be whole units (no fractions)."]);
            if (item.UnitPrice < 0) return _log.Refuse<SaleDto>(_loc["Price cannot be negative."]);
            if (item.Qty > Limits.MaxQty) return _log.Refuse<SaleDto>(_loc["Quantity is too large (at most {0} per line).", Limits.MaxQty.ToString("N0")]);
            if (item.UnitPrice > Limits.MaxUnitAmount || item.UnitPrice * item.Qty > Limits.MaxLineAmount)
                return _log.Refuse<SaleDto>(_loc["Amount is too large (at most {0}).", Limits.MaxUnitAmount.ToString("N0")]);

            // A percentage discount is resolved to a per-unit amount here, before any
            // validation runs, so both entry modes go through exactly the same guards
            // and everything downstream only ever sees an amount.
            if (item.DiscountPercent.HasValue)
            {
                if (item.DiscountPercent < 0 || item.DiscountPercent >= 100)
                    return _log.Refuse<SaleDto>(_loc["Discount percent must be between 0 and 100."]);
                item.DiscountAmount = Math.Round(
                    item.UnitPrice * item.DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
            }

            if (item.DiscountAmount < 0) return _log.Refuse<SaleDto>(_loc["Discount cannot be negative."]);
            if (item.DiscountAmount >= item.UnitPrice && item.UnitPrice > 0)
                return _log.Refuse<SaleDto>(_loc["Discount cannot equal or exceed unit price."]);

            // Sanitize text fields
            item.Notes       = SanitiseText(item.Notes, 500);
            item.PriceReason = SanitiseText(item.PriceReason, 300);

            var p = await _products.GetByIdAsync(item.ProductId);
            if (p == null)   return _log.Refuse<SaleDto>(_loc["Product not found."]);
            if (!p.IsActive) return _log.Refuse<SaleDto>(_loc["Product '{0}' is inactive.", p.Name]);
            // A price different from the master price no longer needs a reason (HC,
            // 2026-10-05). When one is given it is still stored, audited and shown on the
            // sale screen — but never printed.
        }

        // Concurrent sales of the same product wait here for each other (see LockProductsAsync),
        // so the stock read below already includes whatever the other sale took.
        await _inventory.LockStockAsync(dto.Items.Select(i => i.ProductId));

        // Stock is checked per product across all lines, not per line: nothing is saved until
        // the end, so StockOutAsync sees the same on-hand figure for every line, and two lines
        // of one product (3 + 3 with 4 in stock) each passed and took stock negative.
        foreach (var g in dto.Items.GroupBy(i => i.ProductId).Where(g => g.Count() > 1))
        {
            var wanted    = g.Sum(i => i.Qty);
            var available = await _inventory.GetCurrentStockAsync(g.Key);
            if (available < wanted)
                return _log.Refuse<SaleDto>(_loc["Insufficient stock. Available: {0}, Requested: {1}",
                                                 available.ToString("N2"), wanted.ToString("N2")]);
        }

        var saleId   = Guid.NewGuid();
        var saleItems = new List<SaleItem>();

        // Stock out each item (validates stock, inserts ledger — no SaveChanges yet)
        foreach (var itemDto in dto.Items)
        {
            var stockResult = await _inventory.StockOutAsync(itemDto.ProductId, itemDto.Qty, saleId, branch.Id, saleDate);
            if (!stockResult.Success) return _log.Refuse<SaleDto>(stockResult.Error!);

            var product  = await _products.GetByIdAsync(itemDto.ProductId);
            var cost     = await _inventory.GetCurrentAvgCostAsync(itemDto.ProductId);
            int? wMonths = itemDto.WarrantyMonths ?? product!.DefaultWarrantyMonths;

            saleItems.Add(new SaleItem {
                Id             = Guid.NewGuid(),
                SaleId         = saleId,
                ProductId      = itemDto.ProductId,
                Qty            = itemDto.Qty,
                UnitPrice      = itemDto.UnitPrice,
                DiscountAmount = itemDto.DiscountAmount,
                DiscountPercent= itemDto.DiscountPercent,
                LineTotal      = (itemDto.UnitPrice - itemDto.DiscountAmount) * itemDto.Qty,
                CostAtSale     = cost,
                WarrantyMonths = wMonths,
                WarrantyExpiry = wMonths > 0 ? saleDate.AddMonths(wMonths!.Value) : null,
                Notes          = itemDto.Notes,
                PriceReason    = itemDto.PriceReason,
                Product        = product
            });
        }

        // Net of line-level discounts, before the invoice-level discount and PPN.
        var lineNetTotal = saleItems.Sum(i => i.LineTotal);

        // ── Invoice-level discount ─────────────────────────────────────────────────
        // Resolved to an amount here (same rule as line discounts: a supplied percent
        // wins and the amount is recomputed, so a tampered client value can't stick).
        var invoiceDiscount = dto.InvoiceDiscountAmount;
        if (dto.InvoiceDiscountPercent.HasValue)
        {
            if (dto.InvoiceDiscountPercent < 0 || dto.InvoiceDiscountPercent >= 100)
                return _log.Refuse<SaleDto>(_loc["Invoice discount percent must be between 0 and 100."]);
            invoiceDiscount = Math.Round(
                lineNetTotal * dto.InvoiceDiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
        }
        if (invoiceDiscount < 0)
            return _log.Refuse<SaleDto>(_loc["Invoice discount cannot be negative."]);
        if (invoiceDiscount >= lineNetTotal && lineNetTotal > 0)
            return _log.Refuse<SaleDto>(_loc["Invoice discount ({0}) cannot equal or exceed the total ({1}).", invoiceDiscount.ToString("N0"), lineNetTotal.ToString("N0")]);

        AllocateInvoiceDiscount(saleItems, invoiceDiscount, lineNetTotal);

        var netTotal = lineNetTotal - invoiceDiscount;

        // ── PPN ────────────────────────────────────────────────────────────────────
        // Rate is snapshotted onto the sale, so later changes to AppSettings.VatRate
        // never retroactively alter posted invoices.
        var taxRate = (await _settings.GetAsync()).VatRate;
        decimal taxBase, taxAmount, grandTotal;

        if (taxRate <= 0m)
        {
            taxBase = netTotal; taxAmount = 0m; grandTotal = netTotal;
        }
        else if (dto.IsTaxInclusive)
        {
            // Quoted price already contains PPN — extract it backwards.
            // TaxAmount is derived by subtraction so base + tax always reconciles to
            // the total exactly, with no rounding drift.
            grandTotal = netTotal;
            taxBase    = Math.Round(netTotal / (1m + taxRate), 2, MidpointRounding.AwayFromZero);
            taxAmount  = netTotal - taxBase;
        }
        else
        {
            // PPN added on top of the quoted price.
            taxBase    = netTotal;
            taxAmount  = Math.Round(netTotal * taxRate, 2, MidpointRounding.AwayFromZero);
            grandTotal = taxBase + taxAmount;
        }

        // ── Credit term ────────────────────────────────────────────────────────────
        // The selected PaymentTerm is the only source of a due date. A credit sale with
        // no term chosen is open credit with no agreed date, which is legitimate — it
        // just leaves DueDate null so ageing reports don't invent a deadline.
        Guid?     termId  = null;
        DateTime? dueDate = null;

        if (dto.PaymentType != PaymentType.Cash && dto.PaymentTermId.HasValue)
        {
            var term = await _terms.GetByIdAsync(dto.PaymentTermId.Value);
            if (term == null)
                return _log.Refuse<SaleDto>(_loc["Selected payment term not found."]);
            if (!term.IsActive)
                return _log.Refuse<SaleDto>(_loc["Payment term '{0}' is no longer active.", term.Name]);

            termId  = term.Id;
            // Counted from the sale's own date, not from today — backdating a credit sale
            // must not silently shorten the customer's agreed term. (Purchase already
            // counts from the supplier's invoice date for the same reason.)
            dueDate = pickedLocal.AddDays(term.DueDays);
        }

        // ── Perlu dicek (Staff only; HC 2026-10-14) ─────────────────────────────────
        // The sale posts as normal; Admin sees why it was flagged. Compared per unit after
        // every discount (line and invoice), on the same footing as the master price; cost is
        // ex PPN, so an inclusive price is taken ex PPN for the cost test.
        var reasons = new List<string>();
        if (!isAdmin)
        {
            var limit = (await _settings.GetAsync()).StaffDiscountLimitPercent;
            foreach (var i in saleItems)
            {
                var netUnit = (i.LineTotal - i.AllocatedInvoiceDiscount) / i.Qty;
                var netExTax = dto.IsTaxInclusive && taxRate > 0 ? netUnit / (1m + taxRate) : netUnit;
                var name = i.Product?.Name ?? "";
                if (i.CostAtSale > 0 && netExTax < i.CostAtSale)
                    reasons.Add(SaleReview.Line(SaleReview.BelowCost, name));
                var master = i.Product?.UnitPrice ?? 0m;
                if (master > 0)
                {
                    var off = Math.Round((1m - netUnit / master) * 100m, 2, MidpointRounding.AwayFromZero);
                    if (off > limit) reasons.Add(SaleReview.Line(SaleReview.Discount, name, off.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            if (pickedLocal < todayLocal)
                reasons.Add(SaleReview.Line(SaleReview.Backdated, pickedLocal.ToString("yyyy-MM-dd")));
            if (salesPersonId != customer.SalesPersonId)
            {
                var chosen = salesPersonId is { } spId ? (await _people.GetByIdAsync(spId))?.Name ?? "-" : "-";
                var own    = customer.SalesPersonId is { } ownId ? (await _people.GetByIdAsync(ownId))?.Name ?? "-" : "-";
                reasons.Add(SaleReview.Line(SaleReview.SalesPerson, chosen, own));
            }
        }

        // ── Credit limit and overdue (HC 2026-10-15) ───────────────────────────────
        // Credit sales only: a cash sale extends no credit. A Staff sale over the plafon, or to a
        // customer with an invoice more than the grace days past due, still posts but is flagged
        // for Admin; Admin was already warned on the page, so theirs posts as is and the audit
        // line keeps the fact. Two Staff sales to one customer queue at the lock, so the second
        // one's balance includes the first. On a revision the original is already cancelled in
        // this transaction, so it is not counted twice.
        var creditNotes = new List<string>();
        if (dto.PaymentType != PaymentType.Cash)
        {
            if (!isAdmin) await _sales.LockCustomerCreditAsync(customer.Id);
            var credit = await GetCreditStatusAsync(customer.Id);
            if (credit.WouldExceed(grandTotal))
                creditNotes.Add(SaleReview.Line(SaleReview.CreditLimit, Money(credit.CreditLimit!.Value), Money(credit.Open + grandTotal)));
            if (credit.HasOverdue)
                creditNotes.Add(SaleReview.Line(SaleReview.Overdue, credit.OverdueCount.ToString(),
                    credit.OldestOverdueInvoice ?? "", credit.OldestDaysLate.ToString()));
            if (!isAdmin) reasons.AddRange(creditNotes);
        }

        // Build audit detail — note any price overrides
        var overrides = saleItems
            .Where(i => !string.IsNullOrEmpty(i.PriceReason))
            .Select(i => $"{i.Product?.Name}:{i.PriceReason}")
            .ToList();

        var sale = new Sale {
            Id            = saleId,
            InvoiceNumber = await _sales.GenerateInvoiceNumberAsync(),
            SaleDate      = saleDate,
            CustomerId    = dto.CustomerId,
            BranchId      = branch.Id,
            PaymentType   = dto.PaymentType,
            PaymentTermId = termId,
            SalesPersonId = salesPersonId,
            ReplacesSaleId = replacesSaleId,
            DueDate       = dueDate,
            SubTotal      = saleItems.Sum(i => i.UnitPrice * i.Qty),
            DiscountTotal = saleItems.Sum(i => i.DiscountAmount * i.Qty),
            InvoiceDiscountAmount  = invoiceDiscount,
            InvoiceDiscountPercent = dto.InvoiceDiscountPercent,
            TaxBase        = taxBase,
            TaxRate        = taxRate,
            TaxAmount      = taxAmount,
            IsTaxInclusive = dto.IsTaxInclusive,
            GrandTotal    = grandTotal,
            AmountPaid    = dto.PaymentType == PaymentType.Cash ? grandTotal : 0,
            Status        = SaleStatus.Active,
            Notes         = dto.Notes,
            CreatedBy     = user,
            CreatedAt     = DateTime.UtcNow,
            NeedsReview   = reasons.Count > 0,
            ReviewReasons = reasons.Count > 0 ? string.Join('\n', reasons) : null,
            SubmissionId  = dto.SubmissionId is { } sub && sub != Guid.Empty ? sub : null,
            SaleItems     = saleItems
        };

        await _sales.AddAsync(sale);

        // A cash sale is collected in full at creation, so commission accrues now.
        // Credit sales accrue later, per payment, in RecordPaymentAsync.
        if (sale.PaymentType == PaymentType.Cash)
            await _commissions.AccrueForCashSaleAsync(sale, saleItems);

        // Audit
        var auditDetail = sale.InvoiceNumber +
            (overrides.Any() ? " | PriceOverrides: " + string.Join("; ", overrides) : "") +
            (sale.NeedsReview ? " | Perlu dicek: " + string.Join("; ", reasons) : "") +
            (isAdmin && creditNotes.Count > 0 ? " | Credit warning (Admin went ahead): " + string.Join("; ", creditNotes) : "");
        await _audit.LogAsync(user, "Sale.Create", auditDetail);

        _log.LogInformation(
            "Sale {InvoiceNumber} posted — customer {Customer}, {LineCount} line(s), " +
            "{GrandTotal} gross ({PaymentType}), {PriceOverrideCount} price override(s), by {User}",
            sale.InvoiceNumber, customer.Name, saleItems.Count, sale.GrandTotal,
            sale.PaymentType, overrides.Count, user);

        await _uow.SaveChangesAsync();

        var created = await _sales.GetByIdWithItemsAsync(saleId);
        return ServiceResult<SaleDto>.Ok(MapDto(created!));
    }

    public async Task<ServiceResult> CancelAsync(Guid saleId, string user, bool isAdmin, string? reason)
    {
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1),
        // and per salesperson with commission on it, so a payout can't settle rows while this
        // voids or claws them back. Both before any read: LockAsync forgets what was read before.
        await _uow.LockAsync(saleId);
        await _uow.LockAsync(await _commissions.SalesPeopleForSaleAsync(saleId));
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return _log.Refuse(_loc["Sale not found."]);
        if (sale.Status == SaleStatus.Cancelled) return _log.Refuse(_loc["Sale is already cancelled."]);
        if (StaffChangeRefusal(sale, isAdmin, reason) is { } staffNo) return _log.Refuse(staffNo);
        // Also covers Revise, which cancels the original through here.
        if (await _period.ExistingAsync(sale.InvoiceNumber, PeriodLock.LocalDay(sale.SaleDate)) is { } closed)
            return _log.Refuse(closed);

        // Money already received belongs to this invoice. Cancelling used to go through and
        // leave the payment attached to a cancelled invoice, still counted as collected, with
        // nothing recording that it was owed back (HC, 2026-10-05). Same rule as Revise.
        if (await _pending.AnyPendingAsync(saleId))
            return _log.Refuse(_loc["A payment on {0} is waiting for verification. Verify or reject it first.", sale.InvoiceNumber]);
        if (sale.PaymentType == PaymentType.Due && sale.AmountPaid > 0)
            return _log.Refuse(_loc["Payments have been recorded against {0}, so it cannot be cancelled. Correct it with a return or a credit note instead.", sale.InvoiceNumber]);

        // Cancelling restocks every unit sold. If a return has already put some of them
        // back, doing that would receive the same goods twice and leave a credit note
        // hanging against an invoice that no longer exists.
        if (await _returns.HasActiveReturnAsync(saleId))
            return _log.Refuse(_loc["This invoice has a sales return against it. Cancel the return first — cancelling the invoice now would put the returned goods back into stock twice."]);

        // Same shape of problem as a recorded payment: a credit note applied here is
        // reducing this invoice's balance, and cancelling would leave that application
        // pointing at a document that no longer exists.
        if (await _noteApplications.HasLiveApplicationsForSaleAsync(saleId))
            return _log.Refuse(_loc["A credit note has been applied to this invoice. Reverse the application first — cancelling now would leave it reducing a balance that no longer exists."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        foreach (var item in sale.SaleItems)
            await _inventory.StockInForCancelAsync(item.ProductId, item.Qty, item.CostAtSale, saleId, branch.Id);

        sale.Status = SaleStatus.Cancelled;
        sale.CancelledAt  = DateTime.UtcNow;
        sale.CancelledBy  = user;
        sale.CancelReason = Sanitise(reason, 500);
        _sales.Update(sale);

        // Void (never delete) any unpaid commission this sale accrued, and claw back what was
        // already paid out from the salesperson's next payout (HC, 2026-10-18).
        await _commissions.VoidForSaleAsync(saleId);
        await _commissions.ClawBackPaidForCancelledSaleAsync(saleId, user);

        await _audit.LogAsync(user, "Sale.Cancel", sale.InvoiceNumber + (sale.CancelReason is { } why ? $" | {why}" : ""));
        // Worth Information rather than Debug: a cancellation reverses stock and voids
        // commission, so it is the event most likely to be behind "the numbers moved and
        // nobody knows why".
        _log.LogInformation(
            "Sale {InvoiceNumber} cancelled — {LineCount} line(s) restocked, {GrandTotal} reversed, by {User}",
            sale.InvoiceNumber, sale.SaleItems.Count, sale.GrandTotal, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user, bool isAdmin)
    {
        if (dto.Amount <= 0)
            return _log.Refuse<PaymentRecordDto>(_loc["Payment amount must be > 0."]);

        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(dto.SaleId);
        var sale = await _sales.GetByIdWithItemsAsync(dto.SaleId);
        if (sale == null) return _log.Refuse<PaymentRecordDto>(_loc["Sale not found."]);
        if (sale.Status == SaleStatus.Cancelled)
            return _log.Refuse<PaymentRecordDto>(_loc["Cannot record payment on a cancelled sale."]);

        // Allow payment on Due AND all TOP terms
        if (sale.PaymentType == PaymentType.Cash)
            return _log.Refuse<PaymentRecordDto>(_loc["This is a Cash sale — payment already collected."]);

        // Net of applied credit notes, not just of cash already received: a note has
        // already reduced what this customer actually still owes, so the gross balance
        // would invite a payment bigger than the debt.
        var currentBalance = sale.GrandTotal - sale.AmountPaid
                           - await _noteApplications.GetAppliedTotalForSaleAsync(sale.Id);
        // Payments Staff recorded and Admin hasn't verified yet are money already claimed:
        // a second claim can't take the invoice past what is owed.
        var pendingTotal = await _pending.GetPendingTotalAsync(sale.Id);
        if (dto.Amount > currentBalance - pendingTotal)
            return _log.Refuse<PaymentRecordDto>(_loc["Amount ({0}) exceeds balance due ({1}).", dto.Amount.ToString("N0"), Math.Max(0, currentBalance - pendingTotal).ToString("N0")]);

        var (receivedAt, dateNo) = await ReceivedAtAsync(dto.ReceivedOn, new[] { sale });
        if (dateNo != null) return _log.Refuse<PaymentRecordDto>(dateNo);

        // Staff: pending until Admin verifies the money arrived (R1, HC 2026-10-16). Until then it
        // changes nothing: not the balance, not commission, not End of Day's cash.
        if (!isAdmin)
        {
            var p = new PendingPayment {
                Id = Guid.NewGuid(), SaleId = sale.Id, Amount = dto.Amount, Notes = SanitiseText(dto.Notes, 300),
                ReceivedAt = receivedAt, CreatedBy = user, Status = PendingPaymentStatus.Pending };
            await _pending.AddAsync(p);
            await _audit.LogAsync(user, "Payment.Pending", $"{sale.InvoiceNumber} +{dto.Amount:N0} (waiting for verification)");
            _log.LogInformation("Payment {Amount} on {InvoiceNumber} recorded by {User}, waiting for verification",
                dto.Amount, sale.InvoiceNumber, user);
            await _uow.SaveChangesAsync();
            return ServiceResult<PaymentRecordDto>.Ok(new PaymentRecordDto {
                Id = p.Id, PaymentDate = p.ReceivedAt, Amount = p.Amount, Notes = p.Notes, CreatedBy = p.CreatedBy, Pending = true });
        }

        var record = await RecordPaymentCoreAsync(sale, dto.Amount, dto.Notes, batchId: null, user, paidAt: receivedAt);

        await _audit.LogAsync(user, "Payment.Record", $"{sale.InvoiceNumber} +{dto.Amount:N0}{ReceivedNote(receivedAt)}");
        _log.LogInformation(
            "Payment {Amount} received against {InvoiceNumber} — paid {AmountPaid} of {GrandTotal}, by {User}",
            dto.Amount, sale.InvoiceNumber, sale.AmountPaid, sale.GrandTotal, user);
        await _uow.SaveChangesAsync();

        return ServiceResult<PaymentRecordDto>.Ok(new PaymentRecordDto {
            Id = record.Id, PaymentDate = record.PaymentDate,
            Amount = record.Amount, Notes = record.Notes, CreatedBy = record.CreatedBy
        });
    }

    /// <summary>
    /// Applies one payment to one sale: the payment row, the balance increment, and the
    /// commission that collection earns. Does NOT SaveChanges — the caller owns the
    /// transaction, so a single payment and a multi-invoice settlement both commit once.
    /// </summary>
    private async Task<PaymentRecord> RecordPaymentCoreAsync(
        Sale sale, decimal amount, string? notes, Guid? batchId, string user, decimal cashShare = 1m, DateTime? paidAt = null)
    {
        var record = new PaymentRecord {
            Id             = Guid.NewGuid(),
            SaleId         = sale.Id,
            PaymentDate    = paidAt ?? DateTime.UtcNow,
            Amount         = amount,
            Notes          = notes?.Trim(),
            CreatedBy      = user,
            PaymentBatchId = batchId
        };

        await _payments.AddAsync(record);
        sale.AmountPaid += amount;
        _sales.Update(sale);

        // Commission accrues on collection, prorated to this payment's share of the invoice,
        // and only on the cash in it (G7, HC 2026-10-14): in a settlement netted with credit
        // notes, the note-funded part earns nothing, the same as applying the note directly.
        await _commissions.AccrueForPaymentAsync(sale, record, Math.Round(amount * cashShare, 2, MidpointRounding.AwayFromZero));
        return record;
    }

    /// <summary>
    /// When the money arrived (HC, 2026-10-21). Payments used to be dated the moment they were
    /// typed in, so a week of transfers entered on one afternoon all landed on that day: End of
    /// Day, commission dates and how late each customer really paid were all wrong. Today keeps
    /// the clock time; an earlier day is that day's local midnight (LedgerDate, as sales do).
    /// Not in the future, not before any invoice it pays, not in a closed month.
    /// </summary>
    private async Task<(DateTime At, string? Error)> ReceivedAtAsync(DateTime? localDay, IEnumerable<Sale> sales)
    {
        if (localDay is not { } day || day.Date == DateTime.Now.Date) return (DateTime.UtcNow, null);
        day = day.Date;
        if (day > DateTime.Now.Date)
            return (default, _loc["The date received can't be in the future."]);
        foreach (var s in sales)
            if (day < PeriodLock.LocalDay(s.SaleDate))
                return (default, _loc["{0} is dated {1}: the money can't have been received before the invoice.", s.InvoiceNumber, PeriodLock.LocalDay(s.SaleDate).ToString("dd MMM yyyy")]);
        if (await _period.NewDateAsync(day) is { } closed) return (default, closed);
        return (LedgerDate.FromLocalDay(day), null);
    }

    /// <summary>" (received 13 Oct 2026)" for the audit line when the date isn't today.</summary>
    private static string ReceivedNote(DateTime receivedAtUtc)
    {
        var day = PeriodLock.LocalDay(receivedAtUtc);
        return day == DateTime.Now.Date ? "" : $" (received {day:dd MMM yyyy})";
    }

    public async Task<ServiceResult<PaymentBatchDto>> RecordBatchPaymentAsync(
        RecordSaleBatchPaymentDto dto, string user)
    {
        var lines = (dto.Lines ?? new()).Where(l => l.Amount > 0).ToList();
        var noteIds = (dto.ApplyCreditNoteIds ?? new()).Distinct().ToList();

        if (lines.Count == 0 && noteIds.Count == 0)
            return _log.Refuse<PaymentBatchDto>(_loc["Enter an amount on at least one invoice."]);

        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(lines.Select(l => l.SaleId).Concat(noteIds).ToArray());

        var customer = await _customers.GetByIdAsync(dto.CustomerId);
        if (customer == null) return _log.Refuse<PaymentBatchDto>(_loc["Customer not found."]);

        // One invoice can only appear once. Without this, two lines could each pass a
        // balance check read before either was applied, and jointly overpay.
        if (lines.GroupBy(l => l.SaleId).Any(g => g.Count() > 1))
            return _log.Refuse<PaymentBatchDto>(_loc["The same invoice appears twice — combine it into one row."]);

        // ── Validate every invoice line before anything is written ────────────────
        var sales = await _sales.GetByIdsWithItemsAsync(lines.Select(l => l.SaleId));
        var byId  = sales.ToDictionary(s => s.Id);
        // Fetched once for the whole batch rather than per line inside the loop.
        var appliedBySale = await _noteApplications.GetAppliedTotalsForSalesAsync(lines.Select(l => l.SaleId));

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.SaleId, out var sale))
                return _log.Refuse<PaymentBatchDto>(_loc["An invoice on this settlement no longer exists."]);
            if (sale.CustomerId != dto.CustomerId)
                return _log.Refuse<PaymentBatchDto>(_loc["Invoice {0} belongs to a different customer.", sale.InvoiceNumber]);
            if (sale.Status == SaleStatus.Cancelled)
                return _log.Refuse<PaymentBatchDto>(_loc["Invoice {0} is cancelled.", sale.InvoiceNumber]);
            if (sale.PaymentType == PaymentType.Cash)
                return _log.Refuse<PaymentBatchDto>(_loc["Invoice {0} is a cash sale — already collected.", sale.InvoiceNumber]);

            // Net of notes already applied to this specific invoice — separate from, and
            // additional to, whatever notes get ticked into this settlement below.
            var balance = sale.GrandTotal - sale.AmountPaid - appliedBySale.GetValueOrDefault(sale.Id);
            if (line.Amount > balance)
                return _log.Refuse<PaymentBatchDto>(_loc["Invoice {0}: {1} exceeds its balance of {2}.", sale.InvoiceNumber, line.Amount.ToString("N0"), balance.ToString("N0")]);
        }

        var (receivedAt, dateNo) = await ReceivedAtAsync(dto.ReceivedOn, lines.Select(l => byId[l.SaleId]));
        if (dateNo != null) return _log.Refuse<PaymentBatchDto>(dateNo);

        // ── Validate every note before anything is written ────────────────────────
        var notes = noteIds.Count == 0 ? new List<CreditNote>() : await _notes.GetByIdsAsync(noteIds);
        if (notes.Count != noteIds.Count)
            return _log.Refuse<PaymentBatchDto>(_loc["A credit note on this settlement no longer exists."]);

        foreach (var note in notes)
        {
            if (note.Type != CreditDebitType.Credit)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} is a debit note and cannot be applied to money received.", note.DocumentNumber]);
            if (note.CustomerId != dto.CustomerId)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} belongs to a different customer.", note.DocumentNumber]);
            if (note.Status != CreditNoteStatus.Open)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} is already {1}.", note.DocumentNumber, _loc["CreditNoteStatus_" + note.Status].Value]);
            if (note.NeedsApproval)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} is waiting for an administrator's approval and cannot be used yet.", note.DocumentNumber]);
        }

        // ── Post ─────────────────────────────────────────────────────────────────
        var gross = lines.Sum(l => l.Amount);
        // Each note nets its *remaining* value, not its face value: part of it may already
        // be applied to a specific invoice, and that slice has already reduced that
        // invoice's balance. Counting the face value here would net it a second time.
        var appliedByNote = await _noteApplications.GetAppliedTotalsForNotesAsync(noteIds);
        var notesApplied  = notes.Sum(n => n.Amount - appliedByNote.GetValueOrDefault(n.Id));

        // A note is applied whole or not at all, so netting more credit than is being
        // collected would settle notes whose value this settlement can't absorb — quietly
        // destroying the customer's remaining credit. Refuse, and say what to change.
        if (notesApplied > gross)
            return _log.Refuse<PaymentBatchDto>(_loc["The selected credit notes ({0}) exceed the {1} being collected. Include more invoices, or untick a note and settle it against a later payment.", notesApplied.ToString("N0"), gross.ToString("N0")]);

        var batchNotes   = SanitiseText(dto.Notes, 500);

        var batch = new PaymentBatch {
            Id                 = Guid.NewGuid(),
            BatchNumber        = await _batches.GenerateBatchNumberAsync(PaymentBatchDirection.Received),
            Direction          = PaymentBatchDirection.Received,
            BatchDate          = receivedAt,
            CustomerId         = dto.CustomerId,
            GrossAmount        = gross,
            NotesAppliedAmount = notesApplied,
            NetAmount          = gross - notesApplied,
            Notes              = batchNotes,
            CreatedBy          = user,
            CreatedAt          = DateTime.UtcNow
        };
        await _batches.AddAsync(batch);

        // The cash share of this settlement: notes net part of the gross, and that part is not
        // cash collected, so it earns no commission (G7).
        var cashShare = gross > 0 ? (gross - notesApplied) / gross : 0m;
        foreach (var line in lines)
            await RecordPaymentCoreAsync(byId[line.SaleId], line.Amount, batchNotes, batch.Id, user, cashShare, receivedAt);

        // Tracked mutation — no Update() on entities EF is already following.
        foreach (var note in notes)
        {
            note.Status                  = CreditNoteStatus.Settled;
            note.SettledDate             = batch.BatchDate;
            note.SettledByPaymentBatchId = batch.Id;
            note.SettlementNotes         = $"Applied to settlement {batch.BatchNumber}";
        }

        await _audit.LogAsync(user, "PaymentBatch.Record",
            $"{batch.BatchNumber} | {customer.Name} | net {batch.NetAmount:N0} " +
            $"({gross:N0} gross − {notesApplied:N0} notes) across {lines.Count} invoice(s){ReceivedNote(receivedAt)}");
        _log.LogInformation(
            "Settlement {BatchNumber} received from {Customer} — {NetAmount} net " +
            "({Gross} gross less {NotesApplied} in notes) across {InvoiceCount} invoice(s) " +
            "and {NoteCount} note(s), by {User}",
            batch.BatchNumber, customer.Name, batch.NetAmount, gross, notesApplied,
            lines.Count, notes.Count, user);
        await _uow.SaveChangesAsync();

        return ServiceResult<PaymentBatchDto>.Ok(MapBatch(batch, customer.Name, lines.Count, notes.Count));
    }

    public async Task<PaymentStatementDto?> GetCustomerStatementAsync(
        Guid customerId, DateTime? from = null, DateTime? to = null)
    {
        var customer = await _customers.GetByIdAsync(customerId);
        if (customer == null) return null;

        var due = await _sales.GetDueSalesAsync(customerId);
        // The window narrows what's listed, never what's payable — a statement covering
        // "last month" still settles against whatever the customer chooses to pay.
        // SaleDate is UTC; the window is in local days (a 2 Oct sale is stored as 1 Oct 17:00).
        static DateTime LocalDay(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().Date;
        if (from.HasValue) due = due.Where(s => LocalDay(s.SaleDate) >= from.Value.Date).ToList();
        if (to.HasValue)   due = due.Where(s => LocalDay(s.SaleDate) <= to.Value.Date).ToList();

        // A note still waiting for Admin's approval can't be ticked into a settlement (G1).
        var openNotes = (await _notes.GetAllAsync(
            type: CreditDebitType.Credit, status: CreditNoteStatus.Open, customerId: customerId))
            .Where(n => !n.NeedsApproval).ToList();

        // Both sides net of what's already been applied per document: the invoices so each
        // line shows what's genuinely left, and the notes so ticking one nets what it can
        // actually still absorb rather than its face value.
        var appliedBySale = await _noteApplications.GetAppliedTotalsForSalesAsync(due.Select(s => s.Id));
        var appliedByNote = await _noteApplications.GetAppliedTotalsForNotesAsync(openNotes.Select(n => n.Id));

        return new PaymentStatementDto {
            CounterpartyId   = customer.Id,
            CounterpartyName = customer.Name,
            Phone            = customer.Phone,
            Lines = due.Select(s => new StatementLineDto {
                DocumentId        = s.Id,
                DocumentNumber    = s.InvoiceNumber,
                DocumentDate      = s.SaleDate,
                DueDate           = s.DueDate,
                GrandTotal        = s.GrandTotal,
                AmountPaid        = s.AmountPaid,
                AppliedNotesTotal = appliedBySale.GetValueOrDefault(s.Id)
            }).ToList(),
            OpenNotes = openNotes
                .Select(n => new StatementNoteDto {
                    CreditNoteId   = n.Id,
                    DocumentNumber = n.DocumentNumber,
                    NoteDate       = n.NoteDate,
                    Category       = n.Category.ToString(),
                    Amount         = n.Amount - appliedByNote.GetValueOrDefault(n.Id),
                    FaceAmount     = n.Amount,
                    Reason         = n.Reason
                })
                // A note fully consumed by per-invoice applications has nothing left to
                // tick, even though it is still technically Open until its last slice lands.
                .Where(n => n.Amount > 0)
                .ToList()
        };
    }

    private static PaymentBatchDto MapBatch(PaymentBatch b, string counterparty, int docCount, int noteCount) => new() {
        Id                 = b.Id,
        BatchNumber        = b.BatchNumber,
        Direction          = b.Direction.ToString(),
        IsReceived         = b.Direction == PaymentBatchDirection.Received,
        BatchDate          = b.BatchDate,
        CounterpartyName   = counterparty,
        GrossAmount        = b.GrossAmount,
        NotesAppliedAmount = b.NotesAppliedAmount,
        NetAmount          = b.NetAmount,
        DocumentCount      = docCount,
        NoteCount          = noteCount,
        Notes              = b.Notes,
        CreatedBy          = b.CreatedBy
    };

    public async Task<SaleDto?> GetByIdAsync(Guid id)
    {
        var s = await _sales.GetByIdWithItemsAsync(id);
        if (s == null) return null;
        var dto = MapDto(s);
        dto.AppliedNotesTotal = await _noteApplications.GetAppliedTotalForSaleAsync(id);
        if (s.ReplacesSaleId.HasValue)
            dto.ReplacesInvoiceNumber = await _sales.GetInvoiceNumberAsync(s.ReplacesSaleId.Value);
        if (await _sales.FindReplacementAsync(id) is { } r)
            (dto.ReplacedBySaleId, dto.ReplacedByInvoiceNumber) = (r.Id, r.InvoiceNumber);
        return dto;
    }

    public async Task<InvoiceReceiptDto?> GetInvoiceReceiptAsync(Guid customerId, DateTime from, DateTime to)
    {
        var customer = await _customers.GetByIdAsync(customerId);
        if (customer == null) return null;

        // The dates are the operator's calendar days; SaleDate is stored in UTC. The upper
        // bound is the start of the day after, excluded, so a sale at 23:59 on the last day
        // is in and one at 00:00 the next day is not.
        var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Local).ToUniversalTime();
        var toUtc   = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        var sales = (await _sales.GetAllAsync(fromUtc, toUtc, customerId))
            .Where(s => s.SaleDate < toUtc && s.Status != SaleStatus.Cancelled)
            .OrderBy(s => s.SaleDate).ThenBy(s => s.InvoiceNumber)
            .ToList();
        var applied = await _noteApplications.GetAppliedTotalsForSalesAsync(sales.Select(s => s.Id));
        // The full invoices, mapped exactly as the invoice print maps them, so both documents
        // show the same lines and figures. One query each; a receipt covers a handful.
        var details = new Dictionary<Guid, SaleDto>();
        foreach (var s in sales)
            if (await _sales.GetByIdWithItemsAsync(s.Id) is { } full) details[s.Id] = MapDto(full);

        return new InvoiceReceiptDto {
            Details         = details,
            CustomerId      = customer.Id,
            CustomerName    = customer.Name,
            CustomerAddress = customer.Address,
            CustomerPhone   = customer.Phone,
            From            = from.Date,
            To              = to.Date,
            Invoices = sales.Select(s => new SaleListDto {
                Id                = s.Id,
                InvoiceNumber     = s.InvoiceNumber,
                SaleDate          = s.SaleDate,
                CustomerId        = s.CustomerId,
                CustomerName      = customer.Name,
                SalesPersonName   = s.SalesPerson?.Name ?? "",
                PaymentType       = s.PaymentType.ToString(),
                PaymentTermName   = s.PaymentTerm?.Name ?? "",
                DueDate           = s.DueDate,
                GrandTotal        = s.GrandTotal,
                AmountPaid        = s.AmountPaid,
                AppliedNotesTotal = applied.GetValueOrDefault(s.Id),
                Status            = s.Status.ToString()
            }).ToList()
        };
    }

    public async Task<List<SaleListDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var list = await _sales.GetAllAsync(from, to);
        if (!string.IsNullOrWhiteSpace(search))
            list = list.Where(s =>
                s.InvoiceNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Customer?.Name  ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Customer?.Phone ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        // One query for the page, so the balance and overdue columns are net of notes.
        var applied = await _noteApplications.GetAppliedTotalsForSalesAsync(list.Select(s => s.Id));

        return list.Select(s => new SaleListDto {
            AppliedNotesTotal = applied.GetValueOrDefault(s.Id),
            Id            = s.Id,
            InvoiceNumber = s.InvoiceNumber,
            SaleDate      = s.SaleDate,
            CustomerId    = s.CustomerId,
            CustomerName  = s.Customer?.Name ?? "",
            SalesPersonName = s.SalesPerson?.Name ?? "",
            PaymentType   = s.PaymentType.ToString(),
            PaymentTermName = s.PaymentTerm?.Name ?? "",
            DueDate       = s.DueDate,
            GrandTotal    = s.GrandTotal,
            AmountPaid    = s.AmountPaid,
            Status        = s.Status.ToString(),
            NeedsReview   = s.NeedsReview,
            CancelledAt   = s.CancelledAt,
            CancelledBy   = s.CancelledBy,
            CancelReason  = s.CancelReason
        }).ToList();
    }

    // ── Perlu dicek and today's cancellations (Admin) ─────────────────────────
    public async Task<List<SaleListDto>> GetNeedingReviewAsync() =>
        (await _sales.GetNeedingReviewAsync()).Select(ListRow).ToList();

    public async Task<List<SaleListDto>> GetCancelledTodayAsync() =>
        (await _sales.GetCancelledSinceAsync(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Local).ToUniversalTime()))
        .Select(ListRow).ToList();

    private static SaleListDto ListRow(Sale s) => new() {
        Id = s.Id, InvoiceNumber = s.InvoiceNumber, SaleDate = s.SaleDate, CustomerId = s.CustomerId,
        CustomerName = s.Customer?.Name ?? "", SalesPersonName = s.SalesPerson?.Name ?? "",
        PaymentType = s.PaymentType.ToString(), GrandTotal = s.GrandTotal, AmountPaid = s.AmountPaid,
        Status = s.Status.ToString(), NeedsReview = s.NeedsReview,
        CancelledAt = s.CancelledAt, CancelledBy = s.CancelledBy, CancelReason = s.CancelReason };

    public async Task<ServiceResult> MarkReviewedAsync(Guid saleId, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can mark a sale as checked."]);
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(saleId);
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return _log.Refuse(_loc["Sale not found."]);
        if (!sale.NeedsReview) return _log.Refuse(_loc["{0} is already checked.", sale.InvoiceNumber]);
        sale.NeedsReview = false; sale.ReviewedBy = user; sale.ReviewedAt = DateTime.UtcNow;
        await _audit.LogAsync(user, "Sale.Review", sale.InvoiceNumber);
        _log.LogInformation("Sale {InvoiceNumber} checked by {User}", sale.InvoiceNumber, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<DueCustomerDto>> GetDueSummaryAsync()
    {
        var dueSales = await _sales.GetDueSalesAsync();
        // One query for the whole list, not one per invoice.
        var applied  = await _noteApplications.GetAppliedTotalsForSalesAsync(dueSales.Select(s => s.Id));

        // Same six buckets and the same boundary dates as
        // SaleRepository.GetReceivablesAgingAsync — deliberately, so the per-customer
        // rows here sum back to the whole-ledger figure that report produces.
        var today = DateTime.UtcNow.Date;
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        return dueSales
            .Select(s => new {
                Sale = s,
                Net  = s.GrandTotal - s.AmountPaid - applied.GetValueOrDefault(s.Id)
            })
            .GroupBy(x => x.Sale.CustomerId)
            .Select(g => new DueCustomerDto {
                CustomerId   = g.Key,
                CustomerName = g.First().Sale.Customer?.Name ?? "",
                Phone        = g.First().Sale.Customer?.Phone,
                CreditLimit  = g.First().Sale.Customer?.CreditLimit,
                OpenInvoices = g.Count(),
                TotalDue     = g.Sum(x => x.Net),
                Aging        = new AgingBucketsDto {
                    NoDueDate  = g.Where(x => x.Sale.DueDate == null).Sum(x => x.Net),
                    NotYetDue  = g.Where(x => x.Sale.DueDate != null && x.Sale.DueDate!.Value.Date >= today).Sum(x => x.Net),
                    Days1To30  = g.Where(x => x.Sale.DueDate != null && x.Sale.DueDate!.Value.Date <  today && x.Sale.DueDate!.Value.Date >= d30).Sum(x => x.Net),
                    Days31To60 = g.Where(x => x.Sale.DueDate != null && x.Sale.DueDate!.Value.Date <  d30   && x.Sale.DueDate!.Value.Date >= d60).Sum(x => x.Net),
                    Days61To90 = g.Where(x => x.Sale.DueDate != null && x.Sale.DueDate!.Value.Date <  d60   && x.Sale.DueDate!.Value.Date >= d90).Sum(x => x.Net),
                    Days90Plus = g.Where(x => x.Sale.DueDate != null && x.Sale.DueDate!.Value.Date <  d90).Sum(x => x.Net)
                }
            })
            .OrderByDescending(d => d.HasOverdue)
            .ThenByDescending(d => d.TotalDue)
            .ToList();
    }

    // ── Reversing a payment entered wrong (HC, 2026-10-21) ───────────────────

    public async Task<ServiceResult> ReversePaymentAsync(Guid paymentId, string? reason, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can reverse a payment."]);
        var why = SanitiseText(reason, 300);
        if (string.IsNullOrWhiteSpace(why)) return _log.Refuse(_loc["Say why the payment is being reversed."]);

        var first = await _payments.GetByIdAsync(paymentId);
        if (first == null) return _log.Refuse(_loc["Payment not found."]);
        // The invoice, the settlement it may belong to (two lines of one settlement change the
        // same totals) and the salespeople (so a payout can't settle the rows mid-change).
        // LockAsync forgets what was read before it, so everything is read again below.
        await _uow.LockAsync(first.SaleId, first.PaymentBatchId ?? Guid.Empty);
        await _uow.LockAsync(await _commissions.SalesPeopleForSaleAsync(first.SaleId));

        var payment = await _payments.GetByIdAsync(paymentId);
        if (payment == null) return _log.Refuse(_loc["Payment not found."]);
        if (payment.IsReversed) return _log.Refuse(_loc["This payment has already been reversed."]);
        var sale = await _sales.GetByIdWithItemsAsync(payment.SaleId);
        if (sale == null) return _log.Refuse(_loc["Sale not found."]);
        if (sale.Status == SaleStatus.Cancelled) return _log.Refuse(_loc["Sale is already cancelled."]);

        var paidDay = PeriodLock.LocalDay(payment.PaymentDate);
        if (await _period.ExistingAsync(_loc["The payment of {0}", sale.InvoiceNumber].Value, paidDay) is { } closed)
            return _log.Refuse(closed);
        // A return's commission clawback was figured on what this payment earned; taking the
        // payment away under it would leave that clawback too big. Same order as cancelling.
        if (await _returns.HasActiveReturnAsync(sale.Id))
            return _log.Refuse(_loc["This invoice has a sales return against it. Cancel the return first, then reverse the payment."]);
        // A settlement that netted credit notes settled those notes with this money: taking one
        // line back would leave notes settled by cash that never came. Without notes, the
        // settlement's totals simply go down by this line.
        if (payment.PaymentBatch is { NotesAppliedAmount: > 0 } nb)
            return _log.Refuse(_loc["This payment is part of settlement {0}, which netted credit notes, so it can't be reversed on its own.", nb.BatchNumber]);
        if (sale.AmountPaid < payment.Amount)
            return _log.Refuse(_loc["The invoice's paid amount ({0}) is less than this payment ({1}). Nothing was changed; check the invoice.", sale.AmountPaid.ToString("N0"), payment.Amount.ToString("N0")]);

        payment.ReversedAt    = DateTime.UtcNow;
        payment.ReversedBy    = user;
        payment.ReverseReason = why;
        sale.AmountPaid      -= payment.Amount;
        _sales.Update(sale);
        if (payment.PaymentBatch is { } b)
        {
            b.GrossAmount -= payment.Amount;
            b.NetAmount   -= payment.Amount;
        }
        await _commissions.ReverseForPaymentAsync(sale.Id, payment.Id, user);

        await _audit.LogAsync(user, "Payment.Reverse",
            $"{sale.InvoiceNumber} -{payment.Amount:N0} (received {paidDay:dd MMM yyyy}, by {payment.CreatedBy})"
            + (payment.PaymentBatch is { } bb ? $" in {bb.BatchNumber}" : "") + $" | {why}");
        _log.LogInformation(
            "Payment {Amount} on {InvoiceNumber} reversed — paid now {AmountPaid} of {GrandTotal}, by {User}: {Reason}",
            payment.Amount, sale.InvoiceNumber, sale.AmountPaid, sale.GrandTotal, user, why);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<PaymentBatchDto>> GetReceivedSettlementsAsync(DateTime? from = null, DateTime? to = null, Guid? customerId = null)
    {
        // The page filters by local days; BatchDate is a UTC instant.
        DateTime? fromUtc = from is { } f ? DateTime.SpecifyKind(f.Date, DateTimeKind.Local).ToUniversalTime() : null;
        DateTime? toUtc   = to   is { } t ? DateTime.SpecifyKind(t.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime().AddTicks(-1) : null;
        var batches = await _batches.GetAllAsync(PaymentBatchDirection.Received, customerId: customerId, from: fromUtc, to: toUtc);
        return batches.OrderByDescending(b => b.BatchDate).ThenByDescending(b => b.CreatedAt).Select(b =>
        {
            var dto = MapBatch(b, b.Customer?.Name ?? "", b.Payments.Count(p => !p.IsReversed), b.AppliedNotes.Count);
            dto.CreatedAt = b.CreatedAt;
            dto.Lines = b.Payments.OrderBy(p => p.Sale?.InvoiceNumber).Select(p => new PaymentBatchLineDto {
                PaymentId = p.Id, SaleId = p.SaleId, InvoiceNumber = p.Sale?.InvoiceNumber ?? "",
                Amount = p.Amount, IsReversed = p.IsReversed, ReverseReason = p.ReverseReason
            }).ToList();
            return dto;
        }).ToList();
    }

    // ── Staff payments waiting for Admin (R1, HC 2026-10-16) ─────────────────

    public async Task<ServiceResult> VerifyPaymentAsync(Guid pendingId, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can verify a payment."]);
        var p = await _pending.GetByIdAsync(pendingId);
        if (p == null) return _log.Refuse(_loc["Payment not found."]);
        await _uow.LockAsync(p.SaleId);
        p = await _pending.GetByIdAsync(pendingId);
        if (p == null) return _log.Refuse(_loc["Payment not found."]);
        if (p.Status != PendingPaymentStatus.Pending) return _log.Refuse(_loc["This payment has already been decided."]);
        var sale = await _sales.GetByIdWithItemsAsync(p.SaleId);
        if (sale == null || sale.Status == SaleStatus.Cancelled)
            return _log.Refuse(_loc["The invoice is cancelled: reject this payment instead."]);
        var balance = sale.GrandTotal - sale.AmountPaid - await _noteApplications.GetAppliedTotalForSaleAsync(sale.Id);
        if (p.Amount > balance)
            return _log.Refuse(_loc["Amount ({0}) exceeds balance due ({1}).", p.Amount.ToString("N0"), balance.ToString("N0")]);

        // Recorded as the Staff member's payment, dated when they received it; commission accrues now.
        var record = await RecordPaymentCoreAsync(sale, p.Amount, p.Notes, batchId: null, p.CreatedBy, paidAt: p.ReceivedAt);
        p.Status = PendingPaymentStatus.Verified; p.DecidedBy = user; p.DecidedAt = DateTime.UtcNow; p.PaymentRecordId = record.Id;
        await _audit.LogAsync(user, "Payment.Verify", $"{sale.InvoiceNumber} +{p.Amount:N0} recorded by {p.CreatedBy}");
        _log.LogInformation("Payment {Amount} on {InvoiceNumber} by {Staff} verified by {User}", p.Amount, sale.InvoiceNumber, p.CreatedBy, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectPaymentAsync(Guid pendingId, string? reason, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can reject a payment."]);
        var why = SanitiseText(reason, 300);
        if (why == null) return _log.Refuse(_loc["Give a reason for rejecting this payment."]);
        var p = await _pending.GetByIdAsync(pendingId);
        if (p == null) return _log.Refuse(_loc["Payment not found."]);
        await _uow.LockAsync(p.SaleId);
        p = await _pending.GetByIdAsync(pendingId);
        if (p == null) return _log.Refuse(_loc["Payment not found."]);
        if (p.Status != PendingPaymentStatus.Pending) return _log.Refuse(_loc["This payment has already been decided."]);
        p.Status = PendingPaymentStatus.Rejected; p.DecidedBy = user; p.DecidedAt = DateTime.UtcNow; p.RejectReason = why;
        await _audit.LogAsync(user, "Payment.Reject", $"{p.Sale?.InvoiceNumber} {p.Amount:N0} recorded by {p.CreatedBy}: {why}");
        _log.LogWarning("Payment {Amount} on {InvoiceNumber} by {Staff} rejected by {User}: {Reason}",
            p.Amount, p.Sale?.InvoiceNumber, p.CreatedBy, user, why);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<PendingPaymentDto>> GetPendingPaymentsAsync()
    {
        var list = await _pending.GetPendingAsync();
        var applied = await _noteApplications.GetAppliedTotalsForSalesAsync(list.Select(p => p.SaleId).Distinct());
        return list.Select(p => MapPending(p, p.Sale is { } s ? s.GrandTotal - s.AmountPaid - applied.GetValueOrDefault(s.Id) : 0m)).ToList();
    }

    public async Task<List<PendingPaymentDto>> GetPendingPaymentsForSaleAsync(Guid saleId) =>
        (await _pending.GetForSaleAsync(saleId)).Select(p => MapPending(p, 0m)).ToList();

    private static PendingPaymentDto MapPending(PendingPayment p, decimal owed) => new() {
        Id = p.Id, SaleId = p.SaleId, InvoiceNumber = p.Sale?.InvoiceNumber ?? "", CustomerName = p.Sale?.Customer?.Name ?? "",
        Amount = p.Amount, Notes = p.Notes, ReceivedAt = p.ReceivedAt, CreatedBy = p.CreatedBy, Status = p.Status.ToString(),
        DecidedBy = p.DecidedBy, DecidedAt = p.DecidedAt, RejectReason = p.RejectReason, InvoiceOwed = owed };

    public async Task<CustomerCreditDto> GetCreditStatusAsync(Guid customerId)
    {
        var limit = (await _customers.GetByIdAsync(customerId))?.CreditLimit;
        var grace = (await _settings.GetAsync()).OverdueGraceDays;
        var due   = await _sales.GetDueSalesAsync(customerId);
        var applied = await _noteApplications.GetAppliedTotalsForSalesAsync(due.Select(s => s.Id));
        return Credit(customerId, limit, grace, due, applied);
    }

    public async Task<Dictionary<Guid, CustomerCreditDto>> GetCreditStatusesAsync(Guid? excludeSaleId = null)
    {
        var grace = (await _settings.GetAsync()).OverdueGraceDays;
        var due   = (await _sales.GetDueSalesAsync()).Where(s => s.Id != excludeSaleId).ToList();
        var applied = await _noteApplications.GetAppliedTotalsForSalesAsync(due.Select(s => s.Id));
        var byCustomer = due.ToLookup(s => s.CustomerId);
        return (await _customers.GetAllActiveAsync())
            .ToDictionary(c => c.Id, c => Credit(c.Id, c.CreditLimit, grace, byCustomer[c.Id], applied));
    }

    /// <summary>
    /// plafon ≈ monthly credit buying × (term days + grace days) / 30 × 1.5, never below 1.2 × the
    /// biggest credit invoice (so one normal order doesn't trip it), rounded up to 5 / 10 / 25 million.
    /// The window is the last 90 days, or less while the app has less history (from the first sale),
    /// scaled to 30 days. Only a customer with no usual term (a cash customer) gets 0 = cash only. COD
    /// is short credit, not cash (HC, 2026-10-18: usually paid on delivery, sometimes held 1-2 days), so
    /// it goes through the formula with its 0 days + grace. Only active credit invoices count.
    /// </summary>
    public async Task<Dictionary<Guid, CreditSuggestionDto>> GetCreditSuggestionsAsync()
    {
        const int MaxWindow = 90;
        var grace = (await _settings.GetAsync()).OverdueGraceDays;
        var now = DateTime.UtcNow;
        var sales = (await _sales.GetAllAsync(now.AddDays(-MaxWindow), null))
            .Where(s => s.Status == SaleStatus.Active && s.PaymentType != PaymentType.Cash).ToList();
        var first = sales.Count > 0 ? sales.Min(s => s.SaleDate) : now;
        var window = Math.Clamp((int)Math.Ceiling((now - first).TotalDays), 7, MaxWindow);
        var termDays = (await _terms.GetAllAsync()).ToDictionary(t => t.Id, t => t.DueDays);
        var byCustomer = sales.ToLookup(s => s.CustomerId);

        var result = new Dictionary<Guid, CreditSuggestionDto>();
        foreach (var c in await _customers.GetAllActiveAsync())
        {
            var mine = byCustomer[c.Id].ToList();
            var term = c.PaymentTermId is { } t ? termDays.GetValueOrDefault(t) : 0;
            var dto = new CreditSuggestionDto {
                CustomerId = c.Id, TermDays = term, GraceDays = grace, WindowDays = window,
                MonthlyCredit = Math.Round(mine.Sum(s => s.GrandTotal) * 30m / window, 0),
                BiggestInvoice = mine.Count > 0 ? mine.Max(s => s.GrandTotal) : 0m
            };
            if (c.PaymentTermId is null) { dto.Basis = CreditSuggestionBasis.CashCustomer; dto.Suggested = 0m; }
            else if (mine.Count == 0) dto.Basis = CreditSuggestionBasis.NoHistory;
            else
            {
                dto.Basis = CreditSuggestionBasis.Formula;
                var raw = Math.Max(dto.MonthlyCredit * (term + grace) / 30m * 1.5m, dto.BiggestInvoice * 1.2m);
                dto.Suggested = RoundUpTidy(raw);
            }
            result[c.Id] = dto;
        }
        return result;
    }

    /// <summary>Up to the next 5 million below 50 million, 10 million below 200 million, else 25 million.</summary>
    internal static decimal RoundUpTidy(decimal v)
    {
        var step = v <= 50_000_000m ? 5_000_000m : v <= 200_000_000m ? 10_000_000m : 25_000_000m;
        return Math.Max(step, Math.Ceiling(v / step) * step);
    }

    /// <summary>
    /// Same net figure as the Due page (payments and applied credit notes off). Overdue means
    /// more than <paramref name="grace"/> days past the due date, counted in local calendar days
    /// like the due date itself; an invoice with no due date (open credit) is never overdue.
    /// </summary>
    private static CustomerCreditDto Credit(Guid customerId, decimal? limit, int grace,
        IEnumerable<Sale> due, IReadOnlyDictionary<Guid, decimal> applied)
    {
        var today = DateTime.Now.Date;
        var open  = due.Select(s => (Sale: s, Net: s.GrandTotal - s.AmountPaid - applied.GetValueOrDefault(s.Id)))
                       .Where(x => x.Net > 0).ToList();
        var late  = open.Where(x => x.Sale.DueDate is { } d && d.Date.AddDays(grace) < today)
                        .OrderBy(x => x.Sale.DueDate).ToList();
        return new CustomerCreditDto {
            CustomerId = customerId, CreditLimit = limit, GraceDays = grace,
            Open = open.Sum(x => x.Net),
            OverdueCount  = late.Count,
            OverdueAmount = late.Sum(x => x.Net),
            OldestOverdueInvoice = late.FirstOrDefault().Sale?.InvoiceNumber,
            OldestDaysLate = late.Count > 0 ? (today - late[0].Sale.DueDate!.Value.Date).Days : 0
        };
    }

    private static string Money(decimal v) => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Spreads an invoice-level discount across lines in proportion to line total, writing
    /// each share onto SaleItem.AllocatedInvoiceDiscount.
    ///
    /// Each share is rounded, then the residual is given to the largest line rather than
    /// simply the last — the shares must sum to the discount *exactly* (otherwise the
    /// invoice doesn't reconcile), and the largest line is the one guaranteed able to
    /// absorb the adjustment without being driven negative.
    /// </summary>
    private static void AllocateInvoiceDiscount(List<SaleItem> items, decimal discount, decimal lineNetTotal)
    {
        foreach (var i in items) i.AllocatedInvoiceDiscount = 0m;
        if (discount <= 0m || lineNetTotal <= 0m || items.Count == 0) return;

        decimal allocated = 0m;
        foreach (var item in items)
        {
            var share = Math.Round(discount * item.LineTotal / lineNetTotal, 2, MidpointRounding.AwayFromZero);
            item.AllocatedInvoiceDiscount = share;
            allocated += share;
        }

        var residual = discount - allocated;
        if (residual != 0m)
        {
            var largest = items[0];
            foreach (var item in items) if (item.LineTotal > largest.LineTotal) largest = item;
            largest.AllocatedInvoiceDiscount += residual;
        }
    }

    private static string? Sanitise(string? s, int maxLen) => SanitiseText(s, maxLen);

    private static string? SanitiseText(string? s, int maxLen)
    {
        s = TextClean.StripControl(s);
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    private static SaleDto MapDto(Sale s) => new() {
        Id            = s.Id,
        InvoiceNumber = s.InvoiceNumber,
        SaleDate      = s.SaleDate,
        CustomerName  = s.Customer?.Name  ?? "",
        CustomerPhone = s.Customer?.Phone,
        CustomerAddress = s.Customer?.Address,
        CustomerTaxId = s.Customer?.TaxId,
        CustomerNationalId = s.Customer?.NationalId,
        CustomerTaxName = s.Customer?.TaxName,
        CustomerId    = s.CustomerId,
        PaymentTermId = s.PaymentTermId,
        SalesPersonId = s.SalesPersonId,
        ReplacesSaleId = s.ReplacesSaleId,
        BranchCode    = s.Branch?.Code ?? "",
        PaymentType   = s.PaymentType.ToString(),
        PaymentTermName = s.PaymentTerm?.Name ?? "",
        PaymentTermDays = s.PaymentTerm?.DueDays,
        SalesPersonName = s.SalesPerson?.Name ?? "",
        SalesPersonCode = s.SalesPerson?.Code ?? "",
        DueDate       = s.DueDate,
        SubTotal      = s.SubTotal,
        DiscountTotal = s.DiscountTotal,
        InvoiceDiscountAmount  = s.InvoiceDiscountAmount,
        InvoiceDiscountPercent = s.InvoiceDiscountPercent,
        TaxBase        = s.TaxBase,
        TaxRate        = s.TaxRate,
        TaxAmount      = s.TaxAmount,
        IsTaxInclusive = s.IsTaxInclusive,
        GrandTotal    = s.GrandTotal,
        AmountPaid    = s.AmountPaid,
        Status        = s.Status.ToString(),
        Notes         = s.Notes,
        CreatedBy     = s.CreatedBy,
        CreatedAt     = s.CreatedAt,
        NeedsReview   = s.NeedsReview,
        ReviewReasons = s.ReviewReasons,
        ReviewedBy    = s.ReviewedBy,
        ReviewedAt    = s.ReviewedAt,
        CancelledAt   = s.CancelledAt,
        CancelledBy   = s.CancelledBy,
        CancelReason  = s.CancelReason,
        Items = s.SaleItems.Select(i => new SaleItemDto {
            Id             = i.Id,
            ProductId      = i.ProductId,
            ProductName    = i.Product?.Name ?? "",
            SKU            = i.Product?.SKU  ?? "",
            Unit           = i.Product?.Unit ?? "",
            Qty            = i.Qty,
            UnitPrice      = i.UnitPrice,
            DiscountAmount = i.DiscountAmount,
            DiscountPercent= i.DiscountPercent,
            LineTotal      = i.LineTotal,
            AllocatedInvoiceDiscount = i.AllocatedInvoiceDiscount,
            WarrantyMonths = i.WarrantyMonths,
            WarrantyExpiry = i.WarrantyExpiry,
            Notes          = i.Notes,
            PriceReason    = i.PriceReason
        }).ToList(),
        PaymentHistory = s.PaymentRecords?.Select(p => new PaymentRecordDto {
            Id = p.Id, PaymentDate = p.PaymentDate, Amount = p.Amount,
            Notes = p.Notes, CreatedBy = p.CreatedBy,
            BatchId = p.PaymentBatchId, BatchNumber = p.PaymentBatch?.BatchNumber,
            IsReversed = p.IsReversed, ReversedAt = p.ReversedAt, ReversedBy = p.ReversedBy, ReverseReason = p.ReverseReason
        }).OrderBy(p => p.PaymentDate).ToList() ?? new()
    };
}
