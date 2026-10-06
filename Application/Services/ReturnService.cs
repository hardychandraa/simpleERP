using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Sales and purchase returns, each posted as one multi-line document against one source
/// document — the same shape as Sale and Purchase rather than a row per returned line.
///
/// Three rules hold both directions together:
///
/// • **The source document is never edited.** A return doesn't touch the invoice's
///   GrandTotal or AmountPaid. What changes hands is carried by the credit note (customer)
///   or debit note (supplier) that every return generates in the same transaction, and
///   that note is what AR/AP reporting nets off. This is the project's standing
///   "no direct edits to posted transactions, cancel+reverse only" rule.
///
/// • **The credit is derived from the source line's net, never from its unit price.** A
///   line's credit is (LineTotal − AllocatedInvoiceDiscount) prorated by the returned
///   quantity, so an invoice carrying line and document discounts is refunded at what the
///   customer actually paid. The slice that closes a line out is derived by subtraction,
///   so returning a line in two goes still credits exactly its net total.
///
/// • **Goods move at honest cost.** A sales return restocks at SaleItem.CostAtSale — the
///   value the units left at — so the COGS reversal is exact. A purchase return issues at
///   the current moving-average cost, which is genuinely not what the supplier billed;
///   both figures are recorded per line and the difference is shown, not hidden.
/// </summary>
public class ReturnService : IReturnService
{
    private readonly ICustomerReturnRepository _customerReturns;
    private readonly ISupplierReturnRepository _supplierReturns;
    private readonly ICreditNoteRepository     _notes;
    private readonly ISaleRepository           _sales;
    private readonly IPurchaseRepository       _purchases;
    private readonly IBranchRepository         _branches;
    private readonly IAuditLogRepository       _audit;
    private readonly InventoryService          _inventory;
    private readonly CommissionService         _commissions;
    private readonly RebateService             _rebates;
    private readonly IUnitOfWork               _uow;

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<ReturnService> _log;
    public ReturnService(ICustomerReturnRepository customerReturns,
        ISupplierReturnRepository supplierReturns, ICreditNoteRepository notes,
        ISaleRepository sales, IPurchaseRepository purchases, IBranchRepository branches,
        IAuditLogRepository audit, InventoryService inventory,
        CommissionService commissions, RebateService rebates, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<ReturnService> log)
    { _customerReturns=customerReturns; _supplierReturns=supplierReturns; _notes=notes;
      _sales=sales; _purchases=purchases; _branches=branches; _audit=audit;
      _inventory=inventory; _commissions=commissions; _rebates=rebates; _uow=uow;  _loc = loc; _log = log; }

    // ══ Sales returns ══════════════════════════════════════════════════════════

    public async Task<ReturnFormDto?> GetCustomerReturnFormAsync(Guid saleId)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return null;

        var form = new ReturnFormDto {
            SourceId         = sale.Id,
            DocumentNumber   = sale.InvoiceNumber,
            DocumentDate     = sale.SaleDate,
            CounterpartyName = sale.Customer?.Name ?? "",
            TaxRate          = sale.TaxRate,
            IsTaxInclusive   = sale.IsTaxInclusive
        };

        if (sale.Status == SaleStatus.Cancelled)
        {
            form.BlockReason = _loc["This invoice is cancelled — its stock was already reversed, so there is nothing to return against."];
            return form;
        }

        var already = await _customerReturns.GetReturnedQtyBySaleItemAsync(saleId);
        form.Lines = sale.SaleItems.Select(i => new ReturnableLineDto {
            SourceItemId    = i.Id,
            ProductId       = i.ProductId,
            ProductName     = i.Product?.Name ?? "",
            SKU             = i.Product?.SKU  ?? "",
            OriginalQty     = i.Qty,
            AlreadyReturned = already.GetValueOrDefault(i.Id)?.Qty ?? 0m,
            UnitAmount      = i.UnitPrice,
            NetLineTotal    = i.LineTotal - i.AllocatedInvoiceDiscount
        }).ToList();

