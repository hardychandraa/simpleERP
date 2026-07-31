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

    public Task<List<Purchase>> GetByIdsWithItemsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return _db.Purchases
           .Include(p => p.Supplier)
           .Include(p => p.Branch)
           .Include(p => p.PaymentTerm)
           .Include(p => p.PurchaseItems).ThenInclude(i => i.Product)
           .Include(p => p.SupplierPayments)
           .Where(p => list.Contains(p.Id))
           .AsSplitQuery()
           .ToListAsync();
    }

    public Task<List<Purchase>> GetDuePurchasesAsync(Guid? supplierId = null)
    {
        var q = _db.Purchases
           .Include(p => p.Supplier)
           .Where(p => p.Status == PurchaseStatus.Active
                    && p.PaymentType != PaymentType.Cash
                    && p.AmountPaid < p.GrandTotal);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
        return q.OrderBy(p => p.DueDate).ThenBy(p => p.PurchaseDate).ToListAsync();
    }

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

    public async Task<decimal> GetPurchasedQtyAsync(Guid supplierId, Guid productId, DateTime? from, DateTime? to)
    {
        var q = _db.PurchaseItems
            .Where(i => i.ProductId == productId
                     && i.Purchase!.SupplierId == supplierId
                     && i.Purchase.Status == PurchaseStatus.Active);
        if (from.HasValue) q = q.Where(i => i.Purchase!.PurchaseDate >= from.Value);
        if (to.HasValue)   q = q.Where(i => i.Purchase!.PurchaseDate <= to.Value);
        return await q.SumAsync(i => (decimal?)i.Qty) ?? 0m;
    }

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

public class RebateRuleRepository : IRebateRuleRepository
{
    private readonly AppDbContext _db;
    public RebateRuleRepository(AppDbContext db) => _db = db;

    public Task<RebateRule?> GetByIdAsync(Guid id) =>
        _db.RebateRules.Include(r => r.Supplier).Include(r => r.Product).Include(r => r.RewardProduct)
                       .FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<RebateRule>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.RebateRules.Include(r => r.Supplier).Include(r => r.Product).Include(r => r.RewardProduct).AsQueryable();
        if (activeOnly) q = q.Where(r => r.IsActive);
        return q.OrderBy(r => r.Supplier!.Name).ThenBy(r => r.Name).ToListAsync();
    }

    public Task<List<RebateRule>> GetActiveForSupplierAsync(Guid supplierId) =>
        _db.RebateRules.Include(r => r.RewardProduct)
                       .Where(r => r.IsActive && r.SupplierId == supplierId)
                       .ToListAsync();

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.RebateRules.AnyAsync(r => r.Name.ToLower() == name.ToLower()
                                   && (excludeId == null || r.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) => _db.RebateAccruals.AnyAsync(a => a.RebateRuleId == id);

    public async Task AddAsync(RebateRule rule) => await _db.RebateRules.AddAsync(rule);
    public void Update(RebateRule rule) => _db.RebateRules.Update(rule);
    public void Remove(RebateRule rule) => _db.RebateRules.Remove(rule);
}

