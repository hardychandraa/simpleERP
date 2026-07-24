using SimpleERP.Domain.Entities;

namespace SimpleERP.Domain.Interfaces;

public interface IProductRepository {
    Task<Product?> GetByIdAsync(Guid id);
    Task<List<Product>> GetAllActiveAsync();
    Task<List<Product>> GetAllAsync();
    Task AddAsync(Product product);
    void Update(Product product);
}

public interface ICustomerRepository {
    Task<Customer?> GetByIdAsync(Guid id);
    Task<List<Customer>> GetAllActiveAsync();
    Task<List<Customer>> GetAllAsync();
    Task AddAsync(Customer customer);
    void Update(Customer customer);
}

public interface IBranchRepository {
    Task<Branch?> GetDefaultAsync();
    Task<Branch?> GetByIdAsync(Guid id);
}

public interface IInventoryLedgerRepository {
    Task AddAsync(InventoryLedger entry);
    Task<decimal> GetCurrentStockAsync(Guid productId, Guid branchId);
    Task<decimal> GetCurrentAvgCostAsync(Guid productId, Guid branchId);
    Task<List<InventoryLedger>> GetAllAsync(DateTime? from = null, DateTime? to = null);
}

public interface ISaleRepository {
    Task<Sale?> GetByIdWithItemsAsync(Guid id);
    Task<List<Sale>> GetAllAsync(DateTime? from = null, DateTime? to = null);
    Task<List<Sale>> GetDueSalesAsync();
    Task<string> GenerateInvoiceNumberAsync();
    Task AddAsync(Sale sale);
    void Update(Sale sale);
    /// <summary>
    /// Aggregated P&amp;L figures for [from, to), active sales only.
    /// Deliberately set-based (SUM/COUNT in SQL) rather than loading rows into
    /// memory — a P&amp;L can span a full year, which GetAllAsync would materialise.
    /// </summary>
    Task<SalesPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
}

/// <summary>
/// Period totals backing the P&amp;L report.
/// <paramref name="Revenue"/> is the ex-PPN taxable base, NOT GrandTotal: PPN is
/// collected on the government's behalf and is a liability, not turnover. Booking
/// it as revenue would overstate the top line and break reconciliation against the
/// consultant's statement. <paramref name="GrossSales"/> keeps the tax-inclusive
/// figure for tying back to cash/AR movements.
/// </summary>
public record SalesPeriodTotals(
    int     InvoiceCount,
    decimal Revenue,
    decimal Cogs,
    decimal TaxCollected,
    decimal GrossSales);

public interface IPaymentRecordRepository {
    Task AddAsync(PaymentRecord record);
    Task<List<PaymentRecord>> GetBySaleAsync(Guid saleId);
    Task<decimal> GetTotalPaidAsync(Guid saleId);
    /// <summary>Payments collected within [from, to), regardless of the sale's own date.</summary>
    Task<List<PaymentRecord>> GetByDateRangeAsync(DateTime from, DateTime to);
}

public interface IStockAdjustmentRepository {
    Task AddAsync(StockAdjustment adj);
    Task<List<StockAdjustment>> GetAllAsync(DateTime? from = null, DateTime? to = null);
}

public interface IAuditLogRepository {
    Task LogAsync(string user, string action, string? detail = null, string? ip = null);
    Task<List<AuditLog>> GetRecentAsync(int count = 100);
}

public interface IUnitOfWork {
    Task<int> SaveChangesAsync();
}

public interface IAppSettingsRepository {
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
}
