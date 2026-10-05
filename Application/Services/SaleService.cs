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
        IStringLocalizer<SharedResource> loc, ILogger<SaleService> log)
    { _sales=sales; _products=products; _customers=customers; _branches=branches;
      _payments=payments;
      _audit=audit; _inventory=inventory; _settings=settings; _terms=terms;
      _people=people; _returns=returns; _notes=notes; _noteApplications=noteApplications;
      _batches=batches;
      _commissions=commissions; _uow=uow;  _loc = loc; _log = log; }

    public Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user)
        => CreateCoreAsync(dto, user, replacesSaleId: null);

    public async Task<ServiceResult<SaleDto>> ReviseAsync(Guid originalId, CreateSaleDto dto, string user)
    {
        var original = await _sales.GetByIdWithItemsAsync(originalId);
        if (original == null) return _log.Refuse<SaleDto>(_loc["Sale not found."]);
        if (original.Status == SaleStatus.Cancelled)
            return _log.Refuse<SaleDto>(_loc["{0} is already cancelled and cannot be revised.", original.InvoiceNumber]);
        // Money already received belongs to this invoice; moving it is a decision, not a
        // side effect of fixing a typo.
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
            var cancelled = await CancelAsync(originalId, user);
            if (!cancelled.Success) { result = ServiceResult<SaleDto>.Fail(cancelled.Error!); return false; }

            var created = await CreateCoreAsync(dto, user, replacesSaleId: originalId);
            if (!created.Success) { result = created; return false; }

            await _audit.LogAsync(user, "Sale.Revise", $"{originalNumber} -> {created.Data!.InvoiceNumber}");
            _log.LogInformation("Sale {Original} revised as {Replacement}, by {User}",
                originalNumber, created.Data.InvoiceNumber, user);
            result = created;
            return true;
        });
        return result!;
    }

    private async Task<ServiceResult<SaleDto>> CreateCoreAsync(CreateSaleDto dto, string user, Guid? replacesSaleId)
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
            if (item.UnitPrice < 0) return _log.Refuse<SaleDto>(_loc["Price cannot be negative."]);

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

        var saleId   = Guid.NewGuid();
        var saleItems = new List<SaleItem>();

        // Stock out each item (validates stock, inserts ledger — no SaveChanges yet)
        foreach (var itemDto in dto.Items)
        {
            var stockResult = await _inventory.StockOutAsync(itemDto.ProductId, itemDto.Qty, saleId, branch.Id);
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
            SaleItems     = saleItems
        };

        await _sales.AddAsync(sale);

        // A cash sale is collected in full at creation, so commission accrues now.
        // Credit sales accrue later, per payment, in RecordPaymentAsync.
        if (sale.PaymentType == PaymentType.Cash)
            await _commissions.AccrueForCashSaleAsync(sale, saleItems);

        // Audit
        var auditDetail = sale.InvoiceNumber +
            (overrides.Any() ? " | PriceOverrides: " + string.Join("; ", overrides) : "");
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

    public async Task<ServiceResult> CancelAsync(Guid saleId, string user)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return _log.Refuse(_loc["Sale not found."]);
        if (sale.Status == SaleStatus.Cancelled) return _log.Refuse(_loc["Sale is already cancelled."]);

        // Money already received belongs to this invoice. Cancelling used to go through and
        // leave the payment attached to a cancelled invoice, still counted as collected, with
        // nothing recording that it was owed back (HC, 2026-10-05). Same rule as Revise.
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
        _sales.Update(sale);

        // Void (never delete) any unpaid commission this sale accrued.
        await _commissions.VoidForSaleAsync(saleId);

        await _audit.LogAsync(user, "Sale.Cancel", sale.InvoiceNumber);
        // Worth Information rather than Debug: a cancellation reverses stock and voids
        // commission, so it is the event most likely to be behind "the numbers moved and
        // nobody knows why".
        _log.LogInformation(
            "Sale {InvoiceNumber} cancelled — {LineCount} line(s) restocked, {GrandTotal} reversed, by {User}",
            sale.InvoiceNumber, sale.SaleItems.Count, sale.GrandTotal, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user)
    {
        if (dto.Amount <= 0)
            return _log.Refuse<PaymentRecordDto>(_loc["Payment amount must be > 0."]);

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
        if (dto.Amount > currentBalance)
            return _log.Refuse<PaymentRecordDto>(_loc["Amount ({0}) exceeds balance due ({1}).", dto.Amount.ToString("N0"), currentBalance.ToString("N0")]);

        var record = await RecordPaymentCoreAsync(sale, dto.Amount, dto.Notes, batchId: null, user);

        await _audit.LogAsync(user, "Payment.Record", $"{sale.InvoiceNumber} +{dto.Amount:N0}");
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
        Sale sale, decimal amount, string? notes, Guid? batchId, string user)
    {
        var record = new PaymentRecord {
            Id             = Guid.NewGuid(),
            SaleId         = sale.Id,
            PaymentDate    = DateTime.UtcNow,
            Amount         = amount,
            Notes          = notes?.Trim(),
            CreatedBy      = user,
            PaymentBatchId = batchId
        };

        await _payments.AddAsync(record);
        sale.AmountPaid += amount;
        _sales.Update(sale);

        // Commission accrues on collection, prorated to this payment's share of the invoice.
        await _commissions.AccrueForPaymentAsync(sale, record);
        return record;
    }

    public async Task<ServiceResult<PaymentBatchDto>> RecordBatchPaymentAsync(
        RecordSaleBatchPaymentDto dto, string user)
    {
        var lines = (dto.Lines ?? new()).Where(l => l.Amount > 0).ToList();
        var noteIds = (dto.ApplyCreditNoteIds ?? new()).Distinct().ToList();

        if (lines.Count == 0 && noteIds.Count == 0)
            return _log.Refuse<PaymentBatchDto>(_loc["Enter an amount on at least one invoice."]);

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
            BatchDate          = DateTime.UtcNow,
            CustomerId         = dto.CustomerId,
            GrossAmount        = gross,
            NotesAppliedAmount = notesApplied,
            NetAmount          = gross - notesApplied,
            Notes              = batchNotes,
            CreatedBy          = user,
            CreatedAt          = DateTime.UtcNow
        };
        await _batches.AddAsync(batch);

        foreach (var line in lines)
            await RecordPaymentCoreAsync(byId[line.SaleId], line.Amount, batchNotes, batch.Id, user);

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
            $"({gross:N0} gross − {notesApplied:N0} notes) across {lines.Count} invoice(s)");
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
        if (from.HasValue) due = due.Where(s => s.SaleDate.Date >= from.Value.Date).ToList();
        if (to.HasValue)   due = due.Where(s => s.SaleDate.Date <= to.Value.Date).ToList();

        var openNotes = await _notes.GetAllAsync(
            type: CreditDebitType.Credit, status: CreditNoteStatus.Open, customerId: customerId);

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

        return new InvoiceReceiptDto {
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
            Status        = s.Status.ToString()
        }).ToList();
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

    private static string? SanitiseText(string? s, int maxLen)
    {
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
            Notes = p.Notes, CreatedBy = p.CreatedBy
        }).OrderBy(p => p.PaymentDate).ToList() ?? new()
    };
}