public class RebateAccrualRepository : IRebateAccrualRepository
{
    private readonly AppDbContext _db;
    public RebateAccrualRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RebateAccrual accrual) => await _db.RebateAccruals.AddAsync(accrual);
    public void Update(RebateAccrual accrual) => _db.RebateAccruals.Update(accrual);

    private IQueryable<RebateAccrual> WithNav => _db.RebateAccruals
        .Include(a => a.Rule).Include(a => a.Supplier).Include(a => a.Purchase);

    public Task<RebateAccrual?> GetByIdAsync(Guid id) =>
        WithNav.FirstOrDefaultAsync(a => a.Id == id);

    public Task<List<RebateAccrual>> GetByPurchaseAsync(Guid purchaseId) =>
        WithNav.Where(a => a.PurchaseId == purchaseId)
               .OrderBy(a => a.AccrualDate).ToListAsync();

    public Task<List<RebateAccrual>> GetOutstandingBySupplierAsync(Guid supplierId) =>
        WithNav.Where(a => a.SupplierId == supplierId && a.RebateRealizationId == null && !a.IsVoided)
               .OrderBy(a => a.AccrualDate).ToListAsync();

    public Task<List<RebateAccrual>> GetAllAsync(Guid? supplierId = null, bool? outstandingOnly = null)
    {
        var q = WithNav.Where(a => !a.IsVoided);
        if (supplierId.HasValue)  q = q.Where(a => a.SupplierId == supplierId.Value);
        if (outstandingOnly == true)  q = q.Where(a => a.RebateRealizationId == null);
        if (outstandingOnly == false) q = q.Where(a => a.RebateRealizationId != null);
        return q.OrderByDescending(a => a.AccrualDate).ToListAsync();
    }

    public async Task<List<RebateOutstandingBySupplier>> GetOutstandingSummaryAsync()
    {
        // Grouped in SQL by supplier; the reward-type buckets are counted with
        // conditional sums so cash, in-kind and lucky-draw are separated in one pass.
        var rows = await _db.RebateAccruals
            .Where(a => a.RebateRealizationId == null && !a.IsVoided)
            .GroupBy(a => a.SupplierId)
            .Select(g => new {
                SupplierId = g.Key,
                CashCount  = g.Count(a => a.RewardType != RebateRewardType.InKindGoods
                                       && a.RewardType != RebateRewardType.LuckyDraw),
                CashAmount = g.Where(a => a.RewardType != RebateRewardType.InKindGoods
                                       && a.RewardType != RebateRewardType.LuckyDraw)
                              .Sum(a => (decimal?)a.Amount) ?? 0m,
                InKindCount   = g.Count(a => a.RewardType == RebateRewardType.InKindGoods),
                LuckyDrawCount= g.Count(a => a.RewardType == RebateRewardType.LuckyDraw)
            })
            .ToListAsync();

        if (rows.Count == 0) return new List<RebateOutstandingBySupplier>();

        var ids   = rows.Select(r => r.SupplierId).ToList();
        var names = await _db.Suppliers.Where(s => ids.Contains(s.Id))
                                       .ToDictionaryAsync(s => s.Id, s => s.Name);

        return rows.Select(r => new RebateOutstandingBySupplier(
                r.SupplierId, names.GetValueOrDefault(r.SupplierId, "?"),
                r.CashCount, r.CashAmount, r.InKindCount, r.LuckyDrawCount))
            .OrderByDescending(r => r.CashAmount)
            .ThenBy(r => r.SupplierName)
            .ToList();
    }
}

public class RebateRealizationRepository : IRebateRealizationRepository
{
    private readonly AppDbContext _db;
    public RebateRealizationRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RebateRealization realization) => await _db.RebateRealizations.AddAsync(realization);

    public Task<RebateRealization?> GetByIdAsync(Guid id) =>
        _db.RebateRealizations
           .Include(r => r.Supplier)
           .Include(r => r.InKindProduct)
           .Include(r => r.Accruals)
           .FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<RebateRealization>> GetAllAsync(Guid? supplierId = null, DateTime? from = null, DateTime? to = null)
    {
        var q = _db.RebateRealizations.Include(r => r.Supplier).Include(r => r.InKindProduct).AsQueryable();
        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        if (from.HasValue)       q = q.Where(r => r.RealizationDate >= from.Value);
        if (to.HasValue)         q = q.Where(r => r.RealizationDate <= to.Value);
        return q.OrderByDescending(r => r.RealizationDate).ToListAsync();
    }
}

public class CommissionRuleRepository : ICommissionRuleRepository
{
    private readonly AppDbContext _db;
    public CommissionRuleRepository(AppDbContext db) => _db = db;

