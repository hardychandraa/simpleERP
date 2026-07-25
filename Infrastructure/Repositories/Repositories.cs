using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;
using SimpleERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace SimpleERP.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;
    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}

public class BranchRepository : IBranchRepository
{
    private readonly AppDbContext _db;
    public BranchRepository(AppDbContext db) => _db = db;
    public Task<Branch?> GetDefaultAsync() => _db.Branches.FirstOrDefaultAsync(b => b.IsDefault);
    public Task<Branch?> GetByIdAsync(Guid id) => _db.Branches.FindAsync(id).AsTask();
}

public class ExpenseCategoryRepository : IExpenseCategoryRepository
{
    private readonly AppDbContext _db;
    public ExpenseCategoryRepository(AppDbContext db) => _db = db;

    public Task<List<ExpenseCategory>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.ExpenseCategories.AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);
        return q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
    }

    public Task<ExpenseCategory?> GetByIdAsync(Guid id) => _db.ExpenseCategories.FindAsync(id).AsTask();

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.ExpenseCategories.AnyAsync(c => c.Name.ToLower() == name.ToLower()
                                         && (excludeId == null || c.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) => _db.Expenses.AnyAsync(e => e.ExpenseCategoryId == id);

    public async Task AddAsync(ExpenseCategory c) => await _db.ExpenseCategories.AddAsync(c);
    public void Update(ExpenseCategory c) => _db.ExpenseCategories.Update(c);
    public void Remove(ExpenseCategory c) => _db.ExpenseCategories.Remove(c);
}

public class ExpenseRepository : IExpenseRepository
{
    private readonly AppDbContext _db;
    public ExpenseRepository(AppDbContext db) => _db = db;

    public Task<List<Expense>> GetAllAsync(DateTime? from = null, DateTime? to = null, Guid? categoryId = null)
    {
        var q = _db.Expenses.Include(e => e.Category).AsQueryable();
        if (from.HasValue)       q = q.Where(e => e.ExpenseDate >= from.Value);
        if (to.HasValue)         q = q.Where(e => e.ExpenseDate <  to.Value);
        if (categoryId.HasValue) q = q.Where(e => e.ExpenseCategoryId == categoryId.Value);
        return q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAt).ToListAsync();
    }

    public Task<Expense?> GetByIdAsync(Guid id) =>
        _db.Expenses.Include(e => e.Category).FirstOrDefaultAsync(e => e.Id == id);

    public async Task AddAsync(Expense e) => await _db.Expenses.AddAsync(e);
    public void Update(Expense e) => _db.Expenses.Update(e);
    public void Remove(Expense e) => _db.Expenses.Remove(e);

    public async Task<List<ExpenseCategoryTotal>> GetCategoryTotalsAsync(DateTime from, DateTime to)
    {
        // Grouped by the FK alone. Grouping by the joined category columns and
        // projecting straight into the record is not translatable — EF can't turn a
        // custom constructor over a joined GroupBy key into SQL, and silently
        // falls back to throwing rather than evaluating client-side.
        var rows = await _db.Expenses
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate < to)
            .GroupBy(e => e.ExpenseCategoryId)
            .Select(g => new {
                CategoryId = g.Key,
                Count      = g.Count(),
                Amount     = g.Sum(e => e.Amount)
            })
            .ToListAsync();

        if (rows.Count == 0) return new List<ExpenseCategoryTotal>();

        // One extra round-trip to resolve names — still set-based, not per-row.
        var ids  = rows.Select(r => r.CategoryId).ToList();
        var cats = await _db.ExpenseCategories
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        return rows
            .Where(r => cats.ContainsKey(r.CategoryId))
            .Select(r => new ExpenseCategoryTotal(
                r.CategoryId, cats[r.CategoryId].Name,
                cats[r.CategoryId].IsTaxDeductible, r.Count, r.Amount))
            .OrderBy(t => cats[t.CategoryId].SortOrder).ThenBy(t => t.CategoryName)
            .ToList();
    }
}

