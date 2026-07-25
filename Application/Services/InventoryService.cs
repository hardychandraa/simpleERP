using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
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

    public InventoryService(IInventoryLedgerRepository ledger, IProductRepository products,
        IBranchRepository branches, IStockAdjustmentRepository adjustments, IUnitOfWork uow)
    { _ledger=ledger; _products=products; _branches=branches; _adjustments=adjustments; _uow=uow; }

    public async Task<ServiceResult> StockInAsync(StockInDto dto)
    {
        if (dto.Qty <= 0)    return ServiceResult.Fail("Quantity must be > 0.");
        if (dto.UnitCost < 0) return ServiceResult.Fail("Cost cannot be negative.");
        if (await _products.GetByIdAsync(dto.ProductId) == null) return ServiceResult.Fail("Product not found.");
        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");
        await StockInCore(dto.ProductId, dto.Qty, dto.UnitCost, Guid.NewGuid(), branch.Id, ReferenceType.Purchase);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // Called inside SaleService transaction — does NOT SaveChanges
    public async Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId)
    {
        if (qty <= 0) return ServiceResult.Fail("Quantity must be > 0.");
        var stock = await _ledger.GetCurrentStockAsync(productId, branchId);
        if (stock < qty) return ServiceResult.Fail($"Insufficient stock. Available: {stock:N2}, Requested: {qty:N2}");
        var cost = await _ledger.GetCurrentAvgCostAsync(productId, branchId);
        await _ledger.AddAsync(new InventoryLedger {
            Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
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
        IEnumerable<PurchaseReceiptLine> lines, Guid purchaseId, Guid branchId)
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
                Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
                BranchId = branchId, ProductId = line.ProductId,
                ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = purchaseId,
                QtyIn = line.Qty, QtyOut = 0,
                UnitCost = newCost, TotalCost = line.Qty * newCost });

            running[line.ProductId] = (state.Stock + line.Qty, newCost);
        }
    }

    /// <summary>
    /// Reverses a purchase receipt on cancellation. Called inside the PurchaseService
    /// transaction — does NOT SaveChanges.
    ///
    /// Guarded: stock received on a purchase may already have been sold, and taking it
    /// back out would drive the ledger negative and corrupt every cost derived from it.
    /// Refusing here forces the correct answer — a supplier return (Step 8), which is a
    /// real business event — rather than silently rewriting history.
    /// </summary>
    public async Task<ServiceResult> StockOutForPurchaseCancelAsync(
        Guid productId, decimal qty, Guid referenceId, Guid branchId)
    {
        var stock = await _ledger.GetCurrentStockAsync(productId, branchId);
        if (stock < qty)
            return ServiceResult.Fail(
                $"Cannot cancel: only {stock:N2} of {qty:N2} received units are still in stock — " +
                "the rest has already been sold or issued. Record a supplier return instead.");

        var cost = await _ledger.GetCurrentAvgCostAsync(productId, branchId);
        await _ledger.AddAsync(new InventoryLedger {
            Id = Guid.NewGuid(), TransactionDate = DateTime.UtcNow,
            BranchId = branchId, ProductId = productId,
            ReferenceType = ReferenceType.Cancel, ReferenceId = referenceId,
            QtyIn = 0, QtyOut = qty, UnitCost = cost, TotalCost = cost * qty });
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AdjustStockAsync(StockAdjustmentDto dto, string user)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return ServiceResult.Fail("Reason is required.");
        if (dto.QtyActual < 0) return ServiceResult.Fail("Actual quantity cannot be negative.");

        var product = await _products.GetByIdAsync(dto.ProductId);
        if (product == null) return ServiceResult.Fail("Product not found.");

        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return ServiceResult.Fail("Default branch not found.");

        var currentStock = await _ledger.GetCurrentStockAsync(dto.ProductId, branch.Id);
        var delta = dto.QtyActual - currentStock;
        if (delta == 0) return ServiceResult.Fail("No difference between current stock and actual count. No adjustment needed.");

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

        await _adjustments.AddAsync(new StockAdjustment {
            Id = Guid.NewGuid(), ProductId = dto.ProductId, BranchId = branch.Id,
            AdjustmentDate = DateTime.UtcNow, QtyBefore = currentStock,
            QtyAfter = dto.QtyActual, Reason = dto.Reason.Trim(), CreatedBy = user });

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
        foreach (var p in await _products.GetAllActiveAsync()) {
            var s = await _ledger.GetCurrentStockAsync(p.Id, b.Id);
            var c = await _ledger.GetCurrentAvgCostAsync(p.Id, b.Id);
            result.Add(new StockLevelDto {
                ProductId = p.Id, ProductName = p.Name, SKU = p.SKU,
                CurrentStock = s, AvgCost = c, StockValue = s * c,
                LowStockThreshold = p.LowStockThreshold });
        }
        return result;
    }

    public async Task<List<InventoryLedgerDto>> GetLedgerAsync(DateTime? from = null, DateTime? to = null)
    {
        var entries = await _ledger.GetAllAsync(from, to);
        return entries.Select(e => new InventoryLedgerDto {
            Id = e.Id, TransactionDate = e.TransactionDate,
            ProductName = e.Product?.Name ?? "", ReferenceType = e.ReferenceType.ToString(),
            QtyIn = e.QtyIn, QtyOut = e.QtyOut, UnitCost = e.UnitCost, TotalCost = e.TotalCost
        }).ToList();
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