    public Task<CommissionRule?> GetByIdAsync(Guid id) =>
        _db.CommissionRules.Include(r => r.SalesPerson).Include(r => r.Product)
                           .FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<CommissionRule>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.CommissionRules.Include(r => r.SalesPerson).Include(r => r.Product).AsQueryable();
        if (activeOnly) q = q.Where(r => r.IsActive);
        return q.OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToListAsync();
    }

    public Task<List<CommissionRule>> GetActiveForSalesPersonAsync(Guid salesPersonId) =>
        _db.CommissionRules
           .Where(r => r.IsActive && (r.SalesPersonId == null || r.SalesPersonId == salesPersonId))
           .ToListAsync();

    public Task<bool> NameExistsAsync(string name, Guid? excludeId = null) =>
        _db.CommissionRules.AnyAsync(r => r.Name.ToLower() == name.ToLower()
                                       && (excludeId == null || r.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) => _db.CommissionAccruals.AnyAsync(a => a.CommissionRuleId == id);

    public async Task AddAsync(CommissionRule rule) => await _db.CommissionRules.AddAsync(rule);
    public void Update(CommissionRule rule) => _db.CommissionRules.Update(rule);
    public void Remove(CommissionRule rule) => _db.CommissionRules.Remove(rule);
}

public class CommissionAccrualRepository : ICommissionAccrualRepository
{
    private readonly AppDbContext _db;
    public CommissionAccrualRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CommissionAccrual accrual) => await _db.CommissionAccruals.AddAsync(accrual);
    public void Update(CommissionAccrual accrual) => _db.CommissionAccruals.Update(accrual);

    private IQueryable<CommissionAccrual> WithNav => _db.CommissionAccruals
        .Include(a => a.Rule).Include(a => a.SalesPerson)
        .Include(a => a.Sale).Include(a => a.SaleItem).ThenInclude(i => i!.Product);

    public Task<List<CommissionAccrual>> GetBySaleAsync(Guid saleId) =>
        WithNav.Where(a => a.SaleId == saleId).OrderBy(a => a.AccrualDate).ToListAsync();

    public Task<List<CommissionAccrual>> GetUnpaidBySalesPersonAsync(Guid salesPersonId) =>
        WithNav.Where(a => a.SalesPersonId == salesPersonId && a.CommissionPayoutId == null && !a.IsVoided)
               .OrderBy(a => a.AccrualDate).ToListAsync();

    public Task<List<CommissionAccrual>> GetAllAsync(Guid? salesPersonId = null, bool? unpaidOnly = null)
    {
        var q = WithNav.Where(a => !a.IsVoided);
        if (salesPersonId.HasValue) q = q.Where(a => a.SalesPersonId == salesPersonId.Value);
        if (unpaidOnly == true)  q = q.Where(a => a.CommissionPayoutId == null);
        if (unpaidOnly == false) q = q.Where(a => a.CommissionPayoutId != null);
        return q.OrderByDescending(a => a.AccrualDate).ToListAsync();
    }

    public async Task<List<CommissionUnpaidBySalesPerson>> GetUnpaidSummaryAsync()
    {
        var rows = await _db.CommissionAccruals
            .Where(a => a.CommissionPayoutId == null && !a.IsVoided)
            .GroupBy(a => a.SalesPersonId)
            .Select(g => new { SalesPersonId = g.Key, Count = g.Count(), Amount = g.Sum(a => (decimal?)a.Amount) ?? 0m })
            .ToListAsync();

        if (rows.Count == 0) return new List<CommissionUnpaidBySalesPerson>();

        var ids   = rows.Select(r => r.SalesPersonId).ToList();
        var names = await _db.SalesPersons.Where(p => ids.Contains(p.Id))
                                          .ToDictionaryAsync(p => p.Id, p => p.Name);

        return rows.Select(r => new CommissionUnpaidBySalesPerson(
                r.SalesPersonId, names.GetValueOrDefault(r.SalesPersonId, "?"), r.Count, r.Amount))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.SalesPersonName)
            .ToList();
    }
}

public class CommissionPayoutRepository : ICommissionPayoutRepository
{
    private readonly AppDbContext _db;
    public CommissionPayoutRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(CommissionPayout payout) => await _db.CommissionPayouts.AddAsync(payout);

    public Task<CommissionPayout?> GetByIdAsync(Guid id) =>
        _db.CommissionPayouts.Include(p => p.SalesPerson).Include(p => p.Accruals)
                             .FirstOrDefaultAsync(p => p.Id == id);