public class PaymentTermRepository : IPaymentTermRepository
{
    private readonly AppDbContext _db;
    public PaymentTermRepository(AppDbContext db) => _db = db;

    public Task<List<PaymentTerm>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.PaymentTerms.AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive);
        return q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
    }

    public Task<PaymentTerm?> GetByIdAsync(Guid id) => _db.PaymentTerms.FindAsync(id).AsTask();

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.PaymentTerms.AnyAsync(t => t.Name.ToLower() == name.ToLower()
                                    && (excludeId == null || t.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) =>
        _db.Sales.AnyAsync(s => s.PaymentTermId == id);

    public async Task AddAsync(PaymentTerm term) => await _db.PaymentTerms.AddAsync(term);
    public void Update(PaymentTerm term) => _db.PaymentTerms.Update(term);
    public void Remove(PaymentTerm term) => _db.PaymentTerms.Remove(term);
}

public class SupplierRepository : ISupplierRepository
{
    private readonly AppDbContext _db;
    public SupplierRepository(AppDbContext db) => _db = db;

    public Task<Supplier?> GetByIdAsync(Guid id) =>
        _db.Suppliers.Include(s => s.PaymentTerm).FirstOrDefaultAsync(s => s.Id == id);

    public Task<List<Supplier>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.Suppliers.Include(s => s.PaymentTerm).AsQueryable();
        if (activeOnly) q = q.Where(s => s.IsActive);
        return q.OrderBy(s => s.Name).ToListAsync();
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.Suppliers.AnyAsync(s => s.Name.ToLower() == name.ToLower()
                                 && (excludeId == null || s.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) => _db.Purchases.AnyAsync(p => p.SupplierId == id);

    public async Task AddAsync(Supplier s) => await _db.Suppliers.AddAsync(s);
    public void Update(Supplier s) => _db.Suppliers.Update(s);
    public void Remove(Supplier s) => _db.Suppliers.Remove(s);
}

public class PurchaseRepository : IPurchaseRepository
{
    private readonly AppDbContext _db;
    public PurchaseRepository(AppDbContext db) => _db = db;

    public Task<Purchase?> GetByIdWithItemsAsync(Guid id) =>
        _db.Purchases
           .Include(p => p.Supplier)
           .Include(p => p.Branch)
           .Include(p => p.PaymentTerm)
           .Include(p => p.PurchaseItems).ThenInclude(i => i.Product)
           .Include(p => p.SupplierPayments)
           .FirstOrDefaultAsync(p => p.Id == id);

    public Task<List<Purchase>> GetAllAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.Purchases.Include(p => p.Supplier).AsQueryable();
        if (from.HasValue) q = q.Where(p => p.PurchaseDate >= from.Value);
        if (to.HasValue)   q = q.Where(p => p.PurchaseDate <= to.Value);
        return q.OrderByDescending(p => p.PurchaseDate).ToListAsync();
    }

    public Task<List<Purchase>> GetDuePurchasesAsync() =>
        _db.Purchases
           .Include(p => p.Supplier)
           .Where(p => p.Status == PurchaseStatus.Active
                    && p.PaymentType != PaymentType.Cash
                    && p.AmountPaid < p.GrandTotal)
           .OrderBy(p => p.DueDate)
           .ThenBy(p => p.PurchaseDate)
           .ToListAsync();

