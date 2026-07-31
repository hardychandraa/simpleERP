using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
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

    public SaleService(ISaleRepository sales, IProductRepository products,
        ICustomerRepository customers, IBranchRepository branches,
        IPaymentRecordRepository payments,
        IAuditLogRepository audit, InventoryService inventory,
        IAppSettingsRepository settings, IPaymentTermRepository terms,
        ISalesPersonRepository people, ICustomerReturnRepository returns,
        ICreditNoteRepository notes, IPaymentBatchRepository batches,
        CommissionService commissions, IUnitOfWork uow)
    { _sales=sales; _products=products; _customers=customers; _branches=branches;
      _payments=payments;
      _audit=audit; _inventory=inventory; _settings=settings; _terms=terms;
      _people=people; _returns=returns; _notes=notes; _batches=batches;
      _commissions=commissions; _uow=uow; }

    public async Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return ServiceResult<SaleDto>.Fail("Add at least one item.");
        if (dto.CustomerId == Guid.Empty)
            return ServiceResult<SaleDto>.Fail("Customer is required.");

        // Checked up front, like every other counterparty: without this a bogus id only
        // failed at save time as an FK violation, and a deactivated customer could still
        // be invoiced — the one gap in the app's otherwise consistent inactive guards.
        var customer = await _customers.GetByIdAsync(dto.CustomerId);
        if (customer == null)   return ServiceResult<SaleDto>.Fail("Customer not found.");
        if (!customer.IsActive) return ServiceResult<SaleDto>.Fail($"Customer '{customer.Name}' is inactive.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult<SaleDto>.Fail("Default branch not found.");

        // Attribution is optional, but a supplied one must be real and still active —
        // checked up front, before the ledger is touched.
        Guid? salesPersonId = null;
        if (dto.SalesPersonId.HasValue)
        {
            var person = await _people.GetByIdAsync(dto.SalesPersonId.Value);
            if (person == null)
                return ServiceResult<SaleDto>.Fail("Selected sales person not found.");
            if (!person.IsActive)
                return ServiceResult<SaleDto>.Fail($"Sales person '{person.Name}' is no longer active.");
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
            return ServiceResult<SaleDto>.Fail("Sale date cannot be in the future.");

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
            if (item.Qty <= 0) return ServiceResult<SaleDto>.Fail("All quantities must be > 0.");
            if (item.UnitPrice < 0) return ServiceResult<SaleDto>.Fail("Price cannot be negative.");

            // A percentage discount is resolved to a per-unit amount here, before any
            // validation runs, so both entry modes go through exactly the same guards
            // and everything downstream only ever sees an amount.
            if (item.DiscountPercent.HasValue)
            {
                if (item.DiscountPercent < 0 || item.DiscountPercent >= 100)
                    return ServiceResult<SaleDto>.Fail("Discount percent must be between 0 and 100.");
                item.DiscountAmount = Math.Round(
                    item.UnitPrice * item.DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
            }

            if (item.DiscountAmount < 0) return ServiceResult<SaleDto>.Fail("Discount cannot be negative.");
            if (item.DiscountAmount >= item.UnitPrice && item.UnitPrice > 0)
                return ServiceResult<SaleDto>.Fail("Discount cannot equal or exceed unit price.");

            // Sanitize text fields
            item.Notes       = SanitiseText(item.Notes, 500);
            item.PriceReason = SanitiseText(item.PriceReason, 300);

            var p = await _products.GetByIdAsync(item.ProductId);
            if (p == null)   return ServiceResult<SaleDto>.Fail("Product not found.");
            if (!p.IsActive) return ServiceResult<SaleDto>.Fail($"Product '{p.Name}' is inactive.");

            // Price override reason is required when price deviates from master price
            if (item.UnitPrice != p.UnitPrice && string.IsNullOrWhiteSpace(item.PriceReason))
                return ServiceResult<SaleDto>.Fail(
                    $"Price for '{p.Name}' differs from master price ({p.UnitPrice:N0}). " +
                    "Please provide a reason for the price override.");
        }

        var saleId   = Guid.NewGuid();
        var saleItems = new List<SaleItem>();

        // Stock out each item (validates stock, inserts ledger — no SaveChanges yet)
        foreach (var itemDto in dto.Items)
        {
            var stockResult = await _inventory.StockOutAsync(itemDto.ProductId, itemDto.Qty, saleId, branch.Id);
            if (!stockResult.Success) return ServiceResult<SaleDto>.Fail(stockResult.Error!);

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
                return ServiceResult<SaleDto>.Fail("Invoice discount percent must be between 0 and 100.");
            invoiceDiscount = Math.Round(
                lineNetTotal * dto.InvoiceDiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
        }
        if (invoiceDiscount < 0)
            return ServiceResult<SaleDto>.Fail("Invoice discount cannot be negative.");
        if (invoiceDiscount >= lineNetTotal && lineNetTotal > 0)
            return ServiceResult<SaleDto>.Fail(
                $"Invoice discount ({invoiceDiscount:N0}) cannot equal or exceed the total ({lineNetTotal:N0}).");

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
                return ServiceResult<SaleDto>.Fail("Selected payment term not found.");
            if (!term.IsActive)
                return ServiceResult<SaleDto>.Fail($"Payment term '{term.Name}' is no longer active.");

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

        await _uow.SaveChangesAsync();

        var created = await _sales.GetByIdWithItemsAsync(saleId);
        return ServiceResult<SaleDto>.Ok(MapDto(created!));
    }

    public async Task<ServiceResult> CancelAsync(Guid saleId, string user)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return ServiceResult.Fail("Sale not found.");
        if (sale.Status == SaleStatus.Cancelled) return ServiceResult.Fail("Sale is already cancelled.");

        // Cancelling restocks every unit sold. If a return has already put some of them
        // back, doing that would receive the same goods twice and leave a credit note
        // hanging against an invoice that no longer exists.
        if (await _returns.HasActiveReturnAsync(saleId))
            return ServiceResult.Fail(
                "This invoice has a sales return against it. Cancel the return first — " +
                "cancelling the invoice now would put the returned goods back into stock twice.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        foreach (var item in sale.SaleItems)
            await _inventory.StockInForCancelAsync(item.ProductId, item.Qty, item.CostAtSale, saleId, branch.Id);

        sale.Status = SaleStatus.Cancelled;
        _sales.Update(sale);

        // Void (never delete) any unpaid commission this sale accrued.
        await _commissions.VoidForSaleAsync(saleId);

        await _audit.LogAsync(user, "Sale.Cancel", sale.InvoiceNumber);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user)
    {
        if (dto.Amount <= 0)
            return ServiceResult<PaymentRecordDto>.Fail("Payment amount must be > 0.");

        var sale = await _sales.GetByIdWithItemsAsync(dto.SaleId);
        if (sale == null) return ServiceResult<PaymentRecordDto>.Fail("Sale not found.");
        if (sale.Status == SaleStatus.Cancelled)
            return ServiceResult<PaymentRecordDto>.Fail("Cannot record payment on a cancelled sale.");

        // Allow payment on Due AND all TOP terms
        if (sale.PaymentType == PaymentType.Cash)
            return ServiceResult<PaymentRecordDto>.Fail("This is a Cash sale — payment already collected.");

        var currentBalance = sale.GrandTotal - sale.AmountPaid;
        if (dto.Amount > currentBalance)
            return ServiceResult<PaymentRecordDto>.Fail(
                $"Amount ({dto.Amount:N0}) exceeds balance due ({currentBalance:N0}).");

        var record = await RecordPaymentCoreAsync(sale, dto.Amount, dto.Notes, batchId: null, user);

        await _audit.LogAsync(user, "Payment.Record", $"{sale.InvoiceNumber} +{dto.Amount:N0}");
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
            return ServiceResult<PaymentBatchDto>.Fail("Enter an amount on at least one invoice.");

        var customer = await _customers.GetByIdAsync(dto.CustomerId);
        if (customer == null) return ServiceResult<PaymentBatchDto>.Fail("Customer not found.");

        // One invoice can only appear once. Without this, two lines could each pass a
        // balance check read before either was applied, and jointly overpay.
        if (lines.GroupBy(l => l.SaleId).Any(g => g.Count() > 1))
            return ServiceResult<PaymentBatchDto>.Fail(
                "The same invoice appears twice — combine it into one row.");

        // ── Validate every invoice line before anything is written ────────────────
        var sales = await _sales.GetByIdsWithItemsAsync(lines.Select(l => l.SaleId));
        var byId  = sales.ToDictionary(s => s.Id);

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.SaleId, out var sale))
                return ServiceResult<PaymentBatchDto>.Fail("An invoice on this settlement no longer exists.");
            if (sale.CustomerId != dto.CustomerId)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Invoice {sale.InvoiceNumber} belongs to a different customer.");
            if (sale.Status == SaleStatus.Cancelled)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Invoice {sale.InvoiceNumber} is cancelled.");
            if (sale.PaymentType == PaymentType.Cash)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Invoice {sale.InvoiceNumber} is a cash sale — already collected.");

            var balance = sale.GrandTotal - sale.AmountPaid;
            if (line.Amount > balance)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Invoice {sale.InvoiceNumber}: {line.Amount:N0} exceeds its balance of {balance:N0}.");
        }

        // ── Validate every note before anything is written ────────────────────────
        var notes = noteIds.Count == 0 ? new List<CreditNote>() : await _notes.GetByIdsAsync(noteIds);
        if (notes.Count != noteIds.Count)
            return ServiceResult<PaymentBatchDto>.Fail("A credit note on this settlement no longer exists.");

        foreach (var note in notes)
        {
            if (note.Type != CreditDebitType.Credit)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} is a debit note and cannot be applied to money received.");
            if (note.CustomerId != dto.CustomerId)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} belongs to a different customer.");
            if (note.Status != CreditNoteStatus.Open)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} is already {note.Status.ToString().ToLower()}.");
        }

        // ── Post ─────────────────────────────────────────────────────────────────
        var gross        = lines.Sum(l => l.Amount);
        var notesApplied = notes.Sum(n => n.Amount);

        // A note is applied whole or not at all, so netting more credit than is being
        // collected would settle notes whose value this settlement can't absorb — quietly
        // destroying the customer's remaining credit. Refuse, and say what to change.
        if (notesApplied > gross)
            return ServiceResult<PaymentBatchDto>.Fail(
                $"The selected credit notes ({notesApplied:N0}) exceed the {gross:N0} being " +
                "collected. Include more invoices, or untick a note and settle it against a " +
                "later payment.");

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

        return new PaymentStatementDto {
            CounterpartyId   = customer.Id,
            CounterpartyName = customer.Name,
            Phone            = customer.Phone,
            Lines = due.Select(s => new StatementLineDto {
                DocumentId     = s.Id,
                DocumentNumber = s.InvoiceNumber,
                DocumentDate   = s.SaleDate,
                DueDate        = s.DueDate,
                GrandTotal     = s.GrandTotal,
                AmountPaid     = s.AmountPaid
            }).ToList(),
            OpenNotes = openNotes.Select(n => new StatementNoteDto {
                CreditNoteId   = n.Id,
                DocumentNumber = n.DocumentNumber,
                NoteDate       = n.NoteDate,
                Category       = n.Category.ToString(),
                Amount         = n.Amount,
                Reason         = n.Reason
            }).ToList()
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
        return s == null ? null : MapDto(s);
    }

    public async Task<List<SaleListDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var list = await _sales.GetAllAsync(from, to);
        if (!string.IsNullOrWhiteSpace(search))
            list = list.Where(s =>
                s.InvoiceNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Customer?.Name  ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (s.Customer?.Phone ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        return list.Select(s => new SaleListDto {
            Id            = s.Id,
            InvoiceNumber = s.InvoiceNumber,
            SaleDate      = s.SaleDate,
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
        var now      = DateTime.UtcNow.Date;
        return dueSales
            .GroupBy(s => s.CustomerId)
            .Select(g => new DueCustomerDto {
                CustomerId   = g.Key,
                CustomerName = g.First().Customer?.Name ?? "",
                Phone        = g.First().Customer?.Phone,
                OpenInvoices = g.Count(),
                TotalDue     = g.Sum(s => s.GrandTotal - s.AmountPaid),
                HasOverdue   = g.Any(s => s.DueDate.HasValue && s.DueDate.Value.Date < now
                                       && s.GrandTotal - s.AmountPaid > 0)
            })
            .OrderByDescending(d => d.HasOverdue)
            .ThenByDescending(d => d.TotalDue)
            .ToList();
    }

    public async Task<string> GenerateTxtInvoiceAsync(Guid saleId)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return "Sale not found.";
        const int W = 60;
        var L = new List<string>();
        string Pad(string s, int w) => s.Length > w ? s[..w] : s.PadRight(w);
        L.Add("INVOICE".PadLeft((W + 7) / 2));
        L.Add($"No   : {sale.InvoiceNumber}".PadLeft(W));
        L.Add($"Date : {sale.SaleDate.ToLocalTime():dd-MMM-yyyy HH:mm}".PadLeft(W));
        if (sale.DueDate.HasValue)
            L.Add($"Due  : {sale.DueDate.Value:dd-MMM-yyyy} ({sale.PaymentType})".PadLeft(W));
        L.Add(new string('=', W));
        L.Add($"Customer : {sale.Customer?.Name}");
        if (!string.IsNullOrEmpty(sale.Customer?.Phone)) L.Add($"Phone    : {sale.Customer.Phone}");
        L.Add($"Payment  : {sale.PaymentType}");
        if (sale.Status == SaleStatus.Cancelled) L.Add("*** CANCELLED ***");
        L.Add(new string('-', W));
        L.Add($"{"QTY",4}  {"DESCRIPTION",-28}  {"PRICE",8}  {"TOTAL",8}");
        L.Add(new string('-', W));
        foreach (var i in sale.SaleItems)
        {
            L.Add($"{i.Qty,4:N0}  {Pad(i.Product?.Name ?? "", 28)}  {i.UnitPrice,8:N0}  {i.LineTotal,8:N0}");
            if (i.DiscountAmount > 0) L.Add($"      Disc: -{i.DiscountAmount:N0}");
            if (!string.IsNullOrEmpty(i.Notes)) L.Add($"      Note: {i.Notes}");
            if (!string.IsNullOrEmpty(i.PriceReason)) L.Add($"      Price adj: {i.PriceReason}");
            if (i.WarrantyExpiry.HasValue) L.Add($"      Warranty: {i.WarrantyExpiry.Value.ToLocalTime():dd-MMM-yyyy}");
        }
        L.Add(new string('-', W));
        L.Add($"{"SUBTOTAL :",45} {sale.SubTotal,8:N0}");
        if (sale.DiscountTotal > 0) L.Add($"{"DISCOUNT :",45} {sale.DiscountTotal,8:N0}");
        L.Add($"{"GRAND TOTAL :",45} {sale.GrandTotal,8:N0}");
        L.Add($"{"PAID :",45} {sale.AmountPaid,8:N0}");
        if (sale.GrandTotal - sale.AmountPaid > 0)
            L.Add($"{"BALANCE DUE :",45} {sale.GrandTotal - sale.AmountPaid,8:N0}");
        L.Add(new string('=', W));
        L.Add("Thank you for your purchase!".PadLeft((W + 28) / 2));
        return string.Join(Environment.NewLine, L);
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
        PaymentType   = s.PaymentType.ToString(),
        PaymentTermName = s.PaymentTerm?.Name ?? "",
        SalesPersonName = s.SalesPerson?.Name ?? "",
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
            ProductName    = i.Product?.Name ?? "",
            SKU            = i.Product?.SKU  ?? "",
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