    public Task<List<CommissionPayout>> GetAllAsync(Guid? salesPersonId = null)
    {
        // Accruals included so the list can show how many each payout settled. Bounded
        // by payout volume, which is low (one per salesperson per pay run).
        var q = _db.CommissionPayouts.Include(p => p.SalesPerson).Include(p => p.Accruals).AsQueryable();
        if (salesPersonId.HasValue) q = q.Where(p => p.SalesPersonId == salesPersonId.Value);
        return q.OrderByDescending(p => p.PayoutDate).ToListAsync();
    }
}

public class CustomerReturnRepository : ICustomerReturnRepository
{
    private readonly AppDbContext _db;
    public CustomerReturnRepository(AppDbContext db) => _db = db;

    public Task<CustomerReturn?> GetByIdWithItemsAsync(Guid id) =>
        _db.CustomerReturns
           .Include(r => r.Sale!).ThenInclude(s => s.Customer)
           .Include(r => r.Items).ThenInclude(i => i.Product)
           .Include(r => r.CreditNotes)
           .FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<CustomerReturn>> GetAllAsync(
        DateTime? from = null, DateTime? to = null, string? search = null)
    {
        // Items are included for the line count on the list. Bounded by return volume,
        // which is a small fraction of sales volume.
        var q = _db.CustomerReturns
                   .Include(r => r.Sale!).ThenInclude(s => s.Customer)
                   .Include(r => r.Items)
                   .AsQueryable();
        if (from.HasValue) q = q.Where(r => r.ReturnDate >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.ReturnDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(r => r.ReturnNumber.ToLower().Contains(s)
                          || r.Sale!.InvoiceNumber.ToLower().Contains(s)
                          || r.Sale.Customer!.Name.ToLower().Contains(s));
        }
        return q.OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.CreatedAt).ToListAsync();
    }

    public Task<List<CustomerReturn>> GetBySaleAsync(Guid saleId) =>
        _db.CustomerReturns
           .Include(r => r.Sale!).ThenInclude(s => s.Customer)
           .Include(r => r.Items)
           .Where(r => r.SaleId == saleId)
           .OrderBy(r => r.ReturnDate).ToListAsync();

    public async Task<string> GenerateReturnNumberAsync()
    {
        // Same count-then-append shape as GenerateInvoiceNumberAsync/GeneratePurchaseNumberAsync,
        // with the same caveat: two returns posted in the same instant could collide, and the
        // unique index turns that into a failed save rather than a duplicate number.
        var prefix = $"CRN-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.CustomerReturns.CountAsync(r => r.ReturnNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }

    public async Task<Dictionary<Guid, ReturnedLineTally>> GetReturnedQtyBySaleItemAsync(Guid saleId)
    {
        // Cancelled returns don't count — their goods went back out of stock, so the
        // quantity is returnable again.
        var rows = await _db.CustomerReturnItems
            .Where(i => i.Return!.SaleId == saleId && i.Return.Status == ReturnStatus.Active)
            .GroupBy(i => i.SaleItemId)
            .Select(g => new {
                SaleItemId = g.Key,
                Qty        = g.Sum(i => (decimal?)i.Qty)          ?? 0m,
                Amount     = g.Sum(i => (decimal?)i.CreditAmount) ?? 0m
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.SaleItemId, r => new ReturnedLineTally(r.Qty, r.Amount));
    }

    public Task<bool> HasActiveReturnAsync(Guid saleId) =>
        _db.CustomerReturns.AnyAsync(r => r.SaleId == saleId && r.Status == ReturnStatus.Active);

    public async Task AddAsync(CustomerReturn ret) => await _db.CustomerReturns.AddAsync(ret);
    public void Update(CustomerReturn ret) => _db.CustomerReturns.Update(ret);

    public async Task<ReturnPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to)
    {
        var head = await _db.CustomerReturns
            .Where(r => r.ReturnDate >= from && r.ReturnDate < to && r.Status == ReturnStatus.Active)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count = g.Count(),
                Net   = g.Sum(r => (decimal?)r.TaxBase)    ?? 0m,
                Tax   = g.Sum(r => (decimal?)r.TaxAmount)  ?? 0m,
                Gross = g.Sum(r => (decimal?)r.GrandTotal) ?? 0m
            })
            .FirstOrDefaultAsync();

        // Cost put back into stock — the COGS reversal that has to accompany the revenue one.
        var stock = await _db.CustomerReturnItems
            .Where(i => i.Return!.ReturnDate >= from && i.Return.ReturnDate < to
                     && i.Return.Status == ReturnStatus.Active)
            .SumAsync(i => (decimal?)(i.CostAtSale * i.Qty)) ?? 0m;

        return new ReturnPeriodTotals(
            ReturnCount: head?.Count ?? 0,
            NetAmount:   head?.Net   ?? 0m,
            TaxReversed: head?.Tax   ?? 0m,
            GrossAmount: head?.Gross ?? 0m,
            StockValue:  stock);
    }
}

