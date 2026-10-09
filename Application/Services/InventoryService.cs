using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class InventoryService : IInventoryService
{
    private readonly IInventoryLedgerRepository  _ledger;
    private readonly IProductRepository          _products;
    private readonly IBranchRepository           _branches;
    private readonly IStockAdjustmentRepository  _adjustments;
    private readonly IUnitOfWork                 _uow;

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<InventoryService> _log;
    public InventoryService(IInventoryLedgerRepository ledger, IProductRepository products,
        IBranchRepository branches, IStockAdjustmentRepository adjustments, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<InventoryService> log)
    { _ledger=ledger; _products=products; _branches=branches; _adjustments=adjustments; _uow=uow;  _loc = loc; _log = log; }

    public async Task<ServiceResult> StockInAsync(StockInDto dto)
    {
        if (dto.Qty <= 0)    return _log.Refuse(_loc["Quantity must be > 0."]);
        if (dto.UnitCost < 0) return _log.Refuse(_loc["Cost cannot be negative."]);
        if (dto.Qty > Limits.MaxQty) return _log.Refuse(_loc["Quantity is too large (at most {0} per line).", Limits.MaxQty.ToString("N0")]);
        if (dto.UnitCost > Limits.MaxUnitAmount) return _log.Refuse(_loc["Amount is too large (at most {0}).", Limits.MaxUnitAmount.ToString("N0")]);
        if (await _products.GetByIdAsync(dto.ProductId) == null) return _log.Refuse(_loc["Product not found."]);
        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);
        await StockInCore(dto.ProductId, dto.Qty, dto.UnitCost, Guid.NewGuid(), branch.Id, ReferenceType.Purchase);
        // Manual stock-in has no document behind it and — unlike sales, purchases and
        // returns — writes no audit entry either, so before this it moved stock and cost
        // leaving nothing but a ledger row tagged Purchase.
        _log.LogInformation(
            "Manual stock in — {Qty} units of product {ProductId} at {UnitCost} each",
            dto.Qty, dto.ProductId, dto.UnitCost);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // Called inside SaleService transaction — does NOT SaveChanges
    public async Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId,
        DateTime? at = null)
    {
        if (qty <= 0) return _log.Refuse(_loc["Quantity must be > 0."]);
        var stock = await _ledger.GetCurrentStockAsync(productId, branchId);
        if (stock < qty) return _log.Refuse(_loc["Insufficient stock. Available: {0}, Requested: {1}", stock.ToString("N2"), qty.ToString("N2")]);
        var cost = await _ledger.GetCurrentAvgCostAsync(productId, branchId);
        await _ledger.AddAsync(new InventoryLedger {
            Id = Guid.NewGuid(), TransactionDate = at ?? DateTime.UtcNow,
            BranchId = branchId, ProductId = productId,
            ReferenceType = ReferenceType.Sale, ReferenceId = referenceId,
            QtyIn = 0, QtyOut = qty, UnitCost = cost, TotalCost = cost * qty });
        return ServiceResult.Ok();
    }

    // Called inside CancelSale transaction — does NOT SaveChanges
    public async Task StockInForCancelAsync(Guid productId, decimal qty, decimal unitCost,
        Guid referenceId, Guid branchId)
        => await StockInCore(productId, qty, unitCost, referenceId, branchId, ReferenceType.Cancel);

    /// <summary>
    /// Receives every line of one purchase document. Called inside the PurchaseService
    /// transaction — does NOT SaveChanges.
    ///
    /// Takes the whole document at once rather than being called per line because the
    /// moving-average cost has to compound across lines: the current stock/cost helpers
    /// read the database, and nothing in this transaction is written yet, so a
    /// per-line call would compute every line's average from the same pre-purchase
    /// figures. A supplier invoice listing the same product twice — two batches, two
    /// prices, which is normal — would then land the wrong cost. The running tally
    /// here is what makes the second line see the first.
    ///
    /// Tagged PurchaseOrder, deliberately distinct from the Purchase tag used by the
    /// manual Stock In page, so rebate volume and AP reporting only sum real supplier
    /// documents.
    /// </summary>
    public async Task StockInForPurchaseAsync(
        IEnumerable<PurchaseReceiptLine> lines, Guid purchaseId, Guid branchId, DateTime? at = null)
    {
        var running = new Dictionary<Guid, (decimal Stock, decimal Cost)>();

        foreach (var line in lines)
        {
            if (!running.TryGetValue(line.ProductId, out var state))
                state = (await _ledger.GetCurrentStockAsync(line.ProductId, branchId),
                         await _ledger.GetCurrentAvgCostAsync(line.ProductId, branchId));

            var newCost = state.Stock <= 0
                ? line.UnitCost
                : (state.Stock * state.Cost + line.Qty * line.UnitCost) / (state.Stock + line.Qty);

            await _ledger.AddAsync(new InventoryLedger {
                Id = Guid.NewGuid(), TransactionDate = at ?? DateTime.UtcNow,
                BranchId = branchId, ProductId = line.ProductId,
                ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = purchaseId,
                QtyIn = line.Qty, QtyOut = 0,
                UnitCost = newCost, TotalCost = line.Qty * newCost });

            running[line.ProductId] = (state.Stock + line.Qty, newCost);
        }
    }

    /// <summary>
    /// Receives free rebate goods at zero cost, through the normal moving-average path.
    /// Called inside the RebateService transaction — does NOT SaveChanges.
    ///
    /// Zero cost is the point: blending in free units at 0 correctly pulls the product's
    /// moving-average cost down, which is the real economic effect of a rebate paid in
    /// goods — no special-casing needed anywhere downstream. Tagged RebateInKind so the
    /// units never count as purchase volume.
    /// </summary>
    public async Task StockInForRebateAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId)
    {
        var curStock = await _ledger.GetCurrentStockAsync(productId, branchId);
        // Free goods: total value unchanged, quantity up → new average = oldValue / newQty.
        var curCost = await _ledger.GetCurrentAvgCostAsync(productId, branchId);
        var newCost = curStock + qty <= 0 ? 0m : (curStock * curCost) / (curStock + qty);
        await _ledger.AddAsync(new InventoryLedger {
            Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
            BranchId = branchId, ProductId = productId,
            ReferenceType = ReferenceType.RebateInKind, ReferenceId = referenceId,
            QtyIn = qty, QtyOut = 0, UnitCost = newCost, TotalCost = qty * newCost });
    }

    /// <summary>
    /// Puts a sales return's goods back into stock, at the cost each line left at
    /// (SaleItem.CostAtSale). Called inside the ReturnService transaction — does NOT
    /// SaveChanges.
    ///
    /// Takes the whole document for the same reason StockInForPurchaseAsync does: the
    /// moving average has to compound across lines, and nothing in the transaction is
    /// committed yet, so a per-line loop would compute every line from the same
    /// pre-return figures.
    /// </summary>
    public Task StockInForCustomerReturnAsync(
        IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId, DateTime? at = null)
        => StockInManyCore(lines, returnId, branchId, ReferenceType.CustomerReturn, at);

    /// <summary>
    /// Reverses a sales return on cancellation — the goods go back out at the current
    /// moving-average cost, mirroring how a cancelled purchase is reversed. Guarded:
    /// the returned units may already have been re-sold. No SaveChanges.
    /// </summary>
    public Task<ServiceResult> StockOutForCustomerReturnCancelAsync(
        IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId)
        => StockOutManyCore(lines, returnId, branchId, ReferenceType.Cancel, useCurrentCost: true,
            shortfall: (name, stock, want) =>
                _loc["{0}: only {1} of the {2} returned units are still in stock — the rest has been sold again. This return can no longer be cancelled.",
                     name, stock.ToString("N2"), want.ToString("N2")]);

    /// <summary>
    /// Sends goods back to a supplier. Stock leaves at the moving-average cost the caller
    /// resolved (which is what the return records as CostAtReturn), not at the cost the
    /// supplier originally billed. Called inside the ReturnService transaction — does NOT
    /// SaveChanges.
    ///
    /// Guarded against driving the ledger negative, using the same shape as the proven
    /// sales StockOutAsync. The guard keeps a running tally rather than re-reading the
    /// database per line: nothing is committed yet, so two lines of the same product
    /// would otherwise both be checked against the same pre-return stock and could
    /// together issue more than exists.
    /// </summary>
    public Task<ServiceResult> StockOutForSupplierReturnAsync(
        IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId, DateTime? at = null)
        => StockOutManyCore(lines, returnId, branchId, ReferenceType.SupplierReturn, useCurrentCost: false, at: at,
            shortfall: (name, stock, want) =>
                _loc["{0}: only {1} in stock, {2} being returned. Stock cannot go negative — check the quantity, or adjust stock first.",
                     name, stock.ToString("N2"), want.ToString("N2")]);

    /// <summary>
    /// Reverses a supplier return on cancellation, putting the goods back at exactly the
    /// value they left at (SupplierReturnItem.CostAtReturn) rather than at whatever the
    /// average has drifted to since. No SaveChanges.
    /// </summary>
    public Task StockInForSupplierReturnCancelAsync(
        IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId)
        => StockInManyCore(lines, returnId, branchId, ReferenceType.Cancel);

    /// <summary>
    /// Reverses a whole purchase receipt on cancellation. Called inside the PurchaseService
    /// transaction — does NOT SaveChanges.
    ///
    /// Guarded: stock received on a purchase may already have been sold, and taking it back
    /// out would drive the ledger negative and corrupt every cost derived from it. Refusing
    /// forces the correct answer — a supplier return, a real business event — rather than
    /// silently rewriting history.
    ///
    /// Takes the whole document because the guard needs a running tally. This used to be
    /// called once per line, each call re-reading current stock from the database inside the
    /// still-uncommitted transaction, so a purchase listing the same product on two lines
    /// (two batches at two prices — normal) had both lines checked against the same
    /// pre-cancel figure and could jointly issue more than existed.
    /// </summary>
    public Task<ServiceResult> StockOutForPurchaseCancelAsync(
        IEnumerable<StockMovementLine> lines, Guid purchaseId, Guid branchId)
        => StockOutManyCore(lines, purchaseId, branchId, ReferenceType.Cancel, useCurrentCost: true,
            shortfall: (name, stock, want) =>
                _loc["{0}: only {1} of the {2} received units are still in stock — the rest has already been sold or issued. Record a supplier return instead.",
                     name, stock.ToString("N2"), want.ToString("N2")]);

    public Task LockStockAsync(IEnumerable<Guid> productIds) => _ledger.LockProductsAsync(productIds);

    public async Task<ServiceResult> AdjustStockAsync(StockAdjustmentDto dto, string user)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return _log.Refuse(_loc["Reason is required."]);
        if (dto.QtyActual < 0) return _log.Refuse(_loc["Actual quantity cannot be negative."]);
        if (dto.QtyActual > Limits.MaxQty) return _log.Refuse(_loc["Quantity is too large (at most {0} per line).", Limits.MaxQty.ToString("N0")]);

        var product = await _products.GetByIdAsync(dto.ProductId);
        if (product == null) return _log.Refuse(_loc["Product not found."]);

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse(_loc["Default branch not found."]);

        await _ledger.LockProductsAsync(new[] { dto.ProductId });
        var currentStock = await _ledger.GetCurrentStockAsync(dto.ProductId, branch.Id);
        var delta = dto.QtyActual - currentStock;
        if (delta == 0) return _log.Refuse(_loc["No difference between current stock and actual count. No adjustment needed."]);

        var currentCost = await _ledger.GetCurrentAvgCostAsync(dto.ProductId, branch.Id);
        var refId = Guid.NewGuid();

        if (delta > 0)
            await StockInCore(dto.ProductId, delta, currentCost, refId, branch.Id, ReferenceType.Adjustment);
        else
            await _ledger.AddAsync(new InventoryLedger {
                Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
                BranchId = branch.Id, ProductId = dto.ProductId,
                ReferenceType = ReferenceType.Adjustment, ReferenceId = refId,
                QtyIn = 0, QtyOut = Math.Abs(delta), UnitCost = currentCost, TotalCost = Math.Abs(delta) * currentCost });

        // The adjustment's id is the ledger's ReferenceId, so the Kartu Stok can show its reason.
        await _adjustments.AddAsync(new StockAdjustment {
            Id = refId, ProductId = dto.ProductId, BranchId = branch.Id,
            AdjustmentDate = DateTime.UtcNow, QtyBefore = currentStock,
            QtyAfter = dto.QtyActual, Reason = dto.Reason.Trim(), CreatedBy = user });

        // A stock adjustment writes off or writes on real value with no document behind
        // it, which makes it the most sensitive operation in the app to leave untraced.
        // The reason is logged verbatim because it is the only explanation that exists.
        _log.LogInformation(
            "Stock adjusted — {Product}: {QtyBefore} counted as {QtyActual} ({Delta:+#;-#;0}) " +
            "at {UnitCost} each, reason \"{Reason}\", by {User}",
            product.Name, currentStock, dto.QtyActual, delta, currentCost, dto.Reason.Trim(), user);

        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<decimal> GetCurrentStockAsync(Guid productId)
    { var b = await _branches.GetDefaultAsync(); return b == null ? 0 : await _ledger.GetCurrentStockAsync(productId, b.Id); }

    public async Task<decimal> GetCurrentAvgCostAsync(Guid productId)
    { var b = await _branches.GetDefaultAsync(); return b == null ? 0 : await _ledger.GetCurrentAvgCostAsync(productId, b.Id); }

    public async Task<List<StockLevelDto>> GetAllStockLevelsAsync()
    {
        var b = await _branches.GetDefaultAsync();
        if (b == null) return new();
        var result = new List<StockLevelDto>();
        // Active products, plus inactive ones that still hold stock: goods on the shelf must
        // stay visible and valued, or this total and the Position Summary's Persediaan
        // disagree (found by the 2026-10-07 regression: a deactivated product with 5 units).
        foreach (var p in await _products.GetAllAsync()) {
            var s = await _ledger.GetCurrentStockAsync(p.Id, b.Id);
            if (!p.IsActive && s == 0) continue;
            var c = await _ledger.GetCurrentAvgCostAsync(p.Id, b.Id);
            result.Add(new StockLevelDto {
                ProductId = p.Id, ProductName = p.Name, SKU = p.SKU,
                CurrentStock = s, AvgCost = c, StockValue = s * c,
                LowStockThreshold = p.LowStockThreshold, IsActive = p.IsActive });
        }
        return result;
    }

    public async Task<StockCardDto> GetStockCardAsync(Guid? productId, DateTime? fromUtc, DateTime? toUtc)
    {
        var entries = await _ledger.GetStockCardAsync(productId, fromUtc, toUtc);
        var opening = fromUtc.HasValue
            ? await _ledger.GetBalancesBeforeAsync(productId, fromUtc.Value)
            : new Dictionary<Guid, decimal>();
        var refs = await _ledger.ResolveReferencesAsync(entries.Select(e => e.ReferenceId));

        // Running balance per product, continuing from what it held before the period.
        var balance = new Dictionary<Guid, decimal>(opening);
        var rows = new List<InventoryLedgerDto>(entries.Count);
        foreach (var e in entries)
        {
            balance[e.ProductId] = balance.GetValueOrDefault(e.ProductId) + e.QtyIn - e.QtyOut;
            refs.TryGetValue(e.ReferenceId, out var r);
            rows.Add(new InventoryLedgerDto {
                Id = e.Id, TransactionDate = e.TransactionDate,
                ProductId = e.ProductId, ProductName = e.Product?.Name ?? "",
                ReferenceType = e.ReferenceType.ToString(),
                DocumentNumber = r?.DocumentNumber, Party = r?.Party, Link = r?.Link,
                QtyIn = e.QtyIn, QtyOut = e.QtyOut, Balance = balance[e.ProductId],
                UnitCost = e.UnitCost, TotalCost = e.TotalCost
            });
        }
        return new StockCardDto { Opening = opening, Rows = rows };
    }

    /// <summary>
    /// Receives many lines in one uncommitted transaction, compounding the moving average
    /// across them via a running tally. Each line is valued at its own supplied UnitCost.
    /// </summary>
    private async Task StockInManyCore(IEnumerable<StockMovementLine> lines,
        Guid referenceId, Guid branchId, ReferenceType refType, DateTime? at = null)
    {
        var running = new Dictionary<Guid, (decimal Stock, decimal Cost)>();

        foreach (var line in lines)
        {
            if (line.Qty <= 0) continue;

            if (!running.TryGetValue(line.ProductId, out var state))
                state = (await _ledger.GetCurrentStockAsync(line.ProductId, branchId),
                         await _ledger.GetCurrentAvgCostAsync(line.ProductId, branchId));

            var newCost = state.Stock <= 0
                ? line.UnitCost
                : (state.Stock * state.Cost + line.Qty * line.UnitCost) / (state.Stock + line.Qty);

            await _ledger.AddAsync(new InventoryLedger {
                Id = Guid.NewGuid(), TransactionDate = at ?? DateTime.UtcNow,
                BranchId = branchId, ProductId = line.ProductId,
                ReferenceType = refType, ReferenceId = referenceId,
                QtyIn = line.Qty, QtyOut = 0,
                UnitCost = newCost, TotalCost = line.Qty * newCost });

            running[line.ProductId] = (state.Stock + line.Qty, newCost);
        }
    }

    /// <summary>
    /// Issues many lines in one uncommitted transaction, refusing the whole document if
    /// any product would go negative. The available-stock tally runs in memory because
    /// nothing here is committed yet — re-reading the database per line would check two
    /// lines of the same product against the same figure and let them jointly overdraw.
    ///
    /// A stock-out never moves the moving average (that only changes on receipt), so the
    /// current cost is resolved once per product and reused across its lines.
    /// </summary>
    private async Task<ServiceResult> StockOutManyCore(IEnumerable<StockMovementLine> lines,
        Guid referenceId, Guid branchId, ReferenceType refType, bool useCurrentCost,
        Func<string, decimal, decimal, string> shortfall, DateTime? at = null)
    {
        var materialised = lines.Where(l => l.Qty > 0).ToList();
        await _ledger.LockProductsAsync(materialised.Select(l => l.ProductId));
        var available    = new Dictionary<Guid, decimal>();
        var currentCost  = new Dictionary<Guid, decimal>();

        // Validate every line first, so a document whose stock is partly gone fails whole
        // rather than issuing some lines before refusing.
        foreach (var line in materialised)
        {
            if (!available.TryGetValue(line.ProductId, out var stock))
            {
                stock = await _ledger.GetCurrentStockAsync(line.ProductId, branchId);
                currentCost[line.ProductId] = await _ledger.GetCurrentAvgCostAsync(line.ProductId, branchId);
            }
            if (stock < line.Qty)
                return _log.Refuse(shortfall(line.ProductName, stock, line.Qty));
            available[line.ProductId] = stock - line.Qty;
        }

        foreach (var line in materialised)
        {
            var cost = useCurrentCost ? currentCost[line.ProductId] : line.UnitCost;
            await _ledger.AddAsync(new InventoryLedger {
                Id = Guid.NewGuid(), TransactionDate = at ?? DateTime.UtcNow,
                BranchId = branchId, ProductId = line.ProductId,
                ReferenceType = refType, ReferenceId = referenceId,
                QtyIn = 0, QtyOut = line.Qty,
                UnitCost = cost, TotalCost = cost * line.Qty });
        }

        return ServiceResult.Ok();
    }

    private async Task StockInCore(Guid productId, decimal qty, decimal unitCost,
        Guid referenceId, Guid branchId, ReferenceType refType)
    {
        var curStock = await _ledger.GetCurrentStockAsync(productId, branchId);
        var curCost  = await _ledger.GetCurrentAvgCostAsync(productId, branchId);
        var newCost  = curStock <= 0 ? unitCost : (curStock * curCost + qty * unitCost) / (curStock + qty);
        await _ledger.AddAsync(new InventoryLedger {
            Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
            BranchId = branchId, ProductId = productId,
            ReferenceType = refType, ReferenceId = referenceId,
            QtyIn = qty, QtyOut = 0, UnitCost = newCost, TotalCost = qty * newCost });
    }
}
