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
    private readonly ICreditNoteApplicationRepository _noteApplications;
    private readonly PurchaseRecoster           _recoster;
    private readonly ICostSnapshotRepository    _docsForPurchase;

    private readonly PeriodLock _period;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<PurchaseService> _log;
    public PurchaseService(IPurchaseRepository purchases, ISupplierRepository suppliers,
        IProductRepository products, IBranchRepository branches,
        ISupplierPaymentRepository payments, IPaymentTermRepository terms,
        IAppSettingsRepository settings, IAuditLogRepository audit,
        ISupplierReturnRepository returns, ICreditNoteRepository notes,
        ICreditNoteApplicationRepository noteApplications,
        IPaymentBatchRepository batches, IRebateAccrualRepository rebateAccruals,
        InventoryService inventory, RebateService rebates, IUnitOfWork uow,
        PurchaseRecoster recoster, ICostSnapshotRepository docsForPurchase,
        PeriodLock period, IStringLocalizer<SharedResource> loc, ILogger<PurchaseService> log)
    { _purchases=purchases; _suppliers=suppliers; _products=products; _branches=branches;
      _payments=payments; _terms=terms; _settings=settings; _audit=audit;
      _returns=returns; _notes=notes; _noteApplications=noteApplications;
      _batches=batches; _rebateAccruals=rebateAccruals;
      _inventory=inventory; _rebates=rebates; _uow=uow; _recoster=recoster; _docsForPurchase=docsForPurchase;
      _period = period; _loc = loc; _log = log; }

    public Task<ServiceResult<PurchaseDto>> CreateAsync(CreatePurchaseDto dto, string user)
        => CreateCoreAsync(dto, user, enteredWithoutPrice: false);

    /// <summary>
    /// Barang Masuk (HC, 2026-10-08): Staff record what arrived without seeing any price. Every
    /// line is costed here, server-side, at the product's master harga beli (which includes PPN,
    /// HC 2026-10-09); the payment term is the supplier's default. Nothing price-like is taken
    /// from the caller. The purchase is flagged Perlu dicek for Admin to compare with the
    /// supplier's invoice. Returns only the new id: the Staff page must not receive amounts.
    /// </summary>
    public async Task<ServiceResult<Guid>> CreateUnpricedAsync(CreateUnpricedPurchaseDto dto, string user)
    {
        var supplier = dto.SupplierId == Guid.Empty ? null : await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null) return _log.Refuse<Guid>(_loc["Supplier is required."]);

        var priced = await PricedFromMasterAsync(dto, existing: null);
        if (priced.Error != null) return _log.Refuse<Guid>(priced.Error);

        var full = new CreatePurchaseDto {
            SupplierId = supplier.Id, SupplierDocumentNumber = dto.SupplierDocumentNumber,
            PurchaseDate = dto.PurchaseDate, PaymentType = PaymentType.Due,
            PaymentTermId = supplier.PaymentTermId is { } t && (await _terms.GetByIdAsync(t))?.IsActive == true ? t : null,
            IsTaxInclusive = true, Notes = dto.Notes, Items = priced.Items! };
        var result = await CreateCoreAsync(full, user, enteredWithoutPrice: true);
        return result.Success ? ServiceResult<Guid>.Ok(result.Data!.Id) : ServiceResult<Guid>.Fail(result.Error!);
    }

    /// <summary>
    /// Staff correct a Barang Masuk entry: quantities, lines, date, supplier document number,
    /// notes. Prices are never taken from Staff: a line that was already there keeps its current
    /// price and discount (so a price Admin corrected is not reset), a new line is costed at the
    /// master harga beli. Any Staff edit flags the purchase Perlu dicek again, even after Admin
    /// checked it (HC, 2026-10-09).
    /// </summary>
    public async Task<ServiceResult> EditUnpricedAsync(Guid purchaseId, CreateUnpricedPurchaseDto dto, string user)
    {
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(purchaseId);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return _log.Refuse(_loc["Purchase not found."]);
        if (!purchase.EnteredWithoutPrice)
            return _log.Refuse(_loc["Only purchases entered through Barang Masuk can be edited here. Ask an administrator."]);

        var priced = await PricedFromMasterAsync(dto, purchase);
        if (priced.Error != null) return _log.Refuse(priced.Error);

        var full = new CreatePurchaseDto {
            SupplierId = purchase.SupplierId, SupplierDocumentNumber = dto.SupplierDocumentNumber,
            PurchaseDate = dto.PurchaseDate, PaymentType = purchase.PaymentType,
            PaymentTermId = purchase.PaymentTermId, IsTaxInclusive = purchase.IsTaxInclusive,
            InvoiceDiscountPercent = purchase.InvoiceDiscountPercent,
            InvoiceDiscountAmount  = purchase.InvoiceDiscountPercent.HasValue ? 0m : purchase.InvoiceDiscountAmount,
            Notes = dto.Notes, Items = priced.Items! };
        return await ReviseCoreAsync(purchase, full, markReviewed: false, user, byStaff: true);
    }

    /// <summary>
    /// Admin corrects a posted purchase in place (same number): prices, quantities, lines,
    /// discounts, PPN mode, term, date, supplier document number. Everything posted after it for
    /// the same products is re-costed (PurchaseRecoster), rebates are re-evaluated, and the
    /// change is audited before → after. Optionally marks the purchase checked.
    /// </summary>
    public async Task<ServiceResult> ReviseAsync(Guid purchaseId, CreatePurchaseDto dto, bool markReviewed, string user)
    {
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(purchaseId);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return _log.Refuse(_loc["Purchase not found."]);
        return await ReviseCoreAsync(purchase, dto, markReviewed, user, byStaff: false);
    }

    public async Task<ServiceResult> MarkReviewedAsync(Guid purchaseId, string user)
    {
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(purchaseId);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return _log.Refuse(_loc["Purchase not found."]);
        if (purchase.Status == PurchaseStatus.Cancelled) return _log.Refuse(_loc["Purchase is already cancelled."]);
        if (!purchase.NeedsReview) return _log.Refuse(_loc["{0} is already checked.", purchase.PurchaseNumber]);
        purchase.NeedsReview = false; purchase.ReviewedBy = user; purchase.ReviewedAt = DateTime.UtcNow;
        _purchases.Update(purchase);
        await _audit.LogAsync(user, "Purchase.Review", purchase.PurchaseNumber);
        _log.LogInformation("Purchase {PurchaseNumber} checked by {User}", purchase.PurchaseNumber, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    /// <summary>
    /// Lines for a Barang Masuk create or Staff edit, costed server-side. On an edit, a line
    /// that names one of the purchase's own lines (same id and product) keeps that line's price
    /// and discount; anything else gets the master harga beli and no discount.
    /// </summary>
    private async Task<(List<CreatePurchaseItemDto>? Items, LocalizedString? Error)> PricedFromMasterAsync(
        CreateUnpricedPurchaseDto dto, Purchase? existing)
    {
        if (dto.Items == null || dto.Items.Count == 0) return (null, _loc["Add at least one item."]);
        var items = new List<CreatePurchaseItemDto>();
        foreach (var line in dto.Items)
        {
            var own = existing?.PurchaseItems.FirstOrDefault(i => i.Id == line.PurchaseItemId && i.ProductId == line.ProductId);
            if (own != null)
            {
                items.Add(new CreatePurchaseItemDto {
                    PurchaseItemId = own.Id, ProductId = own.ProductId, Qty = line.Qty,
                    UnitCost = own.UnitCost, DiscountAmount = own.DiscountAmount,
                    DiscountPercent = own.DiscountPercent, Notes = line.Notes });
                continue;
            }
            var product = await _products.GetByIdAsync(line.ProductId);
            if (product == null) return (null, _loc["Product not found."]);
            items.Add(new CreatePurchaseItemDto {
                ProductId = product.Id, Qty = line.Qty, UnitCost = product.PurchasePrice, Notes = line.Notes });
        }
        return (items, null);
    }

    private async Task<ServiceResult<PurchaseDto>> CreateCoreAsync(CreatePurchaseDto dto, string user, bool enteredWithoutPrice)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return _log.Refuse<PurchaseDto>(_loc["Add at least one item."]);
        if (dto.SupplierId == Guid.Empty)
            return _log.Refuse<PurchaseDto>(_loc["Supplier is required."]);

        var supplier = await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null)   return _log.Refuse<PurchaseDto>(_loc["Supplier not found."]);
        if (!supplier.IsActive) return _log.Refuse<PurchaseDto>(_loc["Supplier '{0}' is inactive.", supplier.Name]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<PurchaseDto>(_loc["Default branch not found."]);

        // The supplier's document number is how a rebate settlement is later matched
        // back to what was bought, so a duplicate almost always means the same invoice
        // is being entered twice — refuse rather than create two costings of one
        // delivery. Scoped to this supplier: numbers collide across suppliers routinely.
        var supplierDoc = SanitiseText(dto.SupplierDocumentNumber, 100);
        if (supplierDoc != null &&
            await _purchases.SupplierDocumentExistsAsync(dto.SupplierId, supplierDoc))
            return _log.Refuse<PurchaseDto>(_loc["'{0}' already has an active purchase with document number '{1}'. Cancel that one first if this is a correction.", supplier.Name, supplierDoc]);

        // Compared local-to-local, matching Sale's stricter guard (see SaleService.CreateAsync
        // and decisions.md, 2026-07-31) — no grace day, and the no-date-supplied fallback is
        // local "today" rather than the server's UTC calendar day, which trails local by up to
        // 7 hours on this box and would otherwise backdate an unset date during the early
        // morning.
        var todayLocal   = DateTime.Now.Date;
        var purchaseDate = dto.PurchaseDate?.Date ?? todayLocal;
        if (purchaseDate > todayLocal)
            return _log.Refuse<PurchaseDto>(_loc["Purchase date cannot be in the future."]);
        if (await _period.NewDateAsync(purchaseDate) is { } closed) return _log.Refuse<PurchaseDto>(closed);

        var purchaseId = Guid.NewGuid();
        var (priced, priceError) = await PriceAsync(dto, purchaseId, allowInactive: new HashSet<Guid>(), forStaff: enteredWithoutPrice);
        if (priceError != null) return _log.Refuse<PurchaseDto>(priceError);

        var (termId, dueDate, termError) = await ResolveTermAsync(dto.PaymentType, dto.PaymentTermId, purchaseDate, currentTermId: null);
        if (termError != null) return _log.Refuse<PurchaseDto>(termError);

        // ── Receive stock ──────────────────────────────────────────────────────────
        // Inventory is costed ex-PPN: input VAT is reclaimable against output VAT, so
        // capitalising it into stock would overstate COGS by the full rate on every
        // item sold. The cost that lands is the line net of both discounts.
        var receipts = priced!.Items.Select(i => new PurchaseReceiptLine(
            i.ProductId, i.Qty, NetUnitCostExTax(i, dto.IsTaxInclusive, priced.TaxRate))).ToList();

        await _inventory.StockInForPurchaseAsync(receipts, purchaseId, branch.Id, LedgerDate.FromLocalDay(purchaseDate));

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
            Status                 = PurchaseStatus.Active,
            Notes                  = SanitiseText(dto.Notes, 500),
            CreatedBy              = user,
            CreatedAt              = DateTime.UtcNow,
            NeedsReview            = enteredWithoutPrice,
            EnteredWithoutPrice    = enteredWithoutPrice,
            PurchaseItems          = priced.Items
        };
        ApplyTotals(purchase, priced, dto);
        purchase.AmountPaid = dto.PaymentType == PaymentType.Cash ? purchase.GrandTotal : 0m;

        await _purchases.AddAsync(purchase);

        // Accrue Volume/PriceDrop rebates in the same transaction — the purchase and any
        // rebate it earns commit together or not at all.
        await _rebates.EvaluateOnPurchaseAsync(purchase, priced.Items);

        await _audit.LogAsync(user, enteredWithoutPrice ? "Purchase.CreateWithoutPrice" : "Purchase.Create",
            $"{purchase.PurchaseNumber} | {supplier.Name}" +
            (supplierDoc != null ? $" | doc {supplierDoc}" : "") +
            $" | {purchase.GrandTotal:N0}" + (enteredWithoutPrice ? " | priced at master harga beli, needs checking" : ""));

        _log.LogInformation(
            "Purchase {PurchaseNumber} posted — supplier {Supplier}, document {SupplierDocument}, " +
            "{LineCount} line(s) received, {GrandTotal} gross ({PaymentType}), by {User}{Unpriced}",
            purchase.PurchaseNumber, supplier.Name, supplierDoc ?? "—", priced.Items.Count,
            purchase.GrandTotal, purchase.PaymentType, user,
            enteredWithoutPrice ? " (Barang Masuk, needs checking)" : "");

        await _uow.SaveChangesAsync();

        var created = await _purchases.GetByIdWithItemsAsync(purchaseId);
        return ServiceResult<PurchaseDto>.Ok(MapDto(created!));
    }

    /// <summary>The document's priced lines and totals, before anything is written.</summary>
    private sealed record PricedDocument(List<PurchaseItem> Items, decimal InvoiceDiscount, decimal TaxBase,
        decimal TaxAmount, decimal GrandTotal, decimal TaxRate);

    /// <summary>
    /// Validates and prices every line, allocates the document discount and works out PPN — the
    /// arithmetic shared by create and revise. Line ids are kept when the caller names one
    /// (a revision matching its existing lines); otherwise new ids. forStaff keeps amounts out of
    /// the refusal text.
    /// </summary>
    private async Task<(PricedDocument? Doc, LocalizedString? Error)> PriceAsync(
        CreatePurchaseDto dto, Guid purchaseId, ISet<Guid> allowInactive, bool forStaff)
    {
        // Validate every line before anything is written.
        if (dto.Items.Count > Limits.MaxLines) return (null, _loc["Too many lines in one document (at most {0}).", Limits.MaxLines]);
        foreach (var item in dto.Items)
        {
            if (item.Qty <= 0)      return (null, _loc["All quantities must be > 0."]);
            if (item.UnitCost < 0)  return (null, _loc["Cost cannot be negative."]);
            if (item.Qty > Limits.MaxQty) return (null, _loc["Quantity is too large (at most {0} per line).", Limits.MaxQty.ToString("N0")]);
            if (item.UnitCost > Limits.MaxUnitAmount || item.UnitCost * item.Qty > Limits.MaxLineAmount)
                return (null, _loc["Amount is too large (at most {0}).", Limits.MaxUnitAmount.ToString("N0")]);

            // A supplied percent is resolved to an amount server-side, before any
            // validation runs — the client's own computed amount is never trusted.
            if (item.DiscountPercent.HasValue)
            {
                if (item.DiscountPercent < 0 || item.DiscountPercent >= 100)
                    return (null, _loc["Discount percent must be between 0 and 100."]);
                item.DiscountAmount = Math.Round(
                    item.UnitCost * item.DiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
            }

            if (item.DiscountAmount < 0)
                return (null, _loc["Discount cannot be negative."]);
            if (item.DiscountAmount >= item.UnitCost && item.UnitCost > 0)
                return (null, _loc["Discount cannot equal or exceed unit cost."]);

            item.Notes = SanitiseText(item.Notes, 500);

            var p = await _products.GetByIdAsync(item.ProductId);
            if (p == null)   return (null, _loc["Product not found."]);
            // A product deactivated since the purchase was posted may stay on it when revised.
            if (!p.IsActive && !allowInactive.Contains(p.Id)) return (null, _loc["Product '{0}' is inactive.", p.Name]);
        }

        var purchaseItems = new List<PurchaseItem>();
        foreach (var itemDto in dto.Items)
        {
            purchaseItems.Add(new PurchaseItem {
                Id              = itemDto.PurchaseItemId ?? Guid.NewGuid(),
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
                return (null, _loc["Document discount percent must be between 0 and 100."]);
            invoiceDiscount = Math.Round(
                lineNetTotal * dto.InvoiceDiscountPercent.Value / 100m, 2, MidpointRounding.AwayFromZero);
        }
        if (invoiceDiscount < 0)
            return (null, _loc["Document discount cannot be negative."]);
        if (invoiceDiscount >= lineNetTotal && lineNetTotal > 0)
            return (null, forStaff
                ? _loc["The document discount would be larger than the new total. Ask an administrator to correct this purchase."]
                : _loc["Document discount ({0}) cannot equal or exceed the total ({1}).", invoiceDiscount.ToString("N0"), lineNetTotal.ToString("N0")]);

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
        return (new PricedDocument(purchaseItems, invoiceDiscount, taxBase, taxAmount, grandTotal, taxRate), null);
    }

    /// <summary>
    /// Credit term and due date. Due date runs from the supplier's invoice date, not from today —
    /// a document entered a week late is already a week into its term. A term retired since
    /// the purchase was posted is still accepted when it is the one already on it.
    /// </summary>
    private async Task<(Guid? TermId, DateTime? DueDate, LocalizedString? Error)> ResolveTermAsync(
        PaymentType type, Guid? requestedTermId, DateTime purchaseDate, Guid? currentTermId)
    {
        if (type == PaymentType.Cash || !requestedTermId.HasValue) return (null, null, null);
        var term = await _terms.GetByIdAsync(requestedTermId.Value);
        if (term == null)
            return (null, null, _loc["Selected payment term not found."]);
        if (!term.IsActive && term.Id != currentTermId)
            return (null, null, _loc["Payment term '{0}' is no longer active.", term.Name]);
        return (term.Id, purchaseDate.AddDays(term.DueDays), null);
    }

    private static void ApplyTotals(Purchase purchase, PricedDocument priced, CreatePurchaseDto dto)
    {
        purchase.SubTotal               = priced.Items.Sum(i => i.UnitCost * i.Qty);
        purchase.DiscountTotal          = priced.Items.Sum(i => i.DiscountAmount * i.Qty);
        purchase.InvoiceDiscountAmount  = priced.InvoiceDiscount;
        purchase.InvoiceDiscountPercent = dto.InvoiceDiscountPercent;
        purchase.TaxBase                = priced.TaxBase;
        purchase.TaxRate                = priced.TaxRate;
        purchase.TaxAmount              = priced.TaxAmount;
        purchase.IsTaxInclusive         = dto.IsTaxInclusive;
        purchase.GrandTotal             = priced.GrandTotal;
    }

    /// <summary>
    /// The revision itself, for Admin (ReviseAsync) and for a Staff edit (EditUnpricedAsync).
    /// One transaction: if any step refuses — a guard, a re-cost that would drive stock
    /// negative, a closed month — nothing is written.
    /// </summary>
    private async Task<ServiceResult> ReviseCoreAsync(Purchase purchase, CreatePurchaseDto dto,
        bool markReviewed, string user, bool byStaff)
    {
        if (purchase.Status == PurchaseStatus.Cancelled)
            return _log.Refuse(_loc["Purchase is already cancelled."]);
        if (dto.SupplierId != purchase.SupplierId)
            return _log.Refuse(_loc["The supplier of a purchase cannot be changed. Cancel it and enter it again."]);
        if (dto.Items == null || dto.Items.Count == 0)
            return _log.Refuse(_loc["Add at least one item."]);
        // Cash ↔ credit would turn an automatic full payment into a debt or back: not a correction.
        dto.PaymentType = purchase.PaymentType;

        var todayLocal = DateTime.Now.Date;
        var newDate = dto.PurchaseDate?.Date ?? purchase.PurchaseDate.Date;
        if (newDate > todayLocal) return _log.Refuse(_loc["Purchase date cannot be in the future."]);
        if (await _period.ExistingAsync(purchase.PurchaseNumber, purchase.PurchaseDate.Date) is { } closedOld)
            return _log.Refuse(closedOld);
        if (await _period.NewDateAsync(newDate) is { } closedNew) return _log.Refuse(closedNew);

        // A supplier return's lines carry this purchase's line costs; revising under it would
        // leave the return describing a document that no longer exists.
        if (await _returns.HasActiveReturnAsync(purchase.Id))
            return _log.Refuse(_loc["This purchase has a supplier return against it. Cancel the return first, then revise."]);
        var accruals = await _rebateAccruals.GetByPurchaseAsync(purchase.Id);
        if (accruals.Any(a => a.RebateRealizationId != null && !a.IsVoided))
            return _log.Refuse(_loc["A rebate earned on this purchase has already been settled with the supplier, so it can no longer be revised."]);

        var supplierDoc = SanitiseText(dto.SupplierDocumentNumber, 100);
        if (supplierDoc != null &&
            await _purchases.SupplierDocumentExistsAsync(purchase.SupplierId, supplierDoc, purchase.Id))
            return _log.Refuse(_loc["'{0}' already has an active purchase with document number '{1}'. Cancel that one first if this is a correction.", purchase.Supplier?.Name ?? "", supplierDoc]);

        // Lines matched by id to the purchase's own; an id that isn't one of them is a new line.
        var ownIds = purchase.PurchaseItems.Select(i => i.Id).ToHashSet();
        foreach (var line in dto.Items)
            if (line.PurchaseItemId is { } lid && !ownIds.Contains(lid)) line.PurchaseItemId = null;

        var oldProducts = purchase.PurchaseItems.Select(i => i.ProductId).ToHashSet();
        var (priced, priceError) = await PriceAsync(dto, purchase.Id, allowInactive: oldProducts, forStaff: byStaff);
        if (priceError != null) return _log.Refuse(priceError);

        // A line can't be removed while a supplier return (even a cancelled one) points at it.
        var keptIds  = priced!.Items.Select(i => i.Id).ToHashSet();
        var removed  = purchase.PurchaseItems.Where(i => !keptIds.Contains(i.Id)).ToList();
        if (removed.Count > 0)
        {
            var returned = await _docsForPurchase.GetReturnedPurchaseItemIdsAsync(purchase.Id);
            if (removed.FirstOrDefault(i => returned.Contains(i.Id)) is { } locked)
                return _log.Refuse(_loc["The line for {0} has supplier-return history and cannot be removed. Set its quantity instead.", locked.Product?.Name ?? ""]);
        }

        var applied = await _noteApplications.GetAppliedTotalForPurchaseAsync(purchase.Id);
        var paidSoFar = purchase.PaymentType == PaymentType.Cash ? 0m : purchase.AmountPaid;
        if (priced.GrandTotal + 0.005m < paidSoFar + applied)
            return _log.Refuse(byStaff
                ? _loc["After this change the purchase would be worth less than what has already been paid or offset. Ask an administrator."]
                : _loc["The revised total ({0}) is less than what has already been paid or offset ({1}). Reverse a payment or note application first.",
                       priced.GrandTotal.ToString("N0"), (paidSoFar + applied).ToString("N0")]);

        var (termId, dueDate, termError) = await ResolveTermAsync(dto.PaymentType, dto.PaymentTermId, newDate, purchase.PaymentTermId);
        if (termError != null) return _log.Refuse(termError);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        // Before → after, for the audit. Staff edits record quantities only.
        string Line(Guid productId, decimal qty, decimal cost, decimal disc) =>
            byStaff ? $"{qty:0.##}" : $"{qty:0.##}×{cost:N0}" + (disc > 0 ? $"-{disc:N0}" : "");
        var names = purchase.PurchaseItems.Select(i => (i.ProductId, Name: i.Product?.SKU ?? ""))
            .Concat(priced.Items.Select(i => (i.ProductId, Name: i.Product?.SKU ?? "")))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First().Name);
        var before = purchase.PurchaseItems.Select(i => $"{names[i.ProductId]} {Line(i.ProductId, i.Qty, i.UnitCost, i.DiscountAmount)}").ToList();
        var after  = priced.Items.Select(i => $"{names[i.ProductId]} {Line(i.ProductId, i.Qty, i.UnitCost, i.DiscountAmount)}").ToList();
        var oldTotal = purchase.GrandTotal;

        // Did anything that prices the lines change? If not, rebates are left exactly as they are.
        string Sig(IEnumerable<PurchaseItem> items) => string.Join("|", items
            .OrderBy(i => i.ProductId).ThenBy(i => i.Qty)
            .Select(i => $"{i.ProductId}:{i.Qty:0.####}:{i.UnitCost:0.####}:{i.DiscountAmount:0.####}:{i.AllocatedInvoiceDiscount:0.####}"));
        var pricingChanged = Sig(purchase.PurchaseItems) != Sig(priced.Items)
                          || purchase.IsTaxInclusive != dto.IsTaxInclusive || purchase.PurchaseDate.Date != newDate;

        // New receipt per product, ex-PPN and net of discounts: what the stock is costed at.
        var receipts = priced.Items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => {
            var qty = g.Sum(i => i.Qty);
            var value = g.Sum(i => NetUnitCostExTax(i, dto.IsTaxInclusive, priced.TaxRate) * i.Qty);
            return new RecostReceipt(qty, qty == 0 ? 0m : value / qty);
        });
        var productNames = priced.Items.Concat(purchase.PurchaseItems)
            .GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.First().Product?.Name ?? "");

        ServiceResult result = ServiceResult.Ok();
        await _uow.InTransactionAsync(async () =>
        {
            await _inventory.LockStockAsync(oldProducts.Concat(receipts.Keys));
            var recost = await _recoster.RecostAsync(purchase.Id, branch.Id, LedgerDate.FromLocalDay(newDate),
                dateChanged: purchase.PurchaseDate.Date != newDate, receipts, oldProducts, productNames);
            if (!recost.Success) { result = ServiceResult.Fail(recost.Error!); return false; }

            // Lines: update the ones that stay (their ids are referenced by returns and accruals),
            // add the new ones, drop the removed ones.
            foreach (var p in priced.Items)
            {
                var own = purchase.PurchaseItems.FirstOrDefault(i => i.Id == p.Id);
                // A new line has its id preset (so the re-cost could name it); added through the
                // collection, EF would take it for an existing row and UPDATE nothing.
                if (own == null) { p.Product = null; p.PurchaseId = purchase.Id; await _purchases.AddItemAsync(p); continue; }
                own.ProductId = p.ProductId; own.Qty = p.Qty; own.UnitCost = p.UnitCost;
                own.DiscountAmount = p.DiscountAmount; own.DiscountPercent = p.DiscountPercent;
                own.LineTotal = p.LineTotal; own.AllocatedInvoiceDiscount = p.AllocatedInvoiceDiscount;
                own.Notes = p.Notes;
            }
            foreach (var r in removed) purchase.PurchaseItems.Remove(r);

            ApplyTotals(purchase, priced, dto);
            if (purchase.PaymentType == PaymentType.Cash) purchase.AmountPaid = purchase.GrandTotal;
            purchase.PurchaseDate           = newDate;
            purchase.SupplierDocumentNumber = supplierDoc;
            purchase.PaymentTermId          = termId;
            purchase.DueDate                = dueDate;
            purchase.Notes                  = SanitiseText(dto.Notes, 500);
            if (byStaff)
            {
                purchase.NeedsReview = true;
                purchase.LastStaffEditBy = user; purchase.LastStaffEditAt = DateTime.UtcNow;
            }
            else if (markReviewed)
            {
                purchase.NeedsReview = false;
                purchase.ReviewedBy = user; purchase.ReviewedAt = DateTime.UtcNow;
            }

            if (pricingChanged)
            {
                // Outstanding accruals are voided (never deleted) and the rules run again on the
                // corrected lines; settled ones were refused above.
                await _rebates.VoidAccrualsForPurchaseAsync(purchase.Id, user);
                await _rebates.EvaluateOnPurchaseAsync(purchase, purchase.PurchaseItems.ToList());
            }

            var detail = $"{purchase.PurchaseNumber} | {string.Join(", ", before)} -> {string.Join(", ", after)}";
            if (byStaff)
                await _audit.LogAsync(user, "Purchase.EditByStaff", detail + " | needs checking again");
            else
            {
                await _audit.LogAsync(user, "Purchase.Revise",
                    detail + $" | total {oldTotal:N0} -> {purchase.GrandTotal:N0}" +
                    $" | HPP corrected on {recost.Data!.SaleLinesChanged} sale line(s), {recost.Data.HppDelta:+#,0.##;-#,0.##;0}");
                if (markReviewed) await _audit.LogAsync(user, "Purchase.Review", purchase.PurchaseNumber);
            }
            _log.LogInformation(
                "Purchase {PurchaseNumber} revised by {User}{Staff}: {SaleLines} sale line(s) re-costed, HPP {HppDelta}",
                purchase.PurchaseNumber, user, byStaff ? " (Staff edit)" : "",
                recost.Data!.SaleLinesChanged, recost.Data.HppDelta);

            await _uow.SaveChangesAsync();
            return true;
        });
        return result;
    }

    public async Task<ServiceResult> CancelAsync(Guid purchaseId, string user)
    {
        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(purchaseId);
        var purchase = await _purchases.GetByIdWithItemsAsync(purchaseId);
        if (purchase == null) return _log.Refuse(_loc["Purchase not found."]);
        if (purchase.Status == PurchaseStatus.Cancelled)
            return _log.Refuse(_loc["Purchase is already cancelled."]);
        if (await _period.ExistingAsync(purchase.PurchaseNumber, purchase.PurchaseDate.Date) is { } closed)
            return _log.Refuse(closed);
        if (purchase.AmountPaid > 0)
            return _log.Refuse(_loc["{0} has already been paid against this purchase. Cancelling would leave that payment pointing at nothing — reverse the payment first, or record a supplier return instead.", purchase.AmountPaid.ToString("N0")]);

        // Same reasoning as the paid guard above, for the other thing that reduces this
        // purchase's balance.
        if (await _noteApplications.HasLiveApplicationsForPurchaseAsync(purchaseId))
            return _log.Refuse(_loc["A debit note has been applied to this purchase. Reverse the application first — cancelling now would leave it reducing a balance that no longer exists."]);

        // A supplier return has already sent some of these units back, so the stock guard
        // below would refuse anyway — but with a message about goods being sold. Say the
        // real reason, and point at the action that actually unblocks it.
        if (await _returns.HasActiveReturnAsync(purchaseId))
            return _log.Refuse(_loc["This purchase has a supplier return against it. Cancel the return first — its goods have already left stock and cannot be reversed twice."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        // The whole document goes in one call: every line is checked against a running
        // tally before anything is written, so a document whose stock is partly gone fails
        // whole rather than reversing halfway — and two lines of the same product can't
        // both be checked against the same pre-cancel figure.
        var reversal = await _inventory.StockOutForPurchaseCancelAsync(
            purchase.PurchaseItems.Select(i => new StockMovementLine(
                i.ProductId, i.Product?.Name ?? "", i.Qty, 0m)),
            purchaseId, branch.Id);
        if (!reversal.Success) return _log.Refuse(reversal.Error!);

        purchase.Status = PurchaseStatus.Cancelled;
        _purchases.Update(purchase);

        // Void (never delete) any rebate this purchase accrued — a settled one is left
        // alone, since that money already changed hands.
        await _rebates.VoidAccrualsForPurchaseAsync(purchaseId, user);

        await _audit.LogAsync(user, "Purchase.Cancel", purchase.PurchaseNumber);
        _log.LogInformation(
            "Purchase {PurchaseNumber} cancelled — {LineCount} line(s) taken back out of stock, " +
            "{GrandTotal} reversed, rebate accruals voided, by {User}",
            purchase.PurchaseNumber, purchase.PurchaseItems.Count, purchase.GrandTotal, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<SupplierPaymentDto>> RecordPaymentAsync(
        RecordSupplierPaymentDto dto, string user)
    {
        if (dto.Amount <= 0)
            return _log.Refuse<SupplierPaymentDto>(_loc["Payment amount must be > 0."]);

        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(dto.PurchaseId);
        var purchase = await _purchases.GetByIdWithItemsAsync(dto.PurchaseId);
        if (purchase == null) return _log.Refuse<SupplierPaymentDto>(_loc["Purchase not found."]);
        if (purchase.Status == PurchaseStatus.Cancelled)
            return _log.Refuse<SupplierPaymentDto>(_loc["Cannot pay a cancelled purchase."]);
        if (purchase.PaymentType == PaymentType.Cash)
            return _log.Refuse<SupplierPaymentDto>(_loc["This is a cash purchase — already paid."]);

        // Net of applied debit notes — the supplier mirror of the AR-side guard. A debit
        // note already reduced what we owe here, so the gross figure would let us pay
        // more than the remaining debt.
        var balance = purchase.GrandTotal - purchase.AmountPaid
                    - await _noteApplications.GetAppliedTotalForPurchaseAsync(purchase.Id);
        if (dto.Amount > balance)
            return _log.Refuse<SupplierPaymentDto>(_loc["Amount ({0}) exceeds balance owed ({1}).", dto.Amount.ToString("N0"), balance.ToString("N0")]);

        var record = await RecordPaymentCoreAsync(purchase, dto.Amount, dto.Notes, batchId: null, user);

        await _audit.LogAsync(user, "SupplierPayment.Record",
            $"{purchase.PurchaseNumber} -{dto.Amount:N0}");
        _log.LogInformation(
            "Payment {Amount} made against {PurchaseNumber} — paid {AmountPaid} of {GrandTotal}, by {User}",
            dto.Amount, purchase.PurchaseNumber, purchase.AmountPaid, purchase.GrandTotal, user);
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
            return _log.Refuse<PaymentBatchDto>(_loc["Enter an amount on at least one purchase."]);

        // One request at a time per document (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(lines.Select(l => l.PurchaseId).Concat(noteIds).ToArray());
        var supplier = await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null) return _log.Refuse<PaymentBatchDto>(_loc["Supplier not found."]);

        // One purchase can only appear once. Without this, two lines could each pass a
        // balance check read before either was applied, and jointly overpay.
        if (lines.GroupBy(l => l.PurchaseId).Any(g => g.Count() > 1))
            return _log.Refuse<PaymentBatchDto>(_loc["The same purchase appears twice — combine it into one row."]);

        // ── Validate every purchase line before anything is written ───────────────
        var purchases = await _purchases.GetByIdsWithItemsAsync(lines.Select(l => l.PurchaseId));
        var byId      = purchases.ToDictionary(p => p.Id);
        // Fetched once for the whole batch rather than per line inside the loop.
        var appliedByPurchase = await _noteApplications.GetAppliedTotalsForPurchasesAsync(lines.Select(l => l.PurchaseId));

        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.PurchaseId, out var purchase))
                return _log.Refuse<PaymentBatchDto>(_loc["A purchase on this settlement no longer exists."]);
            if (purchase.SupplierId != dto.SupplierId)
                return _log.Refuse<PaymentBatchDto>(_loc["Purchase {0} belongs to a different supplier.", purchase.PurchaseNumber]);
            if (purchase.Status == PurchaseStatus.Cancelled)
                return _log.Refuse<PaymentBatchDto>(_loc["Purchase {0} is cancelled.", purchase.PurchaseNumber]);
            if (purchase.PaymentType == PaymentType.Cash)
                return _log.Refuse<PaymentBatchDto>(_loc["Purchase {0} is a cash purchase — already paid.", purchase.PurchaseNumber]);

            // Net of notes already applied to this specific purchase — separate from, and
            // additional to, whatever notes get ticked into this settlement below.
            var balance = purchase.GrandTotal - purchase.AmountPaid - appliedByPurchase.GetValueOrDefault(purchase.Id);
            if (line.Amount > balance)
                return _log.Refuse<PaymentBatchDto>(_loc["Purchase {0}: {1} exceeds its balance of {2}.", purchase.PurchaseNumber, line.Amount.ToString("N0"), balance.ToString("N0")]);
        }

        // ── Validate every note before anything is written ────────────────────────
        var notes = noteIds.Count == 0 ? new List<CreditNote>() : await _notes.GetByIdsAsync(noteIds);
        if (notes.Count != noteIds.Count)
            return _log.Refuse<PaymentBatchDto>(_loc["A debit note on this settlement no longer exists."]);

        foreach (var note in notes)
        {
            if (note.Type != CreditDebitType.Debit)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} is a credit note and cannot be applied to money paid out.", note.DocumentNumber]);
            if (note.SupplierId != dto.SupplierId)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} belongs to a different supplier.", note.DocumentNumber]);
            if (note.Status != CreditNoteStatus.Open)
                return _log.Refuse<PaymentBatchDto>(_loc["{0} is already {1}.", note.DocumentNumber, _loc["CreditNoteStatus_" + note.Status].Value]);
        }

        // ── Post ─────────────────────────────────────────────────────────────────
        var gross = lines.Sum(l => l.Amount);
        // Each note nets its *remaining* value, not its face value: part of it may already
        // be applied to a specific purchase, and that slice has already reduced that
        // purchase's balance. Counting the face value here would net it a second time.
        var appliedByNote = await _noteApplications.GetAppliedTotalsForNotesAsync(noteIds);
        var notesApplied  = notes.Sum(n => n.Amount - appliedByNote.GetValueOrDefault(n.Id));

        // A note is applied whole or not at all, so netting more credit than is being paid
        // would settle notes whose value this settlement can't absorb — quietly writing off
        // money the supplier still owes back. Refuse, and say what to change.
        if (notesApplied > gross)
            return _log.Refuse<PaymentBatchDto>(_loc["The selected debit notes ({0}) exceed the {1} being paid. Include more purchases, or untick a note and settle it against a later payment.", notesApplied.ToString("N0"), gross.ToString("N0")]);

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
        _log.LogInformation(
            "Settlement {BatchNumber} paid to {Supplier} — {NetAmount} net " +
            "({Gross} gross less {NotesApplied} in notes) across {PurchaseCount} purchase(s) " +
            "and {NoteCount} note(s), by {User}",
            batch.BatchNumber, supplier.Name, batch.NetAmount, gross, notesApplied,
            lines.Count, notes.Count, user);
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

        // Both sides net of what's already been applied per document: the purchases so
        // each line shows what's genuinely left, and the notes so ticking one nets what it
        // can actually still absorb rather than its face value.
        var appliedByPurchase = await _noteApplications.GetAppliedTotalsForPurchasesAsync(due.Select(p => p.Id));
        var appliedByNote     = await _noteApplications.GetAppliedTotalsForNotesAsync(openNotes.Select(n => n.Id));

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
                DocumentId        = p.Id,
                DocumentNumber    = p.PurchaseNumber,
                TheirReference    = p.SupplierDocumentNumber,
                DocumentDate      = p.PurchaseDate,
                DueDate           = p.DueDate,
                GrandTotal        = p.GrandTotal,
                AmountPaid        = p.AmountPaid,
                AppliedNotesTotal = appliedByPurchase.GetValueOrDefault(p.Id)
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
                // A note fully consumed by per-purchase applications has nothing left to
                // tick, even though it is still technically Open until its last slice lands.
                .Where(n => n.Amount > 0)
                .ToList(),
            OutstandingRebateAmount = outstandingRebate > 0 ? outstandingRebate : null
        };
    }

    public async Task<PurchaseDto?> GetByIdAsync(Guid id)
    {
        var p = await _purchases.GetByIdWithItemsAsync(id);
        if (p == null) return null;
        var dto = MapDto(p);
        dto.AppliedNotesTotal = await _noteApplications.GetAppliedTotalForPurchaseAsync(id);
        return dto;
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

        // One query for the page, so the balance and overdue columns are net of notes.
        var applied = await _noteApplications.GetAppliedTotalsForPurchasesAsync(list.Select(p => p.Id));

        return list.Select(p => {
            var dto = MapListDto(p);
            dto.AppliedNotesTotal = applied.GetValueOrDefault(p.Id);
            return dto;
        }).ToList();
    }

    public async Task<List<DueSupplierDto>> GetDueSummaryAsync()
    {
        var due     = await _purchases.GetDuePurchasesAsync();
        // One query for the whole list, not one per purchase.
        var applied = await _noteApplications.GetAppliedTotalsForPurchasesAsync(due.Select(p => p.Id));

        // Same six buckets and boundary dates as PurchaseRepository.GetPayablesAgingAsync,
        // so these per-supplier rows sum back to that whole-ledger figure.
        var today = DateTime.UtcNow.Date;
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        return due
            .Select(p => new {
                Purchase = p,
                Net      = p.GrandTotal - p.AmountPaid - applied.GetValueOrDefault(p.Id)
            })
            .GroupBy(x => x.Purchase.SupplierId)
            .Select(g => new DueSupplierDto {
                SupplierId    = g.Key,
                SupplierName  = g.First().Purchase.Supplier?.Name ?? "",
                Phone         = g.First().Purchase.Supplier?.Phone,
                OpenPurchases = g.Count(),
                TotalDue      = g.Sum(x => x.Net),
                Aging         = new AgingBucketsDto {
                    NoDueDate  = g.Where(x => x.Purchase.DueDate == null).Sum(x => x.Net),
                    NotYetDue  = g.Where(x => x.Purchase.DueDate != null && x.Purchase.DueDate!.Value.Date >= today).Sum(x => x.Net),
                    Days1To30  = g.Where(x => x.Purchase.DueDate != null && x.Purchase.DueDate!.Value.Date <  today && x.Purchase.DueDate!.Value.Date >= d30).Sum(x => x.Net),
                    Days31To60 = g.Where(x => x.Purchase.DueDate != null && x.Purchase.DueDate!.Value.Date <  d30   && x.Purchase.DueDate!.Value.Date >= d60).Sum(x => x.Net),
                    Days61To90 = g.Where(x => x.Purchase.DueDate != null && x.Purchase.DueDate!.Value.Date <  d60   && x.Purchase.DueDate!.Value.Date >= d90).Sum(x => x.Net),
                    Days90Plus = g.Where(x => x.Purchase.DueDate != null && x.Purchase.DueDate!.Value.Date <  d90).Sum(x => x.Net)
                }
            })
            .OrderByDescending(d => d.HasOverdue)
            .ThenByDescending(d => d.TotalDue)
            .ToList();
    }

    // ── Barang Masuk lists (Staff): quantities only, no amount fields at all ──────────

    public async Task<List<PurchaseQtyListDto>> GetQtyListAsync(DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var list = await _purchases.GetAllAsync(from, to);
        if (!string.IsNullOrWhiteSpace(search))
            list = list.Where(p =>
                p.PurchaseNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (p.SupplierDocumentNumber ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (p.Supplier?.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        var withItems = (await _purchases.GetByIdsWithItemsAsync(list.Select(p => p.Id))).ToDictionary(p => p.Id);
        return list.Select(p => {
            var items = withItems.TryGetValue(p.Id, out var w) ? w.PurchaseItems.ToList() : new List<PurchaseItem>();
            return new PurchaseQtyListDto {
                Id = p.Id, PurchaseNumber = p.PurchaseNumber, SupplierDocumentNumber = p.SupplierDocumentNumber,
                PurchaseDate = p.PurchaseDate, SupplierName = p.Supplier?.Name ?? "", Status = p.Status.ToString(),
                NeedsReview = p.NeedsReview, EnteredWithoutPrice = p.EnteredWithoutPrice, CreatedBy = p.CreatedBy,
                LineCount = items.Count, TotalQty = items.Sum(i => i.Qty),
                Summary = string.Join(", ", items.Select(i => $"{i.Product?.Name} × {i.Qty:0.##}"))
            };
        }).ToList();
    }

    public async Task<PurchaseQtyDto?> GetQtyDetailAsync(Guid id)
    {
        var p = await _purchases.GetByIdWithItemsAsync(id);
        if (p == null) return null;
        return new PurchaseQtyDto {
            Id = p.Id, PurchaseNumber = p.PurchaseNumber, SupplierDocumentNumber = p.SupplierDocumentNumber,
            PurchaseDate = p.PurchaseDate, SupplierId = p.SupplierId, SupplierName = p.Supplier?.Name ?? "",
            Status = p.Status.ToString(), Notes = p.Notes, CreatedBy = p.CreatedBy, CreatedAt = p.CreatedAt,
            NeedsReview = p.NeedsReview, EnteredWithoutPrice = p.EnteredWithoutPrice,
            ReviewedBy = p.ReviewedBy, ReviewedAt = p.ReviewedAt,
            LastStaffEditBy = p.LastStaffEditBy, LastStaffEditAt = p.LastStaffEditAt,
            Lines = p.PurchaseItems.Select(i => new PurchaseQtyLineDto {
                PurchaseItemId = i.Id, ProductId = i.ProductId, ProductName = i.Product?.Name ?? "",
                SKU = i.Product?.SKU ?? "", Qty = i.Qty, Notes = i.Notes }).ToList()
        };
    }

    /// <summary>Admin's worklist: purchases still flagged Perlu dicek, oldest first.</summary>
    public async Task<List<PurchaseListDto>> GetNeedingReviewAsync() =>
        (await _purchases.GetNeedingReviewAsync()).Select(MapListDto).ToList();

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
    internal static decimal NetUnitCostExTax(PurchaseItem item, bool taxInclusive, decimal taxRate)
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
        s = TextClean.StripControl(s);
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
        SupplierId     = p.SupplierId,
        PaymentType    = p.PaymentType.ToString(),
        DueDate        = p.DueDate,
        GrandTotal     = p.GrandTotal,
        AmountPaid     = p.AmountPaid,
        Status         = p.Status.ToString(),
        NeedsReview    = p.NeedsReview,
        EnteredWithoutPrice = p.EnteredWithoutPrice
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
        CreatedAt      = p.CreatedAt,
        PaymentTermId  = p.PaymentTermId,
        NeedsReview    = p.NeedsReview,
        ReviewedBy     = p.ReviewedBy,
        ReviewedAt     = p.ReviewedAt,
        EnteredWithoutPrice = p.EnteredWithoutPrice,
        LastStaffEditBy = p.LastStaffEditBy,
        LastStaffEditAt = p.LastStaffEditAt,
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