public class SupplierReturnRepository : ISupplierReturnRepository
{
    private readonly AppDbContext _db;
    public SupplierReturnRepository(AppDbContext db) => _db = db;

    public Task<SupplierReturn?> GetByIdWithItemsAsync(Guid id) =>
        _db.SupplierReturns
           .Include(r => r.Purchase!).ThenInclude(p => p.Supplier)
           .Include(r => r.Items).ThenInclude(i => i.Product)
           .Include(r => r.CreditNotes)
           .FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<SupplierReturn>> GetAllAsync(
        DateTime? from = null, DateTime? to = null, string? search = null)
    {
        var q = _db.SupplierReturns
                   .Include(r => r.Purchase!).ThenInclude(p => p.Supplier)
                   .Include(r => r.Items)
                   .AsQueryable();
        if (from.HasValue) q = q.Where(r => r.ReturnDate >= from.Value);
        if (to.HasValue)   q = q.Where(r => r.ReturnDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(r => r.ReturnNumber.ToLower().Contains(s)
                          || r.Purchase!.PurchaseNumber.ToLower().Contains(s)
                          || (r.Purchase.SupplierDocumentNumber ?? "").ToLower().Contains(s)
                          || r.Purchase.Supplier!.Name.ToLower().Contains(s));
        }
        return q.OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.CreatedAt).ToListAsync();
    }

    public Task<List<SupplierReturn>> GetByPurchaseAsync(Guid purchaseId) =>
        _db.SupplierReturns
           .Include(r => r.Purchase!).ThenInclude(p => p.Supplier)
           .Include(r => r.Items)
           .Where(r => r.PurchaseId == purchaseId)
           .OrderBy(r => r.ReturnDate).ToListAsync();

    public async Task<string> GenerateReturnNumberAsync()
    {
        var prefix = $"SRN-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.SupplierReturns.CountAsync(r => r.ReturnNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }

    public async Task<Dictionary<Guid, ReturnedLineTally>> GetReturnedQtyByPurchaseItemAsync(Guid purchaseId)
    {
        var rows = await _db.SupplierReturnItems
            .Where(i => i.Return!.PurchaseId == purchaseId && i.Return.Status == ReturnStatus.Active)
            .GroupBy(i => i.PurchaseItemId)
            .Select(g => new {
                PurchaseItemId = g.Key,
                Qty            = g.Sum(i => (decimal?)i.Qty)         ?? 0m,
                Amount         = g.Sum(i => (decimal?)i.DebitAmount) ?? 0m
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.PurchaseItemId, r => new ReturnedLineTally(r.Qty, r.Amount));
    }

    public Task<bool> HasActiveReturnAsync(Guid purchaseId) =>
        _db.SupplierReturns.AnyAsync(r => r.PurchaseId == purchaseId && r.Status == ReturnStatus.Active);

    public async Task AddAsync(SupplierReturn ret) => await _db.SupplierReturns.AddAsync(ret);
    public void Update(SupplierReturn ret) => _db.SupplierReturns.Update(ret);

    public async Task<ReturnPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to)
    {
        var head = await _db.SupplierReturns
            .Where(r => r.ReturnDate >= from && r.ReturnDate < to && r.Status == ReturnStatus.Active)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count = g.Count(),
                Net   = g.Sum(r => (decimal?)r.TaxBase)    ?? 0m,
                Tax   = g.Sum(r => (decimal?)r.TaxAmount)  ?? 0m,
                Gross = g.Sum(r => (decimal?)r.GrandTotal) ?? 0m
            })
            .FirstOrDefaultAsync();

        // Inventory value released — the moving-average cost the goods left at, which is
        // deliberately not the same as what the supplier credits back.
        var stock = await _db.SupplierReturnItems
            .Where(i => i.Return!.ReturnDate >= from && i.Return.ReturnDate < to
                     && i.Return.Status == ReturnStatus.Active)
            .SumAsync(i => (decimal?)(i.CostAtReturn * i.Qty)) ?? 0m;

        return new ReturnPeriodTotals(
            ReturnCount: head?.Count ?? 0,
            NetAmount:   head?.Net   ?? 0m,
            TaxReversed: head?.Tax   ?? 0m,
            GrossAmount: head?.Gross ?? 0m,
            StockValue:  stock);
    }
}

