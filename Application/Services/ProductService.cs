using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IInventoryLedgerRepository _ledger;
    private readonly IBranchRepository _branches;
    private readonly IUnitOfWork _uow;

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<ProductService> _log;
    public ProductService(IProductRepository products, IInventoryLedgerRepository ledger,
        IBranchRepository branches, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<ProductService> log)
    { _products = products; _ledger = ledger; _branches = branches; _uow = uow;  _loc = loc; _log = log; }

    public async Task<List<ProductDto>> GetAllAsync(string? search = null)
        => await MapList(await _products.GetAllAsync(), search);

    public async Task<List<ProductDto>> GetAllActiveAsync(string? search = null)
        => await MapList(await _products.GetAllActiveAsync(), search);

    public async Task<ProductDto?> GetByIdAsync(Guid id)
    {
        var p = await _products.GetByIdAsync(id);
        if (p == null) return null;
        var b = await _branches.GetDefaultAsync();
        if (b == null) return null;
        return Map(p, await _ledger.GetCurrentStockAsync(p.Id, b.Id),
                      await _ledger.GetCurrentAvgCostAsync(p.Id, b.Id));
    }

    public async Task<ServiceResult> CreateAsync(CreateProductDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Product name is required."]);
        if (string.IsNullOrWhiteSpace(dto.SKU))  return _log.Refuse(_loc["SKU is required."]);
        if (dto.UnitPrice < 0)   return _log.Refuse(_loc["Unit price cannot be negative."]);
        if (dto.PurchasePrice < 0) return _log.Refuse(_loc["Purchase price cannot be negative."]);
        if (dto.LowStockThreshold < 0) return _log.Refuse(_loc["Low stock threshold cannot be negative."]);
        var unitError = ValidateUnit(dto.Unit);
        if (unitError != null) return unitError;

        var sku = dto.SKU.Trim().ToUpper();
        if (await _products.SkuExistsAsync(sku))
            return _log.Refuse(_loc["SKU '{0}' is already used by another product.", sku]);

        await _products.AddAsync(new Product {
            Id = Guid.NewGuid(), Name = dto.Name.Trim().ToUpperInvariant(), SKU = sku,
            UnitPrice = dto.UnitPrice, PurchasePrice = dto.PurchasePrice, Unit = NormaliseUnit(dto.Unit), Category = NormaliseCategory(dto.Category),
            DefaultWarrantyMonths = dto.DefaultWarrantyMonths,
            LowStockThreshold = dto.LowStockThreshold,
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UpdateProductDto dto)
    {
        var p = await _products.GetByIdAsync(dto.Id);
        if (p == null) return _log.Refuse(_loc["Product not found."]);
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Product name is required."]);
        if (string.IsNullOrWhiteSpace(dto.SKU))  return _log.Refuse(_loc["SKU is required."]);
        if (dto.UnitPrice < 0)   return _log.Refuse(_loc["Unit price cannot be negative."]);
        if (dto.PurchasePrice < 0) return _log.Refuse(_loc["Purchase price cannot be negative."]);
        if (dto.LowStockThreshold < 0) return _log.Refuse(_loc["Threshold cannot be negative."]);
        var unitError = ValidateUnit(dto.Unit);
        if (unitError != null) return unitError;

        var sku = dto.SKU.Trim().ToUpper();
        if (await _products.SkuExistsAsync(sku, dto.Id))
            return _log.Refuse(_loc["SKU '{0}' is already used by another product.", sku]);

        p.Name = dto.Name.Trim().ToUpperInvariant(); p.SKU = sku;
        p.UnitPrice = dto.UnitPrice; p.PurchasePrice = dto.PurchasePrice; p.Unit = NormaliseUnit(dto.Unit); p.Category = NormaliseCategory(dto.Category);
        p.DefaultWarrantyMonths = dto.DefaultWarrantyMonths;
        p.LowStockThreshold = dto.LowStockThreshold;
        p.IsActive = dto.IsActive;
        _products.Update(p); await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(Guid id)
    {
        var p = await _products.GetByIdAsync(id);
        if (p == null) return _log.Refuse(_loc["Product not found."]);
        p.IsActive = false; _products.Update(p); await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<List<ProductDto>> MapList(List<Product> products, string? search)
    {
        var b = await _branches.GetDefaultAsync();
        var q = string.IsNullOrWhiteSpace(search) ? products
            : products.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                               || p.SKU.Contains(search,  StringComparison.OrdinalIgnoreCase)
                               || (p.Category ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<ProductDto>();
        foreach (var p in q) {
            decimal stock = 0, cost = 0;
            if (b != null) { stock = await _ledger.GetCurrentStockAsync(p.Id, b.Id); cost = await _ledger.GetCurrentAvgCostAsync(p.Id, b.Id); }
            result.Add(Map(p, stock, cost));
        }
        return result;
    }

    // Printed in a 5-character invoice column, hence the 10-character ceiling.
    private ServiceResult? ValidateUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return _log.Refuse(_loc["Unit is required (e.g. PCS, UNIT, SET)."]);
        if (unit.Trim().Length > 10)         return _log.Refuse(_loc["Unit cannot exceed 10 characters."]);
        return null;
    }

    private static string NormaliseUnit(string? unit) => (unit ?? "PCS").Trim().ToUpperInvariant();
    /// <summary>Upper-case like name and SKU (HC, 2026-10-06); blank means no category.</summary>
    private static string? NormaliseCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToUpperInvariant();

    private static ProductDto Map(Product p, decimal stock, decimal cost) => new() {
        Id = p.Id, Name = p.Name, SKU = p.SKU, UnitPrice = p.UnitPrice, PurchasePrice = p.PurchasePrice, Unit = p.Unit,
        Category = p.Category, DefaultWarrantyMonths = p.DefaultWarrantyMonths,
        LowStockThreshold = p.LowStockThreshold, IsActive = p.IsActive,
        CurrentStock = stock, AvgCost = cost
    };
}
