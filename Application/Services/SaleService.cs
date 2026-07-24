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
    private readonly IBranchRepository          _branches;
    private readonly IPaymentRecordRepository   _payments;
    private readonly IAuditLogRepository        _audit;
    private readonly InventoryService           _inventory;
    private readonly IAppSettingsRepository     _settings;
    private readonly IPaymentTermRepository     _terms;
    private readonly IUnitOfWork                _uow;

    public SaleService(ISaleRepository sales, IProductRepository products,
        IBranchRepository branches, IPaymentRecordRepository payments,
        IAuditLogRepository audit, InventoryService inventory,
        IAppSettingsRepository settings, IPaymentTermRepository terms, IUnitOfWork uow)
    { _sales=sales; _products=products; _branches=branches; _payments=payments;
      _audit=audit; _inventory=inventory; _settings=settings; _terms=terms; _uow=uow; }

    /// <summary>
    /// Credit days for the legacy TOP payment types, or null for Cash/Due.
    /// Retained only to interpret sales posted before terms became master data —
    /// new sales carry a PaymentTermId instead. Do not extend this switch; add a
    /// PaymentTerm row instead.
    /// </summary>
    private static int? GetTopDays(PaymentType pt) => pt switch {
        PaymentType.TOP30 => 30,
        PaymentType.TOP45 => 45,
        PaymentType.TOP60 => 60,
        PaymentType.TOP90 => 90,
        _                 => null
    };

    /// <summary>True if this payment type is a credit term (Due or any TOP term).</summary>
    private static bool IsCreditTerm(PaymentType pt) =>
        pt == PaymentType.Due || GetTopDays(pt).HasValue;

    public async Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return ServiceResult<SaleDto>.Fail("Add at least one item.");
        if (dto.CustomerId == Guid.Empty)
            return ServiceResult<SaleDto>.Fail("Customer is required.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult<SaleDto>.Fail("Default branch not found.");

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
                WarrantyExpiry = wMonths > 0 ? DateTime.UtcNow.AddMonths(wMonths!.Value) : null,
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
        // Due date comes from the selected PaymentTerm. The legacy GetTopDays switch
        // is only a fallback for the TOP* enum members, which new sales no longer use.
        Guid?     termId  = null;
        DateTime? dueDate = null;

        if (dto.PaymentType != PaymentType.Cash)
        {
            if (dto.PaymentTermId.HasValue)
            {
                var term = await _terms.GetByIdAsync(dto.PaymentTermId.Value);
                if (term == null)
                    return ServiceResult<SaleDto>.Fail("Selected payment term not found.");
                if (!term.IsActive)
                    return ServiceResult<SaleDto>.Fail($"Payment term '{term.Name}' is no longer active.");

                termId  = term.Id;
                dueDate = DateTime.UtcNow.Date.AddDays(term.DueDays);
            }
            else
            {
                // No term chosen: open credit. Fall back to the legacy enum days so
                // an existing TOP* selection still produces the same due date.
                var topDays = GetTopDays(dto.PaymentType);
                if (topDays.HasValue) dueDate = DateTime.UtcNow.Date.AddDays(topDays.Value);
            }
        }

        // Build audit detail — note any price overrides
        var overrides = saleItems
            .Where(i => !string.IsNullOrEmpty(i.PriceReason))
            .Select(i => $"{i.Product?.Name}:{i.PriceReason}")
            .ToList();

        var sale = new Sale {
            Id            = saleId,
            InvoiceNumber = await _sales.GenerateInvoiceNumberAsync(),
            SaleDate      = DateTime.UtcNow,
            CustomerId    = dto.CustomerId,
            BranchId      = branch.Id,
            PaymentType   = dto.PaymentType,
            PaymentTermId = termId,
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

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        foreach (var item in sale.SaleItems)
            await _inventory.StockInForCancelAsync(item.ProductId, item.Qty, item.CostAtSale, saleId, branch.Id);

        sale.Status = SaleStatus.Cancelled;
        _sales.Update(sale);
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

        var record = new PaymentRecord {
            Id          = Guid.NewGuid(),
            SaleId      = dto.SaleId,
            PaymentDate = DateTime.UtcNow,
            Amount      = dto.Amount,
            Notes       = dto.Notes?.Trim(),
            CreatedBy   = user
        };

        await _payments.AddAsync(record);
        sale.AmountPaid += dto.Amount;
        _sales.Update(sale);

        await _audit.LogAsync(user, "Payment.Record", $"{sale.InvoiceNumber} +{dto.Amount:N0}");
        await _uow.SaveChangesAsync();

        return ServiceResult<PaymentRecordDto>.Ok(new PaymentRecordDto {
            Id = record.Id, PaymentDate = record.PaymentDate,
            Amount = record.Amount, Notes = record.Notes, CreatedBy = record.CreatedBy
        });
    }

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
            PaymentType   = s.PaymentType.ToString(),
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