public class CreditNoteRepository : ICreditNoteRepository
{
    private readonly AppDbContext _db;
    public CreditNoteRepository(AppDbContext db) => _db = db;

    private IQueryable<CreditNote> WithNav => _db.CreditNotes
        .Include(n => n.Customer).Include(n => n.Supplier)
        .Include(n => n.SourceSale).Include(n => n.SourcePurchase)
        .Include(n => n.SourceCustomerReturn).Include(n => n.SourceSupplierReturn);

    public Task<CreditNote?> GetByIdAsync(Guid id) => WithNav.FirstOrDefaultAsync(n => n.Id == id);

    public Task<List<CreditNote>> GetAllAsync(CreditDebitType? type = null,
        CreditNoteStatus? status = null, DateTime? from = null, DateTime? to = null,
        Guid? customerId = null, Guid? supplierId = null)
    {
        var q = WithNav;
        if (type.HasValue)       q = q.Where(n => n.Type == type.Value);
        if (status.HasValue)     q = q.Where(n => n.Status == status.Value);
        if (from.HasValue)       q = q.Where(n => n.NoteDate >= from.Value);
        if (to.HasValue)         q = q.Where(n => n.NoteDate <= to.Value);
        if (customerId.HasValue) q = q.Where(n => n.CustomerId == customerId.Value);
        if (supplierId.HasValue) q = q.Where(n => n.SupplierId == supplierId.Value);
        return q.OrderByDescending(n => n.NoteDate).ThenByDescending(n => n.CreatedAt).ToListAsync();
    }

    public Task<List<CreditNote>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return WithNav.Where(n => list.Contains(n.Id)).ToListAsync();
    }

    public Task<CreditNote?> GetByCustomerReturnAsync(Guid customerReturnId) =>
        WithNav.FirstOrDefaultAsync(n => n.SourceCustomerReturnId == customerReturnId
                                      && n.Status != CreditNoteStatus.Cancelled);

    public Task<CreditNote?> GetBySupplierReturnAsync(Guid supplierReturnId) =>
        WithNav.FirstOrDefaultAsync(n => n.SourceSupplierReturnId == supplierReturnId
                                      && n.Status != CreditNoteStatus.Cancelled);

    public async Task<string> GenerateDocumentNumberAsync(CreditDebitType type)
    {
        // Two independent sequences, so a credit note and a debit note raised in the same
        // month never share a number. Same count-then-append caveat as the other generators.
        var prefix = $"{(type == CreditDebitType.Credit ? "CN" : "DN")}-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.CreditNotes.CountAsync(n => n.DocumentNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }

    public async Task<decimal> GetOpenTotalAsync(CreditDebitType type) =>
        await _db.CreditNotes
            .Where(n => n.Type == type && n.Status == CreditNoteStatus.Open)
            .SumAsync(n => (decimal?)n.Amount) ?? 0m;

    public async Task AddAsync(CreditNote note) => await _db.CreditNotes.AddAsync(note);
    public void Update(CreditNote note) => _db.CreditNotes.Update(note);
}