    public async Task<string> GeneratePurchaseNumberAsync()
    {
        // Same count-then-append shape as GenerateInvoiceNumberAsync, and the same
        // caveat: two documents posted in the same instant could collide. The unique
        // index on PurchaseNumber turns that into a failed save rather than a
        // duplicate, which on a two-person system is the right trade.
        var prefix = $"PO-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.Purchases.CountAsync(p => p.PurchaseNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }

    public Task<bool> SupplierDocumentExistsAsync(Guid supplierId, string documentNumber, Guid? excludeId = null) =>
        _db.Purchases.AnyAsync(p => p.SupplierId == supplierId
                                 && p.SupplierDocumentNumber != null
                                 && p.SupplierDocumentNumber.ToLower() == documentNumber.ToLower()
                                 && p.Status == PurchaseStatus.Active
                                 && (excludeId == null || p.Id != excludeId));

    public async Task AddAsync(Purchase p) => await _db.Purchases.AddAsync(p);
    public void Update(Purchase p) => _db.Purchases.Update(p);

    public async Task<PurchasePeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to)
    {
        // decimal? casts so EF emits a SUM() that yields NULL rather than throwing
        // when no rows match — an empty period returns zeroes.
        var head = await _db.Purchases
            .Where(p => p.PurchaseDate >= from && p.PurchaseDate < to
                     && p.Status == PurchaseStatus.Active)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count = g.Count(),
                Net   = g.Sum(p => (decimal?)p.TaxBase)    ?? 0m,
                Tax   = g.Sum(p => (decimal?)p.TaxAmount)  ?? 0m,
                Gross = g.Sum(p => (decimal?)p.GrandTotal) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new PurchasePeriodTotals(
            PurchaseCount:  head?.Count ?? 0,
            NetPurchases:   head?.Net   ?? 0m,
            TaxPaid:        head?.Tax   ?? 0m,
            GrossPurchases: head?.Gross ?? 0m);
    }
}

public class SupplierPaymentRepository : ISupplierPaymentRepository
{
    private readonly AppDbContext _db;
    public SupplierPaymentRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SupplierPayment p) => await _db.SupplierPayments.AddAsync(p);

    public Task<List<SupplierPayment>> GetByPurchaseAsync(Guid purchaseId) =>
        _db.SupplierPayments.Where(p => p.PurchaseId == purchaseId)
                            .OrderByDescending(p => p.PaymentDate).ToListAsync();

    public Task<List<SupplierPayment>> GetByDateRangeAsync(DateTime from, DateTime to) =>
        _db.SupplierPayments.Include(p => p.Purchase!).ThenInclude(x => x.Supplier)
                            .Where(p => p.PaymentDate >= from && p.PaymentDate < to)
                            .OrderByDescending(p => p.PaymentDate).ToListAsync();
}

public class SalesPersonRepository : ISalesPersonRepository
{
    private readonly AppDbContext _db;
    public SalesPersonRepository(AppDbContext db) => _db = db;

    public Task<List<SalesPerson>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.SalesPersons.AsQueryable();
        if (activeOnly) q = q.Where(p => p.IsActive);
        return q.OrderBy(p => p.Name).ToListAsync();
    }

    public Task<SalesPerson?> GetByIdAsync(Guid id) => _db.SalesPersons.FindAsync(id).AsTask();

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.SalesPersons.AnyAsync(p => p.Name.ToLower() == name.ToLower()
                                    && (excludeId == null || p.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) =>
        _db.Sales.AnyAsync(s => s.SalesPersonId == id);

    public async Task AddAsync(SalesPerson person) => await _db.SalesPersons.AddAsync(person);
    public void Update(SalesPerson person) => _db.SalesPersons.Update(person);
    public void Remove(SalesPerson person) => _db.SalesPersons.Remove(person);
}

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public ProductRepository(AppDbContext db) => _db = db;
    public Task<Product?> GetByIdAsync(Guid id) => _db.Products.FindAsync(id).AsTask();
    public Task<List<Product>> GetAllActiveAsync() => _db.Products.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync();
    public Task<List<Product>> GetAllAsync()       => _db.Products.OrderBy(p => p.Name).ToListAsync();
    public async Task AddAsync(Product p) => await _db.Products.AddAsync(p);
    public void Update(Product p) => _db.Products.Update(p);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _db;
    public CustomerRepository(AppDbContext db) => _db = db;
    public Task<Customer?> GetByIdAsync(Guid id) => _db.Customers.FindAsync(id).AsTask();
    public Task<List<Customer>> GetAllActiveAsync() => _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
    public Task<List<Customer>> GetAllAsync()       => _db.Customers.OrderBy(c => c.Name).ToListAsync();
    public async Task AddAsync(Customer c) => await _db.Customers.AddAsync(c);
    public void Update(Customer c) => _db.Customers.Update(c);
}