        return form;
    }

    public async Task<ServiceResult<CustomerReturnDto>> CreateCustomerReturnAsync(
        CreateCustomerReturnDto dto, string user)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return _log.Refuse<CustomerReturnDto>(_loc["Add at least one line to return."]);
        var reason = SanitiseText(dto.Reason, 500);
        if (reason == null)
            return _log.Refuse<CustomerReturnDto>(_loc["A reason for the return is required."]);

        var sale = await _sales.GetByIdWithItemsAsync(dto.SaleId);
        if (sale == null) return _log.Refuse<CustomerReturnDto>(_loc["Invoice not found."]);
        if (sale.Status == SaleStatus.Cancelled)
            return _log.Refuse<CustomerReturnDto>(_loc["This invoice is cancelled — its stock was already reversed, so there is nothing to return against."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<CustomerReturnDto>(_loc["Default branch not found."]);

        var dateError = ValidateReturnDate(dto.ReturnDate, sale.SaleDate, againstInvoice: true, out var returnDate);
        if (dateError != null) return _log.Refuse<CustomerReturnDto>(dateError);

        if (dto.Items.GroupBy(i => i.SourceItemId).Any(g => g.Count() > 1))
            return _log.Refuse<CustomerReturnDto>(_loc["The same invoice line appears twice — combine it into one row."]);

        var already  = await _customerReturns.GetReturnedQtyBySaleItemAsync(dto.SaleId);
        var returnId = Guid.NewGuid();
        var items    = new List<CustomerReturnItem>();

        // Feeds the commission clawback below: for each returned line, exactly the credit
        // and net-line figures already computed here, so the clawback uses the same fraction
        // the customer was actually credited — not a separately re-derived one.
        var returnedLines = new Dictionary<Guid, (decimal CreditAmount, decimal NetLineTotal)>();

        // Validate and price every line before anything is written.
        foreach (var input in dto.Items)
        {
            if (input.Qty <= 0)
                return _log.Refuse<CustomerReturnDto>(_loc["All return quantities must be greater than zero."]);

            var line = sale.SaleItems.FirstOrDefault(i => i.Id == input.SourceItemId);
            if (line == null)
                return _log.Refuse<CustomerReturnDto>(_loc["A line being returned is not on this invoice."]);

            var tally     = already.GetValueOrDefault(line.Id) ?? new ReturnedLineTally(0m, 0m);
            var available = line.Qty - tally.Qty;
            if (input.Qty > available)
                return _log.Refuse<CustomerReturnDto>(tally.Qty > 0
                        ? _loc["'{0}': {1} being returned but only {2} of the {3} sold is still returnable ({4} already returned).", line.Product?.Name ?? "", input.Qty.ToString("N2"), available.ToString("N2"), line.Qty.ToString("N2"), tally.Qty.ToString("N2")]
                        : _loc["'{0}': {1} being returned but only {2} of the {3} sold is still returnable.", line.Product?.Name ?? "", input.Qty.ToString("N2"), available.ToString("N2"), line.Qty.ToString("N2")]);

            var netLine  = line.LineTotal - line.AllocatedInvoiceDiscount;
            var creditAmount = SliceCredit(netLine, line.Qty, input.Qty, available, tally.Amount);

            items.Add(new CustomerReturnItem {
                Id               = Guid.NewGuid(),
                CustomerReturnId = returnId,
                SaleItemId       = line.Id,
                ProductId        = line.ProductId,
                Qty              = input.Qty,
                UnitPrice        = line.UnitPrice,
                CreditAmount     = creditAmount,
                CostAtSale       = line.CostAtSale,
                Notes            = SanitiseText(input.Notes, 500),
                Product          = line.Product
            });
            returnedLines[line.Id] = (creditAmount, netLine);
        }

        var subTotal = items.Sum(i => i.CreditAmount);
        var (taxBase, taxAmount, grandTotal) = MoneyMath.SplitTax(subTotal, sale.TaxRate, sale.IsTaxInclusive);

        // Goods go back in at the cost they left at, so the COGS reversal is exact.
        await _inventory.StockInForCustomerReturnAsync(
            items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtSale)),
            returnId, branch.Id, LedgerDate.FromLocalDay(returnDate));

        var ret = new CustomerReturn {
            Id             = returnId,
            ReturnNumber   = await _customerReturns.GenerateReturnNumberAsync(),
            ReturnDate     = returnDate,
            SaleId         = sale.Id,
            BranchId       = branch.Id,
            SubTotal       = subTotal,
            TaxBase        = taxBase,
            TaxRate        = sale.TaxRate,
            TaxAmount      = taxAmount,
            IsTaxInclusive = sale.IsTaxInclusive,
            GrandTotal     = grandTotal,
            Reason         = reason,
            Notes          = SanitiseText(dto.Notes, 500),
            Status         = ReturnStatus.Active,
            CreatedBy      = user,
            CreatedAt      = DateTime.UtcNow,
            Items          = items
        };
        await _customerReturns.AddAsync(ret);

        // The credit note is what the customer is actually owed, and what AR nets off —
        // the invoice itself is left exactly as posted.
        await _notes.AddAsync(new CreditNote {
            Id                     = Guid.NewGuid(),
            DocumentNumber         = await _notes.GenerateDocumentNumberAsync(CreditDebitType.Credit),
            Type                   = CreditDebitType.Credit,
            Category               = CreditNoteCategory.Return,
            NoteDate               = returnDate,
            CustomerId             = sale.CustomerId,
            TaxBase                = taxBase,
            TaxRate                = sale.TaxRate,
            TaxAmount              = taxAmount,
            Amount                 = grandTotal,
            SourceSaleId           = sale.Id,
            SourceCustomerReturnId = returnId,
            Status                 = CreditNoteStatus.Open,
            Reason                 = $"Sales return {ret.ReturnNumber}: {reason}",
            CreatedBy              = user,
            CreatedAt              = DateTime.UtcNow
        });

        // The salesperson doesn't keep commission on revenue that came back — see
        // CommissionService.ClawBackForReturnAsync for the fractional-reversal design.
        await _commissions.ClawBackForReturnAsync(sale.Id, returnedLines, user);

        await _audit.LogAsync(user, "CustomerReturn.Create",
            $"{ret.ReturnNumber} | {sale.InvoiceNumber} | {items.Count} line(s) | {grandTotal:N0}");

        _log.LogInformation(
            "Sales return {ReturnNumber} posted against {InvoiceNumber} — {LineCount} line(s), " +
            "{GrandTotal} credited, stock restocked, commission clawed back, by {User}",
            ret.ReturnNumber, sale.InvoiceNumber, items.Count, grandTotal, user);

        await _uow.SaveChangesAsync();

        var created = await _customerReturns.GetByIdWithItemsAsync(returnId);
        return ServiceResult<CustomerReturnDto>.Ok(MapCustomerReturn(created!));
    }

    public async Task<ServiceResult> CancelCustomerReturnAsync(Guid id, string user)
    {
        var ret = await _customerReturns.GetByIdWithItemsAsync(id);
        if (ret == null) return _log.Refuse(_loc["Return not found."]);
        if (ret.Status == ReturnStatus.Cancelled)
            return _log.Refuse(_loc["This return is already cancelled."]);

        var note = ret.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled);
        if (note?.Status == CreditNoteStatus.Settled)
            return _log.Refuse(_loc["Credit note {0} has already been settled — the customer has had the money or the credit. Cancelling the return now would leave that settlement unsupported; raise a fresh sale or debit note instead.", note.DocumentNumber]);

        // Same boundary as the settled-note check above: once commission has actually been
        // clawed back for a line on this return, undoing the return would leave that
        // clawback standing with nothing to justify it. See CommissionService.HasClawbackForSaleItemsAsync.
        if (await _commissions.HasClawbackForSaleItemsAsync(ret.SaleId, ret.Items.Select(i => i.SaleItemId)))
            return _log.Refuse(_loc["Commission has already been clawed back against this return — cancelling it now would leave that clawback standing with nothing to justify it. Raise a fresh sale instead if this return was a mistake."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        // Guarded: the returned units may already have been sold again.
        var reversal = await _inventory.StockOutForCustomerReturnCancelAsync(
            ret.Items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtSale)),
            ret.Id, branch.Id);
        if (!reversal.Success) return _log.Refuse(reversal.Error!);

        ret.Status = ReturnStatus.Cancelled;
        _customerReturns.Update(ret);
        if (note != null) note.Status = CreditNoteStatus.Cancelled;   // tracked mutation

        await _audit.LogAsync(user, "CustomerReturn.Cancel", ret.ReturnNumber);
        _log.LogInformation(
            "Sales return {ReturnNumber} cancelled — {LineCount} line(s) taken back out of stock, " +
            "credit note cancelled, by {User}",
            ret.ReturnNumber, ret.Items.Count, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<CustomerReturnDto?> GetCustomerReturnAsync(Guid id)
    {
        var ret = await _customerReturns.GetByIdWithItemsAsync(id);
        return ret == null ? null : MapCustomerReturn(ret);
    }

    public async Task<List<ReturnListDto>> GetCustomerReturnsAsync(
        DateTime? from = null, DateTime? to = null, string? search = null)
        => (await _customerReturns.GetAllAsync(from, to, search)).Select(MapCustomerList).ToList();

    public async Task<List<ReturnListDto>> GetReturnsForSaleAsync(Guid saleId)
        => (await _customerReturns.GetBySaleAsync(saleId)).Select(MapCustomerList).ToList();

    // ══ Purchase returns ═══════════════════════════════════════════════════════

    public async Task<ReturnFormDto?> GetSupplierReturnFormAsync(Guid purchaseId)
    {
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return null;

        var form = new ReturnFormDto {
            SourceId         = purchase.Id,
            DocumentNumber   = purchase.PurchaseNumber,
            DocumentDate     = purchase.PurchaseDate,
            CounterpartyName = purchase.Supplier?.Name ?? "",
            TaxRate          = purchase.TaxRate,
            IsTaxInclusive   = purchase.IsTaxInclusive
        };

        if (purchase.Status == PurchaseStatus.Cancelled)
        {
            form.BlockReason = _loc["This purchase is cancelled — its stock was already reversed, so there is nothing to return."];
            return form;
        }

        var already = await _supplierReturns.GetReturnedQtyByPurchaseItemAsync(purchaseId);
        form.Lines = purchase.PurchaseItems.Select(i => new ReturnableLineDto {
            SourceItemId    = i.Id,
            ProductId       = i.ProductId,
            ProductName     = i.Product?.Name ?? "",
            SKU             = i.Product?.SKU  ?? "",
            OriginalQty     = i.Qty,
            AlreadyReturned = already.GetValueOrDefault(i.Id)?.Qty ?? 0m,
            UnitAmount      = i.UnitCost,
            NetLineTotal    = i.LineTotal - i.AllocatedInvoiceDiscount
        }).ToList();

        return form;
    }

    public async Task<ServiceResult<SupplierReturnDto>> CreateSupplierReturnAsync(
        CreateSupplierReturnDto dto, string user)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return _log.Refuse<SupplierReturnDto>(_loc["Add at least one line to return."]);
        var reason = SanitiseText(dto.Reason, 500);
        if (reason == null)
            return _log.Refuse<SupplierReturnDto>(_loc["A reason for the return is required."]);

        var purchase = await _purchases.GetByIdWithItemsAsync(dto.PurchaseId);
        if (purchase == null) return _log.Refuse<SupplierReturnDto>(_loc["Purchase not found."]);
        if (purchase.Status == PurchaseStatus.Cancelled)
            return _log.Refuse<SupplierReturnDto>(_loc["This purchase is cancelled — its stock was already reversed, so there is nothing to return."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<SupplierReturnDto>(_loc["Default branch not found."]);

        var dateError = ValidateReturnDate(dto.ReturnDate, purchase.PurchaseDate, againstInvoice: false, out var returnDate);
        if (dateError != null) return _log.Refuse<SupplierReturnDto>(dateError);

        if (dto.Items.GroupBy(i => i.SourceItemId).Any(g => g.Count() > 1))
            return _log.Refuse<SupplierReturnDto>(_loc["The same purchase line appears twice — combine it into one row."]);

        var already  = await _supplierReturns.GetReturnedQtyByPurchaseItemAsync(dto.PurchaseId);
        var returnId = Guid.NewGuid();
        var items    = new List<SupplierReturnItem>();

        foreach (var input in dto.Items)
        {
            if (input.Qty <= 0)
                return _log.Refuse<SupplierReturnDto>(_loc["All return quantities must be greater than zero."]);

            var line = purchase.PurchaseItems.FirstOrDefault(i => i.Id == input.SourceItemId);
            if (line == null)
                return _log.Refuse<SupplierReturnDto>(_loc["A line being returned is not on this purchase."]);

            var tally     = already.GetValueOrDefault(line.Id) ?? new ReturnedLineTally(0m, 0m);
            var available = line.Qty - tally.Qty;
            if (input.Qty > available)
                return _log.Refuse<SupplierReturnDto>(tally.Qty > 0
                        ? _loc["'{0}': {1} being returned but only {2} of the {3} received is still returnable ({4} already returned).", line.Product?.Name ?? "", input.Qty.ToString("N2"), available.ToString("N2"), line.Qty.ToString("N2"), tally.Qty.ToString("N2")]
                        : _loc["'{0}': {1} being returned but only {2} of the {3} received is still returnable.", line.Product?.Name ?? "", input.Qty.ToString("N2"), available.ToString("N2"), line.Qty.ToString("N2")]);

            var netLine = line.LineTotal - line.AllocatedInvoiceDiscount;

            items.Add(new SupplierReturnItem {
                Id               = Guid.NewGuid(),
                SupplierReturnId = returnId,
                PurchaseItemId   = line.Id,
                ProductId        = line.ProductId,
                Qty              = input.Qty,
                UnitCost         = line.UnitCost,
                DebitAmount      = SliceCredit(netLine, line.Qty, input.Qty, available, tally.Amount),
                // Resolved here rather than inside the ledger call so the value the goods
                // leave at is stored on the line — cancelling puts them back at exactly it.
                // Safe to read once per line: a stock-out never moves the moving average.
                CostAtReturn     = await _inventory.GetCurrentAvgCostAsync(line.ProductId),
                Notes            = SanitiseText(input.Notes, 500),
                Product          = line.Product
            });
        }

        var subTotal = items.Sum(i => i.DebitAmount);
        var (taxBase, taxAmount, grandTotal) = MoneyMath.SplitTax(subTotal, purchase.TaxRate, purchase.IsTaxInclusive);

        var issue = await _inventory.StockOutForSupplierReturnAsync(
            items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtReturn)),
            returnId, branch.Id, LedgerDate.FromLocalDay(returnDate));
        if (!issue.Success) return _log.Refuse<SupplierReturnDto>(issue.Error!);

        var ret = new SupplierReturn {
            Id             = returnId,
            ReturnNumber   = await _supplierReturns.GenerateReturnNumberAsync(),
            ReturnDate     = returnDate,
            PurchaseId     = purchase.Id,
            BranchId       = branch.Id,
            SubTotal       = subTotal,
            TaxBase        = taxBase,
            TaxRate        = purchase.TaxRate,
            TaxAmount      = taxAmount,
            IsTaxInclusive = purchase.IsTaxInclusive,
            GrandTotal     = grandTotal,
            Reason         = reason,
            Notes          = SanitiseText(dto.Notes, 500),
            Status         = ReturnStatus.Active,
            CreatedBy      = user,
            CreatedAt      = DateTime.UtcNow,
            Items          = items
        };
        await _supplierReturns.AddAsync(ret);

        // The debit note is what the supplier owes back, and what AP nets off — the
        // purchase itself is left exactly as posted.
        await _notes.AddAsync(new CreditNote {
            Id                     = Guid.NewGuid(),
            DocumentNumber         = await _notes.GenerateDocumentNumberAsync(CreditDebitType.Debit),
            Type                   = CreditDebitType.Debit,
            Category               = CreditNoteCategory.Return,
            NoteDate               = returnDate,
            SupplierId             = purchase.SupplierId,
            TaxBase                = taxBase,
            TaxRate                = purchase.TaxRate,
            TaxAmount              = taxAmount,
            Amount                 = grandTotal,
            SourcePurchaseId       = purchase.Id,
            SourceSupplierReturnId = returnId,
            Status                 = CreditNoteStatus.Open,
            Reason                 = $"Purchase return {ret.ReturnNumber}: {reason}",
            CreatedBy              = user,
            CreatedAt              = DateTime.UtcNow
        });

        // Returned goods no longer count toward a Volume rebate threshold — see
        // RebateService.ClawBackVolumeForReturnAsync for exactly what gets re-checked.
        await _rebates.ClawBackVolumeForReturnAsync(purchase, items, user);

        await _audit.LogAsync(user, "SupplierReturn.Create",
            $"{ret.ReturnNumber} | {purchase.PurchaseNumber} | {items.Count} line(s) | {grandTotal:N0}");

        _log.LogInformation(
            "Purchase return {ReturnNumber} posted against {PurchaseNumber} — {LineCount} line(s), " +
            "{GrandTotal} debited, stock issued, rebate volume re-checked, by {User}",
            ret.ReturnNumber, purchase.PurchaseNumber, items.Count, grandTotal, user);

        await _uow.SaveChangesAsync();

        var created = await _supplierReturns.GetByIdWithItemsAsync(returnId);
        return ServiceResult<SupplierReturnDto>.Ok(MapSupplierReturn(created!));
    }

    public async Task<ServiceResult> CancelSupplierReturnAsync(Guid id, string user)
    {
        var ret = await _supplierReturns.GetByIdWithItemsAsync(id);
        if (ret == null) return _log.Refuse(_loc["Return not found."]);
        if (ret.Status == ReturnStatus.Cancelled)
            return _log.Refuse(_loc["This return is already cancelled."]);

        var note = ret.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled);
        if (note?.Status == CreditNoteStatus.Settled)
            return _log.Refuse(_loc["Debit note {0} has already been settled — the supplier has already credited it. Cancelling the return now would leave that settlement unsupported; raise a fresh purchase instead.", note.DocumentNumber]);

        // Same boundary as the settled-note check above: once a Volume rebate accrual has
        // actually been clawed back because of this return, undoing the return would leave
        // that clawback standing with nothing to justify it. See
        // RebateService.HasClawbackForPurchaseItemsAsync.
        if (await _rebates.HasClawbackForPurchaseItemsAsync(ret.PurchaseId, ret.Items.Select(i => i.PurchaseItemId)))
            return _log.Refuse(_loc["Rebate has already been clawed back against this return — cancelling it now would leave that clawback standing with nothing to justify it. Raise a fresh purchase instead if this return was a mistake."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        // Back in at exactly the value they left at, not at the drifted average.
        await _inventory.StockInForSupplierReturnCancelAsync(
            ret.Items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtReturn)),
            ret.Id, branch.Id);

        ret.Status = ReturnStatus.Cancelled;
        _supplierReturns.Update(ret);
        if (note != null) note.Status = CreditNoteStatus.Cancelled;   // tracked mutation

        await _audit.LogAsync(user, "SupplierReturn.Cancel", ret.ReturnNumber);
        _log.LogInformation(
            "Purchase return {ReturnNumber} cancelled — {LineCount} line(s) put back into stock " +
            "at the cost they left at, debit note cancelled, by {User}",
            ret.ReturnNumber, ret.Items.Count, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<SupplierReturnDto?> GetSupplierReturnAsync(Guid id)
    {
        var ret = await _supplierReturns.GetByIdWithItemsAsync(id);
        return ret == null ? null : MapSupplierReturn(ret);
    }

    public async Task<List<ReturnListDto>> GetSupplierReturnsAsync(
        DateTime? from = null, DateTime? to = null, string? search = null)
        => (await _supplierReturns.GetAllAsync(from, to, search)).Select(MapSupplierList).ToList();

    public async Task<List<ReturnListDto>> GetReturnsForPurchaseAsync(Guid purchaseId)
        => (await _supplierReturns.GetByPurchaseAsync(purchaseId)).Select(MapSupplierList).ToList();

    // ══ Helpers ════════════════════════════════════════════════════════════════

    /// <summary>
    /// One line's share of its source line's net value.
    ///
    /// The slice that takes a line to fully returned is derived by subtraction from what
    /// has already been credited on it, so returning a line across several documents still
    /// credits exactly its net total — the same reconciliation-by-subtraction rule the
    /// tax split and the invoice-discount allocation use. Partial slices round normally.
    /// </summary>
    private static decimal SliceCredit(decimal netLine, decimal originalQty, decimal returningQty,
        decimal availableQty, decimal alreadyCredited)
    {
        if (originalQty <= 0m) return 0m;
        return returningQty >= availableQty
            ? netLine - alreadyCredited
            : Math.Round(netLine * returningQty / originalQty, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A return can't predate the document it reverses, and can't be in the future.
    /// Compared local-to-local, matching Sale's stricter guard — see decisions.md,
    /// 2026-07-31. No grace day, and the fallback for no date supplied is local "today"
    /// rather than the server's UTC calendar day. Returns the error message, or null and
    /// the resolved date.
    /// </summary>
    // Instance rather than static now: it needs the localizer. The source document is passed
    // as a flag rather than a spliced noun ("invoice"/"purchase") because a mid-sentence noun
    // cannot be translated independently of the sentence around it — each case gets its own key.
    private string? ValidateReturnDate(DateTime? supplied, DateTime sourceDate,
        bool againstInvoice, out DateTime returnDate)
    {
        var todayLocal = DateTime.Now.Date;
        returnDate = supplied?.Date ?? todayLocal;
        if (returnDate > todayLocal)
            return _loc["Return date cannot be in the future."].Value;
        // SaleDate is a UTC instant (a 2 Oct sale is stored as 1 Oct 17:00), so it is compared
        // as the local day it shows as; PurchaseDate is already stored as the local day. Comparing
        // the UTC date let a return dated the day before its sale through.
        var sourceDay = againstInvoice
            ? DateTime.SpecifyKind(sourceDate, DateTimeKind.Utc).ToLocalTime().Date
            : sourceDate.Date;
        if (returnDate < sourceDay)
            return againstInvoice
                ? _loc["Return date cannot be before the invoice date ({0}).", sourceDay.ToString("dd MMM yyyy")].Value
                : _loc["Return date cannot be before the purchase date ({0}).", sourceDay.ToString("dd MMM yyyy")].Value;
        return null;
    }

    private static string? SanitiseText(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    private static CustomerReturnDto MapCustomerReturn(CustomerReturn r)
    {
        var note = r.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled)
                ?? r.CreditNotes.FirstOrDefault();
        return new CustomerReturnDto {
            Id             = r.Id,
            ReturnNumber   = r.ReturnNumber,
            ReturnDate     = r.ReturnDate,
            SaleId         = r.SaleId,
            InvoiceNumber  = r.Sale?.InvoiceNumber ?? "",
            CustomerName   = r.Sale?.Customer?.Name ?? "",
            SubTotal       = r.SubTotal,
            TaxBase        = r.TaxBase,
            TaxRate        = r.TaxRate,
            TaxAmount      = r.TaxAmount,
            IsTaxInclusive = r.IsTaxInclusive,
            GrandTotal     = r.GrandTotal,
            CostRestocked  = r.Items.Sum(i => i.CostAtSale * i.Qty),
            Reason         = r.Reason,
            Notes          = r.Notes,
            Status         = r.Status.ToString(),
            CreatedBy      = r.CreatedBy,
            CreditNoteId     = note?.Id,
            CreditNoteNumber = note?.DocumentNumber ?? "",
            CreditNoteStatus = note?.Status.ToString() ?? "",
            Items = r.Items.Select(i => new CustomerReturnItemDto {
                Id           = i.Id,
                ProductId    = i.ProductId,
                ProductName  = i.Product?.Name ?? "",
                SKU          = i.Product?.SKU  ?? "",
                Qty          = i.Qty,
                UnitPrice    = i.UnitPrice,
                CreditAmount = i.CreditAmount,
                CostAtSale   = i.CostAtSale,
                Notes        = i.Notes
            }).ToList()
        };
    }

    private static SupplierReturnDto MapSupplierReturn(SupplierReturn r)
    {
        var note = r.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled)
                ?? r.CreditNotes.FirstOrDefault();
        return new SupplierReturnDto {
            Id             = r.Id,
            ReturnNumber   = r.ReturnNumber,
            ReturnDate     = r.ReturnDate,
            PurchaseId     = r.PurchaseId,
            PurchaseNumber = r.Purchase?.PurchaseNumber ?? "",
            SupplierName   = r.Purchase?.Supplier?.Name ?? "",
            SubTotal       = r.SubTotal,
            TaxBase        = r.TaxBase,
            TaxRate        = r.TaxRate,
            TaxAmount      = r.TaxAmount,
            IsTaxInclusive = r.IsTaxInclusive,
            GrandTotal     = r.GrandTotal,
            StockReleased  = r.Items.Sum(i => i.CostAtReturn * i.Qty),
            Reason         = r.Reason,
            Notes          = r.Notes,
            Status         = r.Status.ToString(),
            CreatedBy      = r.CreatedBy,
            DebitNoteId     = note?.Id,
            DebitNoteNumber = note?.DocumentNumber ?? "",
            DebitNoteStatus = note?.Status.ToString() ?? "",
            Items = r.Items.Select(i => new SupplierReturnItemDto {
                Id           = i.Id,
                ProductId    = i.ProductId,
                ProductName  = i.Product?.Name ?? "",
                SKU          = i.Product?.SKU  ?? "",
                Qty          = i.Qty,
                UnitCost     = i.UnitCost,
                DebitAmount  = i.DebitAmount,
                CostAtReturn = i.CostAtReturn,
                Notes        = i.Notes
            }).ToList()
        };
    }

    private static ReturnListDto MapCustomerList(CustomerReturn r) => new() {
        Id               = r.Id,
        ReturnNumber     = r.ReturnNumber,
        ReturnDate       = r.ReturnDate,
        IsCustomerReturn = true,
        SourceDocument   = r.Sale?.InvoiceNumber ?? "",
        SourceId         = r.SaleId,
        CounterpartyName = r.Sale?.Customer?.Name ?? "",
        LineCount        = r.Items.Count,
        GrandTotal       = r.GrandTotal,
        Reason           = r.Reason,
        Status           = r.Status.ToString()
    };

    private static ReturnListDto MapSupplierList(SupplierReturn r) => new() {
        Id               = r.Id,
        ReturnNumber     = r.ReturnNumber,
        ReturnDate       = r.ReturnDate,
        IsCustomerReturn = false,
        SourceDocument   = r.Purchase?.PurchaseNumber ?? "",
        SourceId         = r.PurchaseId,
        CounterpartyName = r.Purchase?.Supplier?.Name ?? "",
        LineCount        = r.Items.Count,
        GrandTotal       = r.GrandTotal,
        Reason           = r.Reason,
        Status           = r.Status.ToString()
    };
}