public class PaymentBatchRepository : IPaymentBatchRepository
{
    private readonly AppDbContext _db;
    public PaymentBatchRepository(AppDbContext db) => _db = db;

    private IQueryable<PaymentBatch> WithNav => _db.PaymentBatches
        .Include(b => b.Customer).Include(b => b.Supplier)
        .Include(b => b.Payments).Include(b => b.SupplierPayments)
        .Include(b => b.AppliedNotes);

    public async Task AddAsync(PaymentBatch batch) => await _db.PaymentBatches.AddAsync(batch);

    public Task<PaymentBatch?> GetByIdAsync(Guid id) =>
        WithNav.AsSplitQuery().FirstOrDefaultAsync(b => b.Id == id);

    public Task<List<PaymentBatch>> GetAllAsync(PaymentBatchDirection? direction = null,
        Guid? customerId = null, Guid? supplierId = null,
        DateTime? from = null, DateTime? to = null)
    {
        var q = WithNav.AsSplitQuery();
        if (direction.HasValue)  q = q.Where(b => b.Direction == direction.Value);
        if (customerId.HasValue) q = q.Where(b => b.CustomerId == customerId.Value);
        if (supplierId.HasValue) q = q.Where(b => b.SupplierId == supplierId.Value);
        if (from.HasValue)       q = q.Where(b => b.BatchDate >= from.Value);
        if (to.HasValue)         q = q.Where(b => b.BatchDate <= to.Value);
        return q.OrderByDescending(b => b.BatchDate).ThenByDescending(b => b.CreatedAt).ToListAsync();
    }

    public async Task<string> GenerateBatchNumberAsync(PaymentBatchDirection direction)
    {
        // Two independent sequences so a received and a paid settlement in the same month
        // never share a number. Same count-then-append caveat as the other generators: a
        // collision becomes a failed save on the unique index, not a duplicate.
        var prefix = $"STL-{(direction == PaymentBatchDirection.Received ? "R" : "P")}-{DateTime.UtcNow:yyyyMM}";
        var count  = await _db.PaymentBatches.CountAsync(b => b.BatchNumber.StartsWith(prefix));
        return $"{prefix}-{count + 1:D4}";
    }
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
    public Task<bool> SkuExistsAsync(string sku, Guid? excludeId = null) =>
        _db.Products.AnyAsync(p => p.SKU.ToLower() == sku.ToLower()
                                && (excludeId == null || p.Id != excludeId));
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
        // PaymentTerm is included because the list and ageing screens show the term name —
        // since the TOP* enum members were retired, the term table is the only place that
        // information exists.
        var q = _db.Sales.Include(s => s.Customer).Include(s => s.SalesPerson)
                         .Include(s => s.PaymentTerm).AsQueryable();
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

    /// <summary>
    /// Active sales still owing money, oldest due first — the AR ageing list. Mirrors
    /// GetDuePurchasesAsync: anything not Cash is credit, so a single negated comparison
    /// replaces what used to be an OR-chain over the retired TOP* enum members.
    /// </summary>
    public Task<List<Sale>> GetByIdsWithItemsAsync(IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        return _db.Sales
           .Include(s => s.Customer)
           .Include(s => s.Branch)
           .Include(s => s.PaymentTerm)
           .Include(s => s.SalesPerson)
           .Include(s => s.SaleItems).ThenInclude(i => i.Product)
           .Include(s => s.PaymentRecords)
           .Where(s => list.Contains(s.Id))
           .AsSplitQuery()
           .ToListAsync();
    }

    public Task<List<Sale>> GetDueSalesAsync(Guid? customerId = null)
    {
        var q = _db.Sales
           .Include(s => s.Customer)
           .Include(s => s.PaymentTerm)
           .Where(s => s.Status == SaleStatus.Active
                    && s.PaymentType != PaymentType.Cash
                    && s.AmountPaid < s.GrandTotal);
        if (customerId.HasValue) q = q.Where(s => s.CustomerId == customerId.Value);
        return q.OrderBy(s => s.DueDate)   // overdue first, then by due date
                .ThenBy(s => s.SaleDate)
                .ToListAsync();
    }

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