public class InventoryLedgerRepository : IInventoryLedgerRepository
{
    private readonly AppDbContext _db;
    public InventoryLedgerRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(InventoryLedger e) => await _db.InventoryLedgers.AddAsync(e);

    public async Task<decimal> GetCurrentStockAsync(Guid productId, Guid branchId)
    {
        var q = _db.InventoryLedgers.Where(l => l.ProductId == productId && l.BranchId == branchId);
        return await q.SumAsync(l => l.QtyIn) - await q.SumAsync(l => l.QtyOut);
    }

    public async Task<decimal> GetCurrentAvgCostAsync(Guid productId, Guid branchId)
    {
        var last = await _db.InventoryLedgers
            .Where(l => l.ProductId == productId && l.BranchId == branchId && l.QtyIn > 0)
            .OrderByDescending(l => l.TransactionDate)
            .FirstOrDefaultAsync();
        return last?.UnitCost ?? 0;
    }

    public Task<List<InventoryLedger>> GetAllAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.InventoryLedgers.Include(l => l.Product).AsQueryable();
        if (from.HasValue) q = q.Where(l => l.TransactionDate >= from.Value);
        if (to.HasValue)   q = q.Where(l => l.TransactionDate <= to.Value);
        return q.OrderByDescending(l => l.TransactionDate).ToListAsync();
    }
}

public class SaleRepository : ISaleRepository
{
    private readonly AppDbContext _db;
    public SaleRepository(AppDbContext db) => _db = db;

    public Task<Sale?> GetByIdWithItemsAsync(Guid id) =>
        _db.Sales
           .Include(s => s.Customer)
           .Include(s => s.Branch)
           .Include(s => s.PaymentTerm)
           .Include(s => s.SalesPerson)
           .Include(s => s.SaleItems).ThenInclude(i => i.Product)
           .Include(s => s.PaymentRecords)
           .FirstOrDefaultAsync(s => s.Id == id);

    public Task<List<Sale>> GetAllAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.Sales.Include(s => s.Customer).Include(s => s.SalesPerson).AsQueryable();
        if (from.HasValue) q = q.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue)   q = q.Where(s => s.SaleDate <= to.Value);
        return q.OrderByDescending(s => s.SaleDate).ToListAsync();
    }

    public async Task<SalesPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to)
    {
        // Two aggregate round-trips, both translated to SQL. The decimal? casts make
        // EF emit SUM() that yields NULL (not an exception) when no rows match, so an
        // empty period returns zeroes instead of throwing.
        var head = await _db.Sales
            .Where(s => s.SaleDate >= from && s.SaleDate < to
                     && s.Status == SaleStatus.Active)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count      = g.Count(),
                Revenue    = g.Sum(s => (decimal?)s.TaxBase)    ?? 0m,
                Tax        = g.Sum(s => (decimal?)s.TaxAmount)  ?? 0m,
                GrossSales = g.Sum(s => (decimal?)s.GrandTotal) ?? 0m
            })
            .FirstOrDefaultAsync();

        // COGS uses the cost snapshotted onto each line at sale time, so restating
        // the product's moving-average cost later cannot rewrite historical margin.
        var cogs = await _db.SaleItems
            .Where(i => i.Sale!.SaleDate >= from && i.Sale.SaleDate < to
                     && i.Sale.Status == SaleStatus.Active)
            .SumAsync(i => (decimal?)(i.CostAtSale * i.Qty)) ?? 0m;

        return new SalesPeriodTotals(
            InvoiceCount: head?.Count      ?? 0,
            Revenue:      head?.Revenue    ?? 0m,
            Cogs:         cogs,
            TaxCollected: head?.Tax        ?? 0m,
            GrossSales:   head?.GrossSales ?? 0m);
    }

    public Task<List<Sale>> GetDueSalesAsync() =>
        _db.Sales
           .Include(s => s.Customer)
           .Where(s => s.Status == Domain.Enums.SaleStatus.Active
                    && s.AmountPaid < s.GrandTotal
                    && (s.PaymentType == Domain.Enums.PaymentType.Due
                     || s.PaymentType == Domain.Enums.PaymentType.TOP30
                     || s.PaymentType == Domain.Enums.PaymentType.TOP45
                     || s.PaymentType == Domain.Enums.PaymentType.TOP60
                     || s.PaymentType == Domain.Enums.PaymentType.TOP90))
           .OrderBy(s => s.DueDate)   // overdue first, then by due date
           .ThenBy(s => s.SaleDate)
           .ToListAsync();

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        var prefix = $"INV-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.Sales.CountAsync(s => s.InvoiceNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }

    public async Task AddAsync(Sale s) => await _db.Sales.AddAsync(s);
    public void Update(Sale s) => _db.Sales.Update(s);
}

