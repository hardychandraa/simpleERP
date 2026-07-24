using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _products;
    private readonly IInventoryLedgerRepository _ledger;
    private readonly IBranchRepository _branches;
    private readonly IUnitOfWork _uow;

    public ProductService(IProductRepository products, IInventoryLedgerRepository ledger,
        IBranchRepository branches, IUnitOfWork uow)
    { _products = products; _ledger = ledger; _branches = branches; _uow = uow; }

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
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Product name is required.");
        if (string.IsNullOrWhiteSpace(dto.SKU))  return ServiceResult.Fail("SKU is required.");
        if (dto.UnitPrice < 0)   return ServiceResult.Fail("Unit price cannot be negative.");
        if (dto.LowStockThreshold < 0) return ServiceResult.Fail("Low stock threshold cannot be negative.");

        await _products.AddAsync(new Product {
            Id = Guid.NewGuid(), Name = dto.Name.Trim(), SKU = dto.SKU.Trim().ToUpper(),
            UnitPrice = dto.UnitPrice, Category = dto.Category?.Trim(),
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
        if (p == null) return ServiceResult.Fail("Product not found.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Product name is required.");
        if (dto.LowStockThreshold < 0) return ServiceResult.Fail("Threshold cannot be negative.");

        p.Name = dto.Name.Trim(); p.SKU = dto.SKU.Trim().ToUpper();
        p.UnitPrice = dto.UnitPrice; p.Category = dto.Category?.Trim();
        p.DefaultWarrantyMonths = dto.DefaultWarrantyMonths;
        p.LowStockThreshold = dto.LowStockThreshold;
        p.IsActive = dto.IsActive;
        _products.Update(p); await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeactivateAsync(Guid id)
    {
        var p = await _products.GetByIdAsync(id);
        if (p == null) return ServiceResult.Fail("Product not found.");
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

    private static ProductDto Map(Product p, decimal stock, decimal cost) => new() {
        Id = p.Id, Name = p.Name, SKU = p.SKU, UnitPrice = p.UnitPrice,
        Category = p.Category, DefaultWarrantyMonths = p.DefaultWarrantyMonths,
        LowStockThreshold = p.LowStockThreshold, IsActive = p.IsActive,
        CurrentStock = stock, AvgCost = cost
    };
}
