using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Posts supplier invoices as whole multi-line documents — the AP mirror of
/// SaleService. Deliberately not the shape of the old Stock In page (one product at a
/// time, supplier recorded as a free-text note): a supplier bills one document
/// covering many products, and rebates settle against that document, so the document
/// has to be the thing the system stores.
///
/// Stock for every line is received in the same transaction that writes the purchase,
/// through exactly one SaveChangesAsync — a document can never be half-received.
/// </summary>
public class PurchaseService : IPurchaseService
{
    private readonly IPurchaseRepository        _purchases;
    private readonly ISupplierRepository        _suppliers;
    private readonly IProductRepository         _products;
    private readonly IBranchRepository          _branches;
    private readonly ISupplierPaymentRepository _payments;
    private readonly IPaymentTermRepository     _terms;
    private readonly IAppSettingsRepository     _settings;
    private readonly IAuditLogRepository        _audit;
    private readonly ISupplierReturnRepository  _returns;
    private readonly ICreditNoteRepository      _notes;
    private readonly IPaymentBatchRepository    _batches;
    private readonly IRebateAccrualRepository   _rebateAccruals;
    private readonly InventoryService           _inventory;
    private readonly RebateService              _rebates;
    private readonly IUnitOfWork                _uow;

    public PurchaseService(IPurchaseRepository purchases, ISupplierRepository suppliers,
        IProductRepository products, IBranchRepository branches,
        ISupplierPaymentRepository payments, IPaymentTermRepository terms,
        IAppSettingsRepository settings, IAuditLogRepository audit,
        ISupplierReturnRepository returns, ICreditNoteRepository notes,
        IPaymentBatchRepository batches, IRebateAccrualRepository rebateAccruals,
        InventoryService inventory, RebateService rebates, IUnitOfWork uow)
    { _purchases=purchases; _suppliers=suppliers; _products=products; _branches=branches;
      _payments=payments; _terms=terms; _settings=settings; _audit=audit;
      _returns=returns; _notes=notes; _batches=batches; _rebateAccruals=rebateAccruals;
      _inventory=inventory; _rebates=rebates; _uow=uow; }

    public async Task<ServiceResult<PurchaseDto>> CreateAsync(CreatePurchaseDto dto, string user)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return ServiceResult<PurchaseDto>.Fail("Add at least one item.");
        if (dto.SupplierId == Guid.Empty)
            return ServiceResult<PurchaseDto>.Fail("Supplier is required.");

