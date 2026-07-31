using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
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
    private readonly IUnitOfWork               _uow;

    public ReturnService(ICustomerReturnRepository customerReturns,
        ISupplierReturnRepository supplierReturns, ICreditNoteRepository notes,
        ISaleRepository sales, IPurchaseRepository purchases, IBranchRepository branches,
        IAuditLogRepository audit, InventoryService inventory, IUnitOfWork uow)
    { _customerReturns=customerReturns; _supplierReturns=supplierReturns; _notes=notes;
      _sales=sales; _purchases=purchases; _branches=branches; _audit=audit;
      _inventory=inventory; _uow=uow; }

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
            form.BlockReason = "This invoice is cancelled — its stock was already reversed, " +
                               "so there is nothing to return against.";
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
            return ServiceResult<CustomerReturnDto>.Fail("Add at least one line to return.");
        var reason = SanitiseText(dto.Reason, 500);
        if (reason == null)
            return ServiceResult<CustomerReturnDto>.Fail("A reason for the return is required.");

        var sale = await _sales.GetByIdWithItemsAsync(dto.SaleId);
        if (sale == null) return ServiceResult<CustomerReturnDto>.Fail("Invoice not found.");
        if (sale.Status == SaleStatus.Cancelled)
            return ServiceResult<CustomerReturnDto>.Fail(
                "This invoice is cancelled — its stock was already reversed, so there is " +
                "nothing to return against.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult<CustomerReturnDto>.Fail("Default branch not found.");

        var dateError = ValidateReturnDate(dto.ReturnDate, sale.SaleDate, "invoice", out var returnDate);
        if (dateError != null) return ServiceResult<CustomerReturnDto>.Fail(dateError);

        if (dto.Items.GroupBy(i => i.SourceItemId).Any(g => g.Count() > 1))
            return ServiceResult<CustomerReturnDto>.Fail(
                "The same invoice line appears twice — combine it into one row.");

        var already  = await _customerReturns.GetReturnedQtyBySaleItemAsync(dto.SaleId);
        var returnId = Guid.NewGuid();
        var items    = new List<CustomerReturnItem>();

        // Validate and price every line before anything is written.
        foreach (var input in dto.Items)
        {
            if (input.Qty <= 0)
                return ServiceResult<CustomerReturnDto>.Fail("All return quantities must be greater than zero.");

            var line = sale.SaleItems.FirstOrDefault(i => i.Id == input.SourceItemId);
            if (line == null)
                return ServiceResult<CustomerReturnDto>.Fail("A line being returned is not on this invoice.");

            var tally     = already.GetValueOrDefault(line.Id) ?? new ReturnedLineTally(0m, 0m);
            var available = line.Qty - tally.Qty;
            if (input.Qty > available)
                return ServiceResult<CustomerReturnDto>.Fail(
                    $"'{line.Product?.Name}': {input.Qty:N2} being returned but only " +
                    $"{available:N2} of the {line.Qty:N2} sold is still returnable" +
                    (tally.Qty > 0 ? $" ({tally.Qty:N2} already returned)." : "."));

            var netLine = line.LineTotal - line.AllocatedInvoiceDiscount;

            items.Add(new CustomerReturnItem {
                Id               = Guid.NewGuid(),
                CustomerReturnId = returnId,
                SaleItemId       = line.Id,
                ProductId        = line.ProductId,
                Qty              = input.Qty,
                UnitPrice        = line.UnitPrice,
                CreditAmount     = SliceCredit(netLine, line.Qty, input.Qty, available, tally.Amount),
                CostAtSale       = line.CostAtSale,
                Notes            = SanitiseText(input.Notes, 500),
                Product          = line.Product
            });
        }

        var subTotal = items.Sum(i => i.CreditAmount);
        var (taxBase, taxAmount, grandTotal) = MoneyMath.SplitTax(subTotal, sale.TaxRate, sale.IsTaxInclusive);

        // Goods go back in at the cost they left at, so the COGS reversal is exact.
        await _inventory.StockInForCustomerReturnAsync(
            items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtSale)),
            returnId, branch.Id);

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

        await _audit.LogAsync(user, "CustomerReturn.Create",
            $"{ret.ReturnNumber} | {sale.InvoiceNumber} | {items.Count} line(s) | {grandTotal:N0}");

        await _uow.SaveChangesAsync();

        var created = await _customerReturns.GetByIdWithItemsAsync(returnId);
        return ServiceResult<CustomerReturnDto>.Ok(MapCustomerReturn(created!));
    }

    public async Task<ServiceResult> CancelCustomerReturnAsync(Guid id, string user)
    {
        var ret = await _customerReturns.GetByIdWithItemsAsync(id);
        if (ret == null) return ServiceResult.Fail("Return not found.");
        if (ret.Status == ReturnStatus.Cancelled)
            return ServiceResult.Fail("This return is already cancelled.");

        var note = ret.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled);
        if (note?.Status == CreditNoteStatus.Settled)
            return ServiceResult.Fail(
                $"Credit note {note.DocumentNumber} has already been settled — the customer " +
                "has had the money or the credit. Cancelling the return now would leave that " +
                "settlement unsupported; raise a fresh sale or debit note instead.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        // Guarded: the returned units may already have been sold again.
        var reversal = await _inventory.StockOutForCustomerReturnCancelAsync(
            ret.Items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtSale)),
            ret.Id, branch.Id);
        if (!reversal.Success) return ServiceResult.Fail(reversal.Error!);

        ret.Status = ReturnStatus.Cancelled;
        _customerReturns.Update(ret);
        if (note != null) note.Status = CreditNoteStatus.Cancelled;   // tracked mutation

        await _audit.LogAsync(user, "CustomerReturn.Cancel", ret.ReturnNumber);
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
            form.BlockReason = "This purchase is cancelled — its stock was already reversed, " +
                               "so there is nothing to return.";
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
            return ServiceResult<SupplierReturnDto>.Fail("Add at least one line to return.");
        var reason = SanitiseText(dto.Reason, 500);
        if (reason == null)
            return ServiceResult<SupplierReturnDto>.Fail("A reason for the return is required.");

        var purchase = await _purchases.GetByIdWithItemsAsync(dto.PurchaseId);
        if (purchase == null) return ServiceResult<SupplierReturnDto>.Fail("Purchase not found.");
        if (purchase.Status == PurchaseStatus.Cancelled)
            return ServiceResult<SupplierReturnDto>.Fail(
                "This purchase is cancelled — its stock was already reversed, so there is " +
                "nothing to return.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult<SupplierReturnDto>.Fail("Default branch not found.");

        var dateError = ValidateReturnDate(dto.ReturnDate, purchase.PurchaseDate, "purchase", out var returnDate);
        if (dateError != null) return ServiceResult<SupplierReturnDto>.Fail(dateError);

        if (dto.Items.GroupBy(i => i.SourceItemId).Any(g => g.Count() > 1))
            return ServiceResult<SupplierReturnDto>.Fail(
                "The same purchase line appears twice — combine it into one row.");

        var already  = await _supplierReturns.GetReturnedQtyByPurchaseItemAsync(dto.PurchaseId);
        var returnId = Guid.NewGuid();
        var items    = new List<SupplierReturnItem>();

        foreach (var input in dto.Items)
        {
            if (input.Qty <= 0)
                return ServiceResult<SupplierReturnDto>.Fail("All return quantities must be greater than zero.");

            var line = purchase.PurchaseItems.FirstOrDefault(i => i.Id == input.SourceItemId);
            if (line == null)
                return ServiceResult<SupplierReturnDto>.Fail("A line being returned is not on this purchase.");

            var tally     = already.GetValueOrDefault(line.Id) ?? new ReturnedLineTally(0m, 0m);
            var available = line.Qty - tally.Qty;
            if (input.Qty > available)
                return ServiceResult<SupplierReturnDto>.Fail(
                    $"'{line.Product?.Name}': {input.Qty:N2} being returned but only " +
                    $"{available:N2} of the {line.Qty:N2} received is still returnable" +
                    (tally.Qty > 0 ? $" ({tally.Qty:N2} already returned)." : "."));

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
            returnId, branch.Id);
        if (!issue.Success) return ServiceResult<SupplierReturnDto>.Fail(issue.Error!);

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

        await _audit.LogAsync(user, "SupplierReturn.Create",
            $"{ret.ReturnNumber} | {purchase.PurchaseNumber} | {items.Count} line(s) | {grandTotal:N0}");

        await _uow.SaveChangesAsync();

        var created = await _supplierReturns.GetByIdWithItemsAsync(returnId);
        return ServiceResult<SupplierReturnDto>.Ok(MapSupplierReturn(created!));
    }

    public async Task<ServiceResult> CancelSupplierReturnAsync(Guid id, string user)
    {
        var ret = await _supplierReturns.GetByIdWithItemsAsync(id);
        if (ret == null) return ServiceResult.Fail("Return not found.");
        if (ret.Status == ReturnStatus.Cancelled)
            return ServiceResult.Fail("This return is already cancelled.");

        var note = ret.CreditNotes.FirstOrDefault(n => n.Status != CreditNoteStatus.Cancelled);
        if (note?.Status == CreditNoteStatus.Settled)
            return ServiceResult.Fail(
                $"Debit note {note.DocumentNumber} has already been settled — the supplier " +
                "has already credited it. Cancelling the return now would leave that " +
                "settlement unsupported; raise a fresh purchase instead.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        // Back in at exactly the value they left at, not at the drifted average.
        await _inventory.StockInForSupplierReturnCancelAsync(
            ret.Items.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, i.CostAtReturn)),
            ret.Id, branch.Id);

        ret.Status = ReturnStatus.Cancelled;
        _supplierReturns.Update(ret);
        if (note != null) note.Status = CreditNoteStatus.Cancelled;   // tracked mutation

        await _audit.LogAsync(user, "SupplierReturn.Cancel", ret.ReturnNumber);
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
    /// Returns the error message, or null and the resolved date.
    /// </summary>
    private static string? ValidateReturnDate(DateTime? supplied, DateTime sourceDate,
        string sourceLabel, out DateTime returnDate)
    {
        returnDate = supplied?.Date ?? DateTime.UtcNow.Date;
        if (returnDate > DateTime.UtcNow.Date.AddDays(1))
            return "Return date cannot be in the future.";
        if (returnDate < sourceDate.Date)
            return $"Return date cannot be before the {sourceLabel} date " +
                   $"({sourceDate:dd MMM yyyy}).";
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