public class PaymentRecordRepository : IPaymentRecordRepository
{
    private readonly AppDbContext _db;
    public PaymentRecordRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(PaymentRecord r) => await _db.PaymentRecords.AddAsync(r);

    public Task<List<PaymentRecord>> GetBySaleAsync(Guid saleId) =>
        _db.PaymentRecords.Where(p => p.SaleId == saleId)
                          .OrderByDescending(p => p.PaymentDate).ToListAsync();

    public async Task<decimal> GetTotalPaidAsync(Guid saleId) =>
        await _db.PaymentRecords.Where(p => p.SaleId == saleId).SumAsync(p => p.Amount);

    public Task<List<PaymentRecord>> GetByDateRangeAsync(DateTime from, DateTime to) =>
        _db.PaymentRecords.Include(p => p.Sale!).ThenInclude(s => s.Customer)
                          .Where(p => p.PaymentDate >= from && p.PaymentDate < to)
                          .OrderByDescending(p => p.PaymentDate).ToListAsync();
}

public class StockAdjustmentRepository : IStockAdjustmentRepository
{
    private readonly AppDbContext _db;
    public StockAdjustmentRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(StockAdjustment a) => await _db.StockAdjustments.AddAsync(a);

    public Task<List<StockAdjustment>> GetAllAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.StockAdjustments.Include(a => a.Product).AsQueryable();
        if (from.HasValue) q = q.Where(a => a.AdjustmentDate >= from.Value);
        if (to.HasValue)   q = q.Where(a => a.AdjustmentDate <= to.Value);
        return q.OrderByDescending(a => a.AdjustmentDate).ToListAsync();
    }
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;
    public AuditLogRepository(AppDbContext db) => _db = db;

    public async Task LogAsync(string user, string action, string? detail = null, string? ip = null)
    {
        await _db.AuditLogs.AddAsync(new AuditLog {
            Timestamp = DateTime.UtcNow, User = user,
            Action = action, Detail = detail, IpAddress = ip });
        await _db.SaveChangesAsync();  // Audit writes immediately, independent of UoW
    }

    public Task<List<AuditLog>> GetRecentAsync(int count = 100) =>
        _db.AuditLogs.OrderByDescending(l => l.Id).Take(count).ToListAsync();
}

public class AppSettingsRepository : IAppSettingsRepository
{
    private readonly AppDbContext _db;
    public AppSettingsRepository(AppDbContext db) => _db = db;

    public async Task<AppSettings> GetAsync()
    {
        var s = await _db.AppSettings.FindAsync("default");
        if (s != null) return s;
        s = new AppSettings();
        await _db.AppSettings.AddAsync(s);
        await _db.SaveChangesAsync();
        return s;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var existing = await _db.AppSettings.FindAsync("default");
        if (existing == null) await _db.AppSettings.AddAsync(settings);
        else _db.AppSettings.Update(settings);
        await _db.SaveChangesAsync();
    }
}