        var supplier = await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null)   return ServiceResult<PurchaseDto>.Fail("Supplier not found.");
        if (!supplier.IsActive) return ServiceResult<PurchaseDto>.Fail($"Supplier '{supplier.Name}' is inactive.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult<PurchaseDto>.Fail("Default branch not found.");

        // The supplier's document number is how a rebate settlement is later matched
        // back to what was bought, so a duplicate almost always means the same invoice
        // is being entered twice — refuse rather than create two costings of one
        // delivery. Scoped to this supplier: numbers collide across suppliers routinely.
        var supplierDoc = SanitiseText(dto.SupplierDocumentNumber, 100);
        if (supplierDoc != null &&
            await _purchases.SupplierDocumentExistsAsync(dto.SupplierId, supplierDoc))
            return ServiceResult<PurchaseDto>.Fail(
                $"'{supplier.Name}' already has an active purchase with document number " +
                $"'{supplierDoc}'. Cancel that one first if this is a correction.");

        var purchaseDate = dto.PurchaseDate?.Date ?? DateTime.UtcNow.Date;
        if (purchaseDate > DateTime.UtcNow.Date.AddDays(1))
            return ServiceResult<PurchaseDto>.Fail("Purchase date cannot be in the future.");

        // Validate every line before anything is written.
        foreach (var item in dto.Items)
        {
            if (item.Qty <= 0)      return ServiceResult<PurchaseDto>.Fail("All quantities must be > 0.");
            if (item.UnitCost < 0)  return ServiceResult<PurchaseDto>.Fail("Cost cannot be negative.");

            // A supplied percent is resolved to an amount server-side, before any
            // validation runs — the client's own computed amount is never trusted.
            if (item.DiscountPercent.HasValue)
            {
                if (item.DiscountPercent < 0 || item.DiscountPercent >= 100)
                    return ServiceResult<PurchaseDto>.Fail("Discount percent must be between 0 and 100.");
                item.DiscountAmount = Math.Round(
                    item.UnitCost * item.DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
            }

            if (item.DiscountAmount < 0)
                return ServiceResult<PurchaseDto>.Fail("Discount cannot be negative.");
            if (item.DiscountAmount >= item.UnitCost && item.UnitCost > 0)
                return ServiceResult<PurchaseDto>.Fail("Discount cannot equal or exceed unit cost.");

            item.Notes = SanitiseText(item.Notes, 500);

            var p = await _products.GetByIdAsync(item.ProductId);
            if (p == null)   return ServiceResult<PurchaseDto>.Fail("Product not found.");
            if (!p.IsActive) return ServiceResult<PurchaseDto>.Fail($"Product '{p.Name}' is inactive.");
        }

        var purchaseId    = Guid.NewGuid();
        var purchaseItems = new List<PurchaseItem>();

        foreach (var itemDto in dto.Items)
        {
            purchaseItems.Add(new PurchaseItem {
                Id              = Guid.NewGuid(),
                PurchaseId      = purchaseId,
                ProductId       = itemDto.ProductId,
                Qty             = itemDto.Qty,
                UnitCost        = itemDto.UnitCost,
                DiscountAmount  = itemDto.DiscountAmount,
                DiscountPercent = itemDto.DiscountPercent,
                LineTotal       = (itemDto.UnitCost - itemDto.DiscountAmount) * itemDto.Qty,
                Notes           = itemDto.Notes,
                Product         = await _products.GetByIdAsync(itemDto.ProductId)
            });
        }

        var lineNetTotal = purchaseItems.Sum(i => i.LineTotal);

        // ── Document-level discount ────────────────────────────────────────────────
        var invoiceDiscount = dto.InvoiceDiscountAmount;
        if (dto.InvoiceDiscountPercent.HasValue)
        {
            if (dto.InvoiceDiscountPercent < 0 || dto.InvoiceDiscountPercent >= 100)
                return ServiceResult<PurchaseDto>.Fail("Document discount percent must be between 0 and 100.");
            invoiceDiscount = Math.Round(
                lineNetTotal * dto.InvoiceDiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
        }
        if (invoiceDiscount < 0)
            return ServiceResult<PurchaseDto>.Fail("Document discount cannot be negative.");
        if (invoiceDiscount >= lineNetTotal && lineNetTotal > 0)
            return ServiceResult<PurchaseDto>.Fail(
                $"Document discount ({invoiceDiscount:N0}) cannot equal or exceed the total ({lineNetTotal:N0}).");

        AllocateInvoiceDiscount(purchaseItems, invoiceDiscount, lineNetTotal);

        var netTotal = lineNetTotal - invoiceDiscount;

        // ── PPN Masukan ────────────────────────────────────────────────────────────
        // Same two branches as the sales side, and the inclusive branch derives the tax
        // by subtraction so base + tax reconciles to the total exactly.
        var taxRate = (await _settings.GetAsync()).VatRate;
        decimal taxBase, taxAmount, grandTotal;

        if (taxRate <= 0m)
        {
            taxBase = netTotal; taxAmount = 0m; grandTotal = netTotal;
        }
        else if (dto.IsTaxInclusive)
        {
            grandTotal = netTotal;
            taxBase    = Math.Round(netTotal / (1m + taxRate), 2, MidpointRounding.AwayFromZero);
            taxAmount  = netTotal - taxBase;
        }
        else
        {
            taxBase    = netTotal;
            taxAmount  = Math.Round(netTotal * taxRate, 2, MidpointRounding.AwayFromZero);
            grandTotal = taxBase + taxAmount;
        }

        // ── Credit term ────────────────────────────────────────────────────────────
        // Due date runs from the supplier's invoice date, not from today — a document
        // entered a week late is already a week into its term.
        Guid?     termId  = null;
        DateTime? dueDate = null;

        if (dto.PaymentType != PaymentType.Cash && dto.PaymentTermId.HasValue)
        {
            var term = await _terms.GetByIdAsync(dto.PaymentTermId.Value);
            if (term == null)
                return ServiceResult<PurchaseDto>.Fail("Selected payment term not found.");
            if (!term.IsActive)
                return ServiceResult<PurchaseDto>.Fail($"Payment term '{term.Name}' is no longer active.");

            termId  = term.Id;
            dueDate = purchaseDate.AddDays(term.DueDays);
        }

        // ── Receive stock ──────────────────────────────────────────────────────────
        // Inventory is costed ex-PPN: input VAT is reclaimable against output VAT, so
        // capitalising it into stock would overstate COGS by the full rate on every
        // item sold. The cost that lands is the line net of both discounts.
        var receipts = purchaseItems.Select(i => new PurchaseReceiptLine(
            i.ProductId, i.Qty, NetUnitCostExTax(i, dto.IsTaxInclusive, taxRate))).ToList();

        await _inventory.StockInForPurchaseAsync(receipts, purchaseId, branch.Id);

        var purchase = new Purchase {
            Id                     = purchaseId,
            PurchaseNumber         = await _purchases.GeneratePurchaseNumberAsync(),
            SupplierDocumentNumber = supplierDoc,
            PurchaseDate           = purchaseDate,
            SupplierId             = dto.SupplierId,
            BranchId               = branch.Id,
            PaymentType            = dto.PaymentType,
            PaymentTermId          = termId,
            DueDate                = dueDate,
            SubTotal               = purchaseItems.Sum(i => i.UnitCost * i.Qty),
            DiscountTotal          = purchaseItems.Sum(i => i.DiscountAmount * i.Qty),
            InvoiceDiscountAmount  = invoiceDiscount,
            InvoiceDiscountPercent = dto.InvoiceDiscountPercent,
            TaxBase                = taxBase,
            TaxRate                = taxRate,
            TaxAmount              = taxAmount,
            IsTaxInclusive         = dto.IsTaxInclusive,
            GrandTotal             = grandTotal,
            AmountPaid             = dto.PaymentType == PaymentType.Cash ? grandTotal : 0m,
            Status                 = PurchaseStatus.Active,
            Notes                  = SanitiseText(dto.Notes, 500),
            CreatedBy              = user,
            CreatedAt              = DateTime.UtcNow,
            PurchaseItems          = purchaseItems
        };

        await _purchases.AddAsync(purchase);

        // Accrue Volume/PriceDrop rebates in the same transaction — the purchase and any
        // rebate it earns commit together or not at all.
        await _rebates.EvaluateOnPurchaseAsync(purchase, purchaseItems);

        await _audit.LogAsync(user, "Purchase.Create",
            $"{purchase.PurchaseNumber} | {supplier.Name}" +
            (supplierDoc != null ? $" | doc {supplierDoc}" : "") +
            $" | {grandTotal:N0}");

        await _uow.SaveChangesAsync();

        var created = await _purchases.GetByIdWithItemsAsync(purchaseId);
        return ServiceResult<PurchaseDto>.Ok(MapDto(created!));
    }

    public async Task<ServiceResult> CancelAsync(Guid purchaseId, string user)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return ServiceResult.Fail("Purchase not found.");
        if (purchase.Status == PurchaseStatus.Cancelled)
            return ServiceResult.Fail("Purchase is already cancelled.");
        if (purchase.AmountPaid > 0)
            return ServiceResult.Fail(
                $"{purchase.AmountPaid:N0} has already been paid against this purchase. " +
                "Cancelling would leave that payment pointing at nothing — reverse the " +
                "payment first, or record a supplier return instead.");

        // A supplier return has already sent some of these units back, so the stock guard
        // below would refuse anyway — but with a message about goods being sold. Say the
        // real reason, and point at the action that actually unblocks it.
        if (await _returns.HasActiveReturnAsync(purchaseId))
            return ServiceResult.Fail(
                "This purchase has a supplier return against it. Cancel the return first — " +
                "its goods have already left stock and cannot be reversed twice.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        // The whole document goes in one call: every line is checked against a running
        // tally before anything is written, so a document whose stock is partly gone fails
        // whole rather than reversing halfway — and two lines of the same product can't
        // both be checked against the same pre-cancel figure.
        var reversal = await _inventory.StockOutForPurchaseCancelAsync(
            purchase.PurchaseItems.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, 0m)),
            purchaseId, branch.Id);
        if (!reversal.Success) return ServiceResult.Fail(reversal.Error!);

        purchase.Status = PurchaseStatus.Cancelled;
        _purchases.Update(purchase);

        // Void (never delete) any rebate this purchase accrued — a settled one is left
        // alone, since that money already changed hands.
        await _rebates.VoidAccrualsForPurchaseAsync(purchaseId, user);

        await _audit.LogAsync(user, "Purchase.Cancel", purchase.PurchaseNumber);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<SupplierPaymentDto>> RecordPaymentAsync(
        RecordSupplierPaymentDto dto, string user)
    {
        if (dto.Amount <= 0)
            return ServiceResult<SupplierPaymentDto>.Fail("Payment amount must be > 0.");

        var purchase = await _purchases.GetByIdWithItemsAsync(dto.PurchaseId);
        if (purchase == null) return ServiceResult<SupplierPaymentDto>.Fail("Purchase not found.");
        if (purchase.Status == PurchaseStatus.Cancelled)
            return ServiceResult<SupplierPaymentDto>.Fail("Cannot pay a cancelled purchase.");
        if (purchase.PaymentType == PaymentType.Cash)
            return ServiceResult<SupplierPaymentDto>.Fail("This is a cash purchase — already paid.");

        var balance = purchase.GrandTotal - purchase.AmountPaid;
        if (dto.Amount > balance)
            return ServiceResult<SupplierPaymentDto>.Fail(
                $"Amount ({dto.Amount:N0}) exceeds balance owed ({balance:N0}).");

        var record = await RecordPaymentCoreAsync(purchase, dto.Amount, dto.Notes, batchId: null, user);

        await _audit.LogAsync(user, "SupplierPayment.Record",
            $"{purchase.PurchaseNumber} -{dto.Amount:N0}");
        await _uow.SaveChangesAsync();

        return ServiceResult<SupplierPaymentDto>.Ok(new SupplierPaymentDto {
            Id = record.Id, PaymentDate = record.PaymentDate,
            Amount = record.Amount, Notes = record.Notes, CreatedBy = record.CreatedBy
        });
    }

    /// <summary>
    /// Applies one payment to one purchase: the payment row, the balance increment, and the
    /// self-executing OnTimePayment rebate check. Does NOT SaveChanges — the caller owns the
    /// transaction, so a single payment and a multi-purchase settlement both commit once.
    /// </summary>
    private async Task<SupplierPayment> RecordPaymentCoreAsync(
        Purchase purchase, decimal amount, string? notes, Guid? batchId, string user)
    {
        var record = new SupplierPayment {
            Id             = Guid.NewGuid(),
            PurchaseId     = purchase.Id,
            PaymentDate    = DateTime.UtcNow,
            Amount         = amount,
            Notes          = SanitiseText(notes, 300),
            CreatedBy      = user,
            PaymentBatchId = batchId
        };

        await _payments.AddAsync(record);
        purchase.AmountPaid += amount;
        _purchases.Update(purchase);

        // Self-executing OnTimePayment rebate: if this payment clears the balance on or
        // before the due date, it accrues and settles in the same transaction. Scoped
        // entirely to this one purchase, so settling several in a loop is safe.
        await _rebates.EvaluateOnPaymentAsync(purchase, record.PaymentDate, user);
        return record;
    }

    public async Task<ServiceResult<PaymentBatchDto>> RecordBatchPaymentAsync(
        RecordSupplierBatchPaymentDto dto, string user)
    {
        var lines   = (dto.Lines ?? new()).Where(l => l.Amount > 0).ToList();
        var noteIds = (dto.ApplyCreditNoteIds ?? new()).Distinct().ToList();

        if (lines.Count == 0 && noteIds.Count == 0)
            return ServiceResult<PaymentBatchDto>.Fail("Enter an amount on at least one purchase.");

        var supplier = await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null) return ServiceResult<PaymentBatchDto>.Fail("Supplier not found.");

        // One purchase can only appear once. Without this, two lines could each pass a
        // balance check read before either was applied, and jointly overpay.
        if (lines.GroupBy(l => l.PurchaseId).Any(g => g.Count() > 1))
            return ServiceResult<PaymentBatchDto>.Fail(
                "The same purchase appears twice — combine it into one row.");

        // ── Validate every purchase line before anything is written ───────────────
        var purchases = await _purchases.GetByIdsWithItemsAsync(lines.Select(l => l.PurchaseId));
        var byId      = purchases.ToDictionary(p => p.Id);

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.PurchaseId, out var purchase))
                return ServiceResult<PaymentBatchDto>.Fail("A purchase on this settlement no longer exists.");
            if (purchase.SupplierId != dto.SupplierId)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Purchase {purchase.PurchaseNumber} belongs to a different supplier.");
            if (purchase.Status == PurchaseStatus.Cancelled)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Purchase {purchase.PurchaseNumber} is cancelled.");
            if (purchase.PaymentType == PaymentType.Cash)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Purchase {purchase.PurchaseNumber} is a cash purchase — already paid.");

            var balance = purchase.GrandTotal - purchase.AmountPaid;
            if (line.Amount > balance)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"Purchase {purchase.PurchaseNumber}: {line.Amount:N0} exceeds its balance of {balance:N0}.");
        }

        // ── Validate every note before anything is written ────────────────────────
        var notes = noteIds.Count == 0 ? new List<CreditNote>() : await _notes.GetByIdsAsync(noteIds);
        if (notes.Count != noteIds.Count)
            return ServiceResult<PaymentBatchDto>.Fail("A debit note on this settlement no longer exists.");

        foreach (var note in notes)
        {
            if (note.Type != CreditDebitType.Debit)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} is a credit note and cannot be applied to money paid out.");
            if (note.SupplierId != dto.SupplierId)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} belongs to a different supplier.");
            if (note.Status != CreditNoteStatus.Open)
                return ServiceResult<PaymentBatchDto>.Fail(
                    $"{note.DocumentNumber} is already {note.Status.ToString().ToLower()}.");
        }

        // ── Post ─────────────────────────────────────────────────────────────────
        var gross        = lines.Sum(l => l.Amount);
        var notesApplied = notes.Sum(n => n.Amount);

        // A note is applied whole or not at all, so netting more credit than is being paid
        // would settle notes whose value this settlement can't absorb — quietly writing off
        // money the supplier still owes back. Refuse, and say what to change.
        if (notesApplied > gross)
            return ServiceResult<PaymentBatchDto>.Fail(
                $"The selected debit notes ({notesApplied:N0}) exceed the {gross:N0} being " +
                "paid. Include more purchases, or untick a note and settle it against a " +
                "later payment.");

        var batchNotes   = SanitiseText(dto.Notes, 500);

        var batch = new PaymentBatch {
            Id                 = Guid.NewGuid(),
            BatchNumber        = await _batches.GenerateBatchNumberAsync(PaymentBatchDirection.Paid),
            Direction          = PaymentBatchDirection.Paid,
            BatchDate          = DateTime.UtcNow,
            SupplierId         = dto.SupplierId,
            GrossAmount        = gross,
            NotesAppliedAmount = notesApplied,
            NetAmount          = gross - notesApplied,
            Notes              = batchNotes,
            CreatedBy          = user,
            CreatedAt          = DateTime.UtcNow
        };
        await _batches.AddAsync(batch);

        foreach (var line in lines)
            await RecordPaymentCoreAsync(byId[line.PurchaseId], line.Amount, batchNotes, batch.Id, user);

        // Tracked mutation — no Update() on entities EF is already following.
        foreach (var note in notes)
        {
            note.Status                  = CreditNoteStatus.Settled;
            note.SettledDate             = batch.BatchDate;
            note.SettledByPaymentBatchId = batch.Id;
            note.SettlementNotes         = $"Applied to settlement {batch.BatchNumber}";
        }

        await _audit.LogAsync(user, "PaymentBatch.Record",
            $"{batch.BatchNumber} | {supplier.Name} | net {batch.NetAmount:N0} " +
            $"({gross:N0} gross − {notesApplied:N0} notes) across {lines.Count} purchase(s)");
        await _uow.SaveChangesAsync();

        return ServiceResult<PaymentBatchDto>.Ok(new PaymentBatchDto {
            Id                 = batch.Id,
            BatchNumber        = batch.BatchNumber,
            Direction          = batch.Direction.ToString(),
            IsReceived         = false,
            BatchDate          = batch.BatchDate,
            CounterpartyName   = supplier.Name,
            GrossAmount        = batch.GrossAmount,
            NotesAppliedAmount = batch.NotesAppliedAmount,
            NetAmount          = batch.NetAmount,
            DocumentCount      = lines.Count,
            NoteCount          = notes.Count,
            Notes              = batch.Notes,
            CreatedBy          = batch.CreatedBy
        });
    }

    public async Task<PaymentStatementDto?> GetSupplierStatementAsync(
        Guid supplierId, DateTime? from = null, DateTime? to = null)
    {
        var supplier = await _suppliers.GetByIdAsync(supplierId);
        if (supplier == null) return null;

        var due = await _purchases.GetDuePurchasesAsync(supplierId);
        // The window narrows what's listed, never what's payable.
        if (from.HasValue) due = due.Where(p => p.PurchaseDate.Date >= from.Value.Date).ToList();
        if (to.HasValue)   due = due.Where(p => p.PurchaseDate.Date <= to.Value.Date).ToList();

        var openNotes = await _notes.GetAllAsync(
            type: CreditDebitType.Debit, status: CreditNoteStatus.Open, supplierId: supplierId);

        // Informational only. Rebate settles on its own cadence against the supplier's own
        // reconciliation sheet — it is deliberately not netted into a PO payment run.
        var outstandingRebate = (await _rebateAccruals.GetOutstandingBySupplierAsync(supplierId))
            .Where(a => a.RewardType != RebateRewardType.InKindGoods
                     && a.RewardType != RebateRewardType.LuckyDraw)
            .Sum(a => a.Amount);

        return new PaymentStatementDto {
            CounterpartyId   = supplier.Id,
            CounterpartyName = supplier.Name,
            Phone            = supplier.Phone,
            Lines = due.Select(p => new StatementLineDto {
                DocumentId     = p.Id,
                DocumentNumber = p.PurchaseNumber,
                TheirReference = p.SupplierDocumentNumber,
                DocumentDate   = p.PurchaseDate,
                DueDate        = p.DueDate,
                GrandTotal     = p.GrandTotal,
                AmountPaid     = p.AmountPaid
            }).ToList(),
            OpenNotes = openNotes.Select(n => new StatementNoteDto {
                CreditNoteId   = n.Id,
                DocumentNumber = n.DocumentNumber,
                NoteDate       = n.NoteDate,
                Category       = n.Category.ToString(),
                Amount         = n.Amount,
                Reason         = n.Reason
            }).ToList(),
            OutstandingRebateAmount = outstandingRebate > 0 ? outstandingRebate : null
        };
    }

    public async Task<PurchaseDto?> GetByIdAsync(Guid id)
    {
        var p = await _purchases.GetByIdWithItemsAsync(id);
        return p == null ? null : MapDto(p);
    }

    public async Task<List<PurchaseListDto>> GetAllAsync(
        DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var list = await _purchases.GetAllAsync(from, to);
        if (!string.IsNullOrWhiteSpace(search))
            list = list.Where(p =>
                p.PurchaseNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (p.SupplierDocumentNumber ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (p.Supplier?.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        return list.Select(MapListDto).ToList();
    }

    public async Task<List<DueSupplierDto>> GetDueSummaryAsync()
    {
        var due = await _purchases.GetDuePurchasesAsync();
        var now = DateTime.UtcNow.Date;
        return due
            .GroupBy(p => p.SupplierId)
            .Select(g => new DueSupplierDto {
                SupplierId    = g.Key,
                SupplierName  = g.First().Supplier?.Name ?? "",
                Phone         = g.First().Supplier?.Phone,
                OpenPurchases = g.Count(),
                TotalDue      = g.Sum(p => p.GrandTotal - p.AmountPaid),
                HasOverdue    = g.Any(p => p.DueDate.HasValue && p.DueDate.Value.Date < now
                                        && p.GrandTotal - p.AmountPaid > 0)
            })
            .OrderByDescending(d => d.HasOverdue)
            .ThenByDescending(d => d.TotalDue)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// What one unit of a line really cost, ex-PPN and net of both discounts — the
    /// figure inventory is valued at.
    ///
    /// Computed per line rather than by apportioning the header TaxBase, so each
    /// product carries its own cost; the sum of the lines can differ from TaxBase by
    /// cents, which is immaterial to a moving average and preferable to smearing one
    /// line's rounding across the others.
    /// </summary>
    private static decimal NetUnitCostExTax(PurchaseItem item, bool taxInclusive, decimal taxRate)
    {
        var net = item.LineTotal - item.AllocatedInvoiceDiscount;
        if (taxInclusive && taxRate > 0m)
            net = Math.Round(net / (1m + taxRate), 2, MidpointRounding.AwayFromZero);
        return item.Qty == 0 ? 0m : Math.Round(net / item.Qty, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Spreads a document-level discount across lines in proportion to line total.
    /// Identical rule to the sales side: shares round per line and the residual goes to
    /// the largest line, so the shares sum to the discount exactly.
    /// </summary>
    private static void AllocateInvoiceDiscount(List<PurchaseItem> items, decimal discount, decimal lineNetTotal)
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

    private static PurchaseListDto MapListDto(Purchase p) => new() {
        Id             = p.Id,
        PurchaseNumber = p.PurchaseNumber,
        SupplierDocumentNumber = p.SupplierDocumentNumber,
        PurchaseDate   = p.PurchaseDate,
        SupplierName   = p.Supplier?.Name ?? "",
        PaymentType    = p.PaymentType.ToString(),
        DueDate        = p.DueDate,
        GrandTotal     = p.GrandTotal,
        AmountPaid     = p.AmountPaid,
        Status         = p.Status.ToString()
    };

    private static PurchaseDto MapDto(Purchase p) => new() {
        Id             = p.Id,
        PurchaseNumber = p.PurchaseNumber,
        SupplierDocumentNumber = p.SupplierDocumentNumber,
        PurchaseDate   = p.PurchaseDate,
        SupplierId     = p.SupplierId,
        SupplierName   = p.Supplier?.Name  ?? "",
        SupplierPhone  = p.Supplier?.Phone,
        PaymentType    = p.PaymentType.ToString(),
        PaymentTermName= p.PaymentTerm?.Name ?? "",
        DueDate        = p.DueDate,
        SubTotal       = p.SubTotal,
        DiscountTotal  = p.DiscountTotal,
        InvoiceDiscountAmount  = p.InvoiceDiscountAmount,
        InvoiceDiscountPercent = p.InvoiceDiscountPercent,
        TaxBase        = p.TaxBase,
        TaxRate        = p.TaxRate,
        TaxAmount      = p.TaxAmount,
        IsTaxInclusive = p.IsTaxInclusive,
        GrandTotal     = p.GrandTotal,
        AmountPaid     = p.AmountPaid,
        Status         = p.Status.ToString(),
        Notes          = p.Notes,
        CreatedBy      = p.CreatedBy,
        Items = p.PurchaseItems.Select(i => new PurchaseItemDto {
            Id              = i.Id,
            ProductId       = i.ProductId,
            ProductName     = i.Product?.Name ?? "",
            SKU             = i.Product?.SKU  ?? "",
            Qty             = i.Qty,
            UnitCost        = i.UnitCost,
            DiscountAmount  = i.DiscountAmount,
            DiscountPercent = i.DiscountPercent,
            LineTotal       = i.LineTotal,
            AllocatedInvoiceDiscount = i.AllocatedInvoiceDiscount,
            Notes           = i.Notes
        }).ToList(),
        PaymentHistory = p.SupplierPayments?.Select(x => new SupplierPaymentDto {
            Id = x.Id, PaymentDate = x.PaymentDate, Amount = x.Amount,
            Notes = x.Notes, CreatedBy = x.CreatedBy
        }).OrderBy(x => x.PaymentDate).ToList() ?? new()
    };
}
