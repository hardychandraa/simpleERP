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
