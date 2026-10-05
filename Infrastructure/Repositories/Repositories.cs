using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;
using SimpleERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace SimpleERP.Infrastructure.Repositories;

/// <summary>
/// The <c>yyyyMM</c> stamp every document number is prefixed with (INV-, PO-, CRN-, SRN-,
/// CN-/DN-, STL-R-/STL-P-).
///
/// Taken from the **local** business day, not UTC. These generators previously used
/// <c>DateTime.UtcNow</c>, which on a UTC+7 machine is still the previous day for the first
/// seven hours of every local day — and on the 1st of a month, the previous *month*. A sale
/// entered at 06:12 local on 1 August was therefore numbered <c>INV-202607-…</c> while the
/// invoice, the list and the printed receipt all showed it as 1 August: an August transaction
/// filed under July, which is exactly what breaks a month-end reconciliation against the tax
/// consultant's records.
///
/// A document number labels a business event, so it follows the business's calendar. This is
/// the same local-first reasoning the future-date guards use (see <c>SaleService.CreateAsync</c>);
/// stored timestamps stay UTC and are unaffected.
/// </summary>
internal static class DocumentNumber
{
    public static string MonthStamp() => DateTime.Now.ToString("yyyyMM");

    /// <summary>
    /// Reserves the next number under <paramref name="prefix"/> and returns it formatted
    /// (<c>INV-202610-0007</c>).
    ///
    /// This replaced count-then-append (<c>COUNT(*) + 1</c>), which had two failure modes:
    /// two people posting at the same moment both read the same count, and two documents
    /// raised inside one operation (a supplier settlement auto-raising a debit note per
    /// purchase) could both count only what was already saved. The unique indexes turned
    /// either into a failed save rather than a duplicate, but a failed save is still a
    /// staff member retyping an invoice.
    ///
    /// Now the counter row is bumped with <c>INSERT … ON CONFLICT DO UPDATE</c>, which takes
    /// a row lock. A transaction is opened here if the operation does not have one yet, and
    /// <see cref="UnitOfWork.SaveChangesAsync"/> commits it — so the lock is held until the
    /// document itself is saved. A second person posting the same document type waits a few
    /// milliseconds instead of colliding; a second document in the same operation sees the
    /// row its own transaction already bumped; and a save that fails rolls the counter back
    /// with it, so no number is ever skipped.
    ///
    /// Side effect, deliberately relied on: everything the operation writes after this point
    /// — including <see cref="AuditLogRepository.LogAsync"/>'s own early SaveChanges — now
    /// commits or rolls back as one unit.
    ///
    /// <paramref name="existingNumbers"/> seeds the counter from the highest number already
    /// issued, so the first use of a prefix (including the first run after this was
    /// introduced mid-month) continues the sequence instead of restarting it. GREATEST keeps
    /// that true even if a row were ever written behind the counter's back.
    /// </summary>
    public static async Task<string> NextAsync(AppDbContext db, string prefix, IQueryable<string> existingNumbers)
    {
        if (db.Database.CurrentTransaction == null)
            await db.Database.BeginTransactionAsync();

        var issued = await existingNumbers.Where(n => n.StartsWith(prefix + "-")).ToListAsync();
        var seed   = issued.Select(n => int.TryParse(n.AsSpan(prefix.Length + 1), out var k) ? k : 0)
                           .DefaultIfEmpty(0).Max();

        var next = (await db.Database.SqlQuery<int>($@"
            INSERT INTO ""DocumentSequences"" (""Prefix"", ""LastNumber"") VALUES ({prefix}, {seed + 1})
            ON CONFLICT (""Prefix"") DO UPDATE
                SET ""LastNumber"" = GREATEST(""DocumentSequences"".""LastNumber"", {seed}) + 1
            RETURNING ""LastNumber"" AS ""Value""").ToListAsync()).Single();

        return $"{prefix}-{next:D4}";
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    public UnitOfWork(AppDbContext db) => _db = db;
    // True while InTransactionAsync owns the transaction: saves inside it must not commit.
    private bool _outer;

    public async Task<int> SaveChangesAsync()
    {
        var written = await _db.SaveChangesAsync();
        // Opened by DocumentNumber.NextAsync when this operation reserved a number. An
        // operation that never reserved one has no transaction and saves exactly as before.
        if (!_outer && _db.Database.CurrentTransaction is { } tx)
        {
            await tx.CommitAsync();
            await tx.DisposeAsync();
        }
        return written;
    }

    public async Task<bool> InTransactionAsync(Func<Task<bool>> work)
    {
        if (_outer) return await work();   // already inside one — join it

        await using var tx = await _db.Database.BeginTransactionAsync();
        _outer = true;
        try
        {
            if (await work()) { await tx.CommitAsync(); return true; }
            await tx.RollbackAsync();
            // The rolled-back entities are still tracked with their in-memory edits
            // (e.g. a sale marked Cancelled); forget them so nothing later in this request
            // can save them by accident.
            _db.ChangeTracker.Clear();
            return false;
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
        finally { _outer = false; }
    }
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
                    // Net of applied debit notes — AP mirror of GetDueSalesAsync.
                    && p.GrandTotal - p.AmountPaid -
                       (_db.CreditNoteApplications
                            .Where(a => a.PurchaseId == p.Id && !a.IsReversed)
                            .Sum(a => (decimal?)a.Amount) ?? 0m) > 0);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
        return q.OrderBy(p => p.DueDate).ThenBy(p => p.PurchaseDate).ToListAsync();
    }

    public async Task<PayablesTotals> GetPayablesTotalAsync()
    {
        // AP mirror of SaleRepository.GetReceivablesTotalAsync — same shape, same cutoff,
        // same project-then-group structure.
        var today = DateTime.UtcNow.Date;
        var head = await _db.Purchases
            .Where(p => p.Status == PurchaseStatus.Active && p.PaymentType != PaymentType.Cash)
            .Select(p => new {
                p.DueDate,
                Gross   = p.GrandTotal - p.AmountPaid,
                Applied = _db.CreditNoteApplications
                             .Where(a => a.PurchaseId == p.Id && !a.IsReversed)
                             .Sum(a => (decimal?)a.Amount) ?? 0m
            })
            .Select(x => new { x.DueDate, x.Gross, x.Applied, Net = x.Gross - x.Applied })
            .Where(x => x.Net > 0)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count   = g.Count(),
                Gross   = g.Sum(x => (decimal?)x.Gross)   ?? 0m,
                Net     = g.Sum(x => (decimal?)x.Net)     ?? 0m,
                Applied = g.Sum(x => (decimal?)x.Applied) ?? 0m,
                Overdue = g.Where(x => x.DueDate != null && x.DueDate < today)
                           .Sum(x => (decimal?)x.Net) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new PayablesTotals(head?.Count ?? 0, head?.Gross ?? 0m, head?.Net ?? 0m,
                                  head?.Overdue ?? 0m, head?.Applied ?? 0m);
    }

    public async Task<PayablesAging> GetPayablesAgingAsync()
    {
        // AP mirror of SaleRepository.GetReceivablesAgingAsync — deliberately identical in
        // shape and thresholds, so the two sides can be reconciled against each other.
        var today = DateTime.UtcNow.Date;
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        var head = await _db.Purchases
            .Where(p => p.Status == PurchaseStatus.Active && p.PaymentType != PaymentType.Cash)
            .Select(p => new {
                p.DueDate,
                Net = p.GrandTotal - p.AmountPaid -
                      (_db.CreditNoteApplications
                           .Where(a => a.PurchaseId == p.Id && !a.IsReversed)
                           .Sum(a => (decimal?)a.Amount) ?? 0m)
            })
            .Where(x => x.Net > 0)
            .GroupBy(_ => 1)
            .Select(g => new {
                NoDueDate = g.Where(x => x.DueDate == null).Sum(x => (decimal?)x.Net) ?? 0m,
                NotYetDue = g.Where(x => x.DueDate != null && x.DueDate >= today).Sum(x => (decimal?)x.Net) ?? 0m,
                D1To30    = g.Where(x => x.DueDate != null && x.DueDate <  today && x.DueDate >= d30).Sum(x => (decimal?)x.Net) ?? 0m,
                D31To60   = g.Where(x => x.DueDate != null && x.DueDate <  d30   && x.DueDate >= d60).Sum(x => (decimal?)x.Net) ?? 0m,
                D61To90   = g.Where(x => x.DueDate != null && x.DueDate <  d60   && x.DueDate >= d90).Sum(x => (decimal?)x.Net) ?? 0m,
                D90Plus   = g.Where(x => x.DueDate != null && x.DueDate <  d90).Sum(x => (decimal?)x.Net) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new PayablesAging(head?.NoDueDate ?? 0m, head?.NotYetDue ?? 0m, head?.D1To30 ?? 0m,
                                 head?.D31To60 ?? 0m, head?.D61To90 ?? 0m, head?.D90Plus ?? 0m);
    }

    public async Task<string> GeneratePurchaseNumberAsync()
    {
        return await DocumentNumber.NextAsync(_db, $"PO-{DocumentNumber.MonthStamp()}",
            _db.Purchases.Select(p => p.PurchaseNumber));
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
        var purchased = await q.SumAsync(i => (decimal?)i.Qty) ?? 0m;

        // Returned units no longer count toward a Volume threshold (HC, 2026-07-31 — see
        // decisions.md). Windowed by the ORIGINAL purchase's date, not the return's own date,
        // so a purchase's contribution to a period is corrected in the same period it was
        // originally counted into, even if the return itself happens later.
        var returnedQ = _db.SupplierReturnItems
            .Where(i => i.ProductId == productId
                     && i.Return!.Status == ReturnStatus.Active
                     && i.Return.Purchase!.SupplierId == supplierId);
        if (from.HasValue) returnedQ = returnedQ.Where(i => i.Return!.Purchase!.PurchaseDate >= from.Value);
        if (to.HasValue)   returnedQ = returnedQ.Where(i => i.Return!.Purchase!.PurchaseDate <= to.Value);
        var returned = await returnedQ.SumAsync(i => (decimal?)i.Qty) ?? 0m;

        return purchased - returned;
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

    public Task<RebateAccrualPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to) =>
        RollUpAsync(_db.RebateAccruals
            .Where(a => a.AccrualDate >= from && a.AccrualDate < to && !a.IsVoided));

    public Task<RebateAccrualPeriodTotals> GetOutstandingTotalAsync() =>
        RollUpAsync(_db.RebateAccruals
            .Where(a => a.RebateRealizationId == null && !a.IsVoided));

    /// <summary>
    /// Shared conditional-sum rollup for both the period (P&amp;L) and outstanding
    /// (position) views — identical shape, different predicate. In-kind and lucky-draw
    /// accruals are counted but excluded from the cash sum: both carry Amount = 0 by
    /// design, so including them would be harmless today and silently wrong the moment
    /// either ever gets a non-zero accrual.
    /// </summary>
    private static async Task<RebateAccrualPeriodTotals> RollUpAsync(IQueryable<RebateAccrual> q)
    {
        var head = await q
            .GroupBy(_ => 1)
            .Select(g => new {
                Count = g.Count(a => a.RewardType != RebateRewardType.InKindGoods
                                  && a.RewardType != RebateRewardType.LuckyDraw),
                Cash  = g.Where(a => a.RewardType != RebateRewardType.InKindGoods
                                  && a.RewardType != RebateRewardType.LuckyDraw)
                         .Sum(a => (decimal?)a.Amount) ?? 0m,
                InKind    = g.Count(a => a.RewardType == RebateRewardType.InKindGoods),
                LuckyDraw = g.Count(a => a.RewardType == RebateRewardType.LuckyDraw)
            })
            .FirstOrDefaultAsync();

        return new RebateAccrualPeriodTotals(
            AccrualCount:   head?.Count     ?? 0,
            CashAccrued:    head?.Cash      ?? 0m,
            InKindCount:    head?.InKind    ?? 0,
            LuckyDrawCount: head?.LuckyDraw ?? 0);
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

    public async Task<RebateRealizationPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to)
    {
        // Gross/Withholding/Net are nullable on the entity (a purely in-kind settlement
        // has no cash figures at all), so every sum coalesces twice: once for "no rows
        // matched" and once for "row matched but the column is null".
        var head = await _db.RebateRealizations
            .Where(r => r.RealizationDate >= from && r.RealizationDate < to)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count       = g.Count(),
                Gross       = g.Sum(r => (decimal?)(r.GrossAmount       ?? 0m)) ?? 0m,
                Withholding = g.Sum(r => (decimal?)(r.WithholdingAmount ?? 0m)) ?? 0m,
                Net         = g.Sum(r => (decimal?)(r.NetAmount         ?? 0m)) ?? 0m,
                LuckyDraw   = g.Where(r => r.RewardType == RebateRewardType.LuckyDraw)
                               .Sum(r => (decimal?)(r.GrossAmount ?? 0m)) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new RebateRealizationPeriodTotals(
            RealizationCount: head?.Count       ?? 0,
            Gross:            head?.Gross       ?? 0m,
            Withholding:      head?.Withholding ?? 0m,
            Net:              head?.Net         ?? 0m,
            LuckyDrawGross:   head?.LuckyDraw   ?? 0m);
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

    public Task<CommissionPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to) =>
        RollUpAsync(_db.CommissionAccruals
            .Where(a => a.AccrualDate >= from && a.AccrualDate < to && !a.IsVoided));

    public Task<CommissionPeriodTotals> GetUnpaidTotalAsync() =>
        RollUpAsync(_db.CommissionAccruals
            .Where(a => a.CommissionPayoutId == null && !a.IsVoided));

    private static async Task<CommissionPeriodTotals> RollUpAsync(IQueryable<CommissionAccrual> q)
    {
        var head = await q
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(a => (decimal?)a.Amount) ?? 0m })
            .FirstOrDefaultAsync();

        return new CommissionPeriodTotals(head?.Count ?? 0, head?.Amount ?? 0m);
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
        return await DocumentNumber.NextAsync(_db, $"CRN-{DocumentNumber.MonthStamp()}",
            _db.CustomerReturns.Select(r => r.ReturnNumber));
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
        return await DocumentNumber.NextAsync(_db, $"SRN-{DocumentNumber.MonthStamp()}",
            _db.SupplierReturns.Select(r => r.ReturnNumber));
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
        // month never share a number.
        var prefix = $"{(type == CreditDebitType.Credit ? "CN" : "DN")}-{DocumentNumber.MonthStamp()}";
        return await DocumentNumber.NextAsync(_db, prefix, _db.CreditNotes.Select(n => n.DocumentNumber));
    }

    public async Task<decimal> GetOpenTotalAsync(CreditDebitType type) =>
        // Remaining, not face value: an applied slice already reduces the balance of the
        // document it was applied to, so counting it here as well would double it.
        await _db.CreditNotes
            .Where(n => n.Type == type && n.Status == CreditNoteStatus.Open)
            .Select(n => n.Amount -
                (_db.CreditNoteApplications
                     .Where(a => a.CreditNoteId == n.Id && !a.IsReversed)
                     .Sum(a => (decimal?)a.Amount) ?? 0m))
            .SumAsync(remaining => (decimal?)remaining) ?? 0m;

    public async Task AddAsync(CreditNote note) => await _db.CreditNotes.AddAsync(note);
    public void Update(CreditNote note) => _db.CreditNotes.Update(note);
}

/// <summary>
/// Per-document note application. Every aggregate here filters out reversed rows —
/// a reversal releases the amount back to both the note and the document at once.
/// </summary>
public class CreditNoteApplicationRepository : ICreditNoteApplicationRepository
{
    private readonly AppDbContext _db;
    public CreditNoteApplicationRepository(AppDbContext db) => _db = db;

    private IQueryable<CreditNoteApplication> WithNav => _db.CreditNoteApplications
        .Include(a => a.CreditNote).Include(a => a.Sale).Include(a => a.Purchase);

    public Task<CreditNoteApplication?> GetByIdAsync(Guid id) =>
        WithNav.FirstOrDefaultAsync(a => a.Id == id);

    // History reads keep reversed rows: they are the audit trail of what was corrected.
    public Task<List<CreditNoteApplication>> GetByCreditNoteAsync(Guid creditNoteId) =>
        WithNav.Where(a => a.CreditNoteId == creditNoteId)
               .OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.CreatedAt).ToListAsync();

    public Task<List<CreditNoteApplication>> GetBySaleAsync(Guid saleId) =>
        WithNav.Where(a => a.SaleId == saleId)
               .OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.CreatedAt).ToListAsync();

    public Task<List<CreditNoteApplication>> GetByPurchaseAsync(Guid purchaseId) =>
        WithNav.Where(a => a.PurchaseId == purchaseId)
               .OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.CreatedAt).ToListAsync();

    public async Task AddAsync(CreditNoteApplication application) =>
        await _db.CreditNoteApplications.AddAsync(application);
    public void Update(CreditNoteApplication application) =>
        _db.CreditNoteApplications.Update(application);

    public async Task<decimal> GetAppliedTotalForSaleAsync(Guid saleId) =>
        await _db.CreditNoteApplications
            .Where(a => a.SaleId == saleId && !a.IsReversed)
            .SumAsync(a => (decimal?)a.Amount) ?? 0m;

    public async Task<decimal> GetAppliedTotalForPurchaseAsync(Guid purchaseId) =>
        await _db.CreditNoteApplications
            .Where(a => a.PurchaseId == purchaseId && !a.IsReversed)
            .SumAsync(a => (decimal?)a.Amount) ?? 0m;

    public async Task<Dictionary<Guid, decimal>> GetAppliedTotalsForSalesAsync(IEnumerable<Guid> saleIds)
    {
        var list = saleIds.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.CreditNoteApplications
            .Where(a => a.SaleId != null && list.Contains(a.SaleId.Value) && !a.IsReversed)
            .GroupBy(a => a.SaleId!.Value)
            .Select(g => new { SaleId = g.Key, Applied = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.SaleId, x => x.Applied);
    }

    public async Task<Dictionary<Guid, decimal>> GetAppliedTotalsForPurchasesAsync(IEnumerable<Guid> purchaseIds)
    {
        var list = purchaseIds.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.CreditNoteApplications
            .Where(a => a.PurchaseId != null && list.Contains(a.PurchaseId.Value) && !a.IsReversed)
            .GroupBy(a => a.PurchaseId!.Value)
            .Select(g => new { PurchaseId = g.Key, Applied = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.PurchaseId, x => x.Applied);
    }

    public async Task<decimal> GetAppliedTotalForNoteAsync(Guid creditNoteId) =>
        await _db.CreditNoteApplications
            .Where(a => a.CreditNoteId == creditNoteId && !a.IsReversed)
            .SumAsync(a => (decimal?)a.Amount) ?? 0m;

    public async Task<Dictionary<Guid, decimal>> GetAppliedTotalsForNotesAsync(IEnumerable<Guid> creditNoteIds)
    {
        var list = creditNoteIds.Distinct().ToList();
        if (list.Count == 0) return new();
        return await _db.CreditNoteApplications
            .Where(a => list.Contains(a.CreditNoteId) && !a.IsReversed)
            .GroupBy(a => a.CreditNoteId)
            .Select(g => new { NoteId = g.Key, Applied = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.NoteId, x => x.Applied);
    }

    public Task<bool> HasLiveApplicationsForNoteAsync(Guid creditNoteId) =>
        _db.CreditNoteApplications.AnyAsync(a => a.CreditNoteId == creditNoteId && !a.IsReversed);

    public Task<bool> HasLiveApplicationsForSaleAsync(Guid saleId) =>
        _db.CreditNoteApplications.AnyAsync(a => a.SaleId == saleId && !a.IsReversed);

    public Task<bool> HasLiveApplicationsForPurchaseAsync(Guid purchaseId) =>
        _db.CreditNoteApplications.AnyAsync(a => a.PurchaseId == purchaseId && !a.IsReversed);
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
        // never share a number.
        var prefix = $"STL-{(direction == PaymentBatchDirection.Received ? "R" : "P")}-{DocumentNumber.MonthStamp()}";
        return await DocumentNumber.NextAsync(_db, prefix, _db.PaymentBatches.Select(b => b.BatchNumber));
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

    // Codes are stored upper-case, so a plain comparison is already case-insensitive.
    public Task<bool> CodeExistsAsync(string code, Guid? excludeId = null) =>
        _db.SalesPersons.AnyAsync(p => p.Code == code && (excludeId == null || p.Id != excludeId));

    public Task<bool> IsInUseAsync(Guid id) =>
        _db.Sales.AnyAsync(s => s.SalesPersonId == id);

    public async Task AddAsync(SalesPerson person) => await _db.SalesPersons.AddAsync(person);
    public void Update(SalesPerson person) => _db.SalesPersons.Update(person);
    public void Remove(SalesPerson person) => _db.SalesPersons.Remove(person);
}

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public Task<List<User>> GetAllAsync(bool activeOnly = false)
    {
        var q = _db.Users.AsQueryable();
        if (activeOnly) q = q.Where(u => u.IsActive);
        return q.OrderBy(u => u.Username).ToListAsync();
    }

    public Task<bool> AnyAsync() => _db.Users.AnyAsync();

    public Task<User?> GetByIdAsync(Guid id) => _db.Users.FindAsync(id).AsTask();

    public Task<User?> GetByUsernameAsync(string username) =>
        _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

    public Task<bool> UsernameExistsAsync(string username, Guid? excludeId = null) =>
        _db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()
                             && (excludeId == null || u.Id != excludeId));

    public async Task<bool> IsLastActiveAdminAsync(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null || !user.IsActive || user.Role != UserRole.Admin) return false;
        return await _db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin) <= 1;
    }

    public async Task AddAsync(User user) => await _db.Users.AddAsync(user);
    public void Update(User user) => _db.Users.Update(user);
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

    public async Task<InventoryValuation> GetValuationAsync(Guid branchId)
    {
        // One grouped query with a correlated sub-select for the latest stock-in cost,
        // rather than GetCurrentStockAsync + GetCurrentAvgCostAsync per product (which
        // is two round trips each). The projection returns one row per product that has
        // ever moved — bounded by the catalogue, not by ledger volume — and the final
        // sum is done here so products sitting at zero stock can be excluded from the
        // count without a second query.
        var rows = await _db.InventoryLedgers
            .Where(l => l.BranchId == branchId)
            .GroupBy(l => l.ProductId)
            .Select(g => new {
                Qty  = g.Sum(l => l.QtyIn) - g.Sum(l => l.QtyOut),
                Cost = _db.InventoryLedgers
                          .Where(x => x.ProductId == g.Key && x.BranchId == branchId && x.QtyIn > 0)
                          .OrderByDescending(x => x.TransactionDate)
                          .Select(x => (decimal?)x.UnitCost)
                          .FirstOrDefault()
            })
            .ToListAsync();

        // Negative stock shouldn't happen (StockOutAsync guards it) but if it ever did,
        // letting it subtract value here would quietly understate the position rather
        // than showing the real quantity problem — so only positive stock is valued.
        var held = rows.Where(r => r.Qty > 0).ToList();

        return new InventoryValuation(
            ProductsInStock: held.Count,
            TotalValue:      held.Sum(r => r.Qty * (r.Cost ?? 0m)));
    }
}

public class SaleRepository : ISaleRepository
{
    private readonly AppDbContext _db;
    public SaleRepository(AppDbContext db) => _db = db;

    public Task<string?> GetInvoiceNumberAsync(Guid id) =>
        _db.Sales.Where(s => s.Id == id).Select(s => (string?)s.InvoiceNumber).FirstOrDefaultAsync();

    public async Task<(Guid Id, string InvoiceNumber)?> FindReplacementAsync(Guid saleId)
    {
        var r = await _db.Sales.Where(s => s.ReplacesSaleId == saleId)
                               .Select(s => new { s.Id, s.InvoiceNumber }).FirstOrDefaultAsync();
        return r == null ? null : (r.Id, r.InvoiceNumber);
    }

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
                    // Net of applied credit notes, not just of cash. A note never touches
                    // AmountPaid, so an invoice fully covered by one would otherwise sit
                    // in the due list forever with nothing left to collect.
                    && s.GrandTotal - s.AmountPaid -
                       (_db.CreditNoteApplications
                            .Where(a => a.SaleId == s.Id && !a.IsReversed)
                            .Sum(a => (decimal?)a.Amount) ?? 0m) > 0);
        if (customerId.HasValue) q = q.Where(s => s.CustomerId == customerId.Value);
        return q.OrderBy(s => s.DueDate)   // overdue first, then by due date
                .ThenBy(s => s.SaleDate)
                .ToListAsync();
    }

    public async Task<ReceivablesTotals> GetReceivablesTotalAsync()
    {
        // Same population as GetDueSalesAsync, summed in SQL. Cutoff is UTC "today" to
        // match how DueDate is stored and how the Due screens already compare it.
        //
        // Projected per invoice first, then grouped: nesting the correlated notes subquery
        // directly inside a GroupBy's Sum is a translation risk, and this shape also keeps
        // the gross and net figures derivable from the same pass.
        var today = DateTime.UtcNow.Date;
        var head = await _db.Sales
            .Where(s => s.Status == SaleStatus.Active && s.PaymentType != PaymentType.Cash)
            .Select(s => new {
                s.DueDate,
                Gross   = s.GrandTotal - s.AmountPaid,
                Applied = _db.CreditNoteApplications
                             .Where(a => a.SaleId == s.Id && !a.IsReversed)
                             .Sum(a => (decimal?)a.Amount) ?? 0m
            })
            .Select(x => new { x.DueDate, x.Gross, x.Applied, Net = x.Gross - x.Applied })
            // Only invoices with something genuinely left to collect. Because each Net is
            // capped at write time so it can never go below zero, a sum of these can never
            // be negative — which is what makes a negative Piutang Bersih impossible.
            .Where(x => x.Net > 0)
            .GroupBy(_ => 1)
            .Select(g => new {
                Count    = g.Count(),
                Gross    = g.Sum(x => (decimal?)x.Gross)   ?? 0m,
                Net      = g.Sum(x => (decimal?)x.Net)     ?? 0m,
                Applied  = g.Sum(x => (decimal?)x.Applied) ?? 0m,
                Overdue  = g.Where(x => x.DueDate != null && x.DueDate < today)
                            .Sum(x => (decimal?)x.Net) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new ReceivablesTotals(head?.Count ?? 0, head?.Gross ?? 0m, head?.Net ?? 0m,
                                     head?.Overdue ?? 0m, head?.Applied ?? 0m);
    }

    public async Task<ReceivablesAging> GetReceivablesAgingAsync()
    {
        // Boundary dates, not a computed day count: comparing DueDate against fixed
        // thresholds stays one set-based query and matches how every other overdue check
        // in this app already works. Buckets are closed at the older end and open at the
        // newer, so each invoice lands in exactly one and the six sum to NetTotal.
        var today = DateTime.UtcNow.Date;
        var d30 = today.AddDays(-30);
        var d60 = today.AddDays(-60);
        var d90 = today.AddDays(-90);

        var head = await _db.Sales
            .Where(s => s.Status == SaleStatus.Active && s.PaymentType != PaymentType.Cash)
            .Select(s => new {
                s.DueDate,
                Net = s.GrandTotal - s.AmountPaid -
                      (_db.CreditNoteApplications
                           .Where(a => a.SaleId == s.Id && !a.IsReversed)
                           .Sum(a => (decimal?)a.Amount) ?? 0m)
            })
            .Where(x => x.Net > 0)
            .GroupBy(_ => 1)
            .Select(g => new {
                // Open credit with no agreed date — deliberately not aged, since there is
                // no deadline to be late against.
                NoDueDate  = g.Where(x => x.DueDate == null).Sum(x => (decimal?)x.Net) ?? 0m,
                NotYetDue  = g.Where(x => x.DueDate != null && x.DueDate >= today).Sum(x => (decimal?)x.Net) ?? 0m,
                D1To30     = g.Where(x => x.DueDate != null && x.DueDate <  today && x.DueDate >= d30).Sum(x => (decimal?)x.Net) ?? 0m,
                D31To60    = g.Where(x => x.DueDate != null && x.DueDate <  d30   && x.DueDate >= d60).Sum(x => (decimal?)x.Net) ?? 0m,
                D61To90    = g.Where(x => x.DueDate != null && x.DueDate <  d60   && x.DueDate >= d90).Sum(x => (decimal?)x.Net) ?? 0m,
                D90Plus    = g.Where(x => x.DueDate != null && x.DueDate <  d90).Sum(x => (decimal?)x.Net) ?? 0m
            })
            .FirstOrDefaultAsync();

        return new ReceivablesAging(head?.NoDueDate ?? 0m, head?.NotYetDue ?? 0m, head?.D1To30 ?? 0m,
                                    head?.D31To60 ?? 0m, head?.D61To90 ?? 0m, head?.D90Plus ?? 0m);
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        return await DocumentNumber.NextAsync(_db, $"INV-{DocumentNumber.MonthStamp()}",
            _db.Sales.Select(s => s.InvoiceNumber));
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
        // Writes immediately. This is the request's shared DbContext, so it also flushes
        // whatever the calling operation has staged so far — it is NOT independent of the
        // unit of work. Once an operation has reserved a document number, that flush
        // happens inside DocumentNumber.NextAsync's transaction and commits with it.
        await _db.SaveChangesAsync();
    }

    public Task<List<AuditLog>> GetRecentAsync(int count = 100) =>
        _db.AuditLogs.OrderByDescending(l => l.Id).Take(count).ToListAsync();
}

public class AppLogRepository : IAppLogRepository
{
    private readonly AppDbContext _db;
    public AppLogRepository(AppDbContext db) => _db = db;

    /// <summary>
    /// Severity order, so "Warning and above" can be expressed as a set rather than a
    /// string comparison — "Error" &gt; "Warning" is true alphabetically but that is a
    /// coincidence, and "Fatal" &lt; "Warning" shows why relying on it would break.
    /// </summary>
    private static readonly Dictionary<string, string[]> AtOrAbove = new(StringComparer.OrdinalIgnoreCase) {
        ["Warning"] = new[] { "Warning", "Error", "Fatal" },
        ["Error"]   = new[] { "Error", "Fatal" },
        ["Fatal"]   = new[] { "Fatal" },
    };

    public Task<List<AppLog>> GetAsync(string? minLevel = null, DateTime? from = null, DateTime? to = null,
                                       string? search = null, int count = 200)
    {
        var q = _db.AppLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(minLevel) && AtOrAbove.TryGetValue(minLevel, out var levels))
            q = q.Where(l => levels.Contains(l.Level));

        if (from.HasValue) q = q.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue)   q = q.Where(l => l.Timestamp <  to.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE via EF.Functions so the match happens in Postgres, not after
            // materialising rows — this table is the one that grows fastest in the app.
            var pattern = $"%{search.Trim()}%";
            q = q.Where(l => EF.Functions.ILike(l.Message, pattern)
                          || (l.Exception != null && EF.Functions.ILike(l.Exception, pattern))
                          || (l.Source    != null && EF.Functions.ILike(l.Source,    pattern))
                          || (l.CorrelationId != null && EF.Functions.ILike(l.CorrelationId, pattern)));
        }

        return q.OrderByDescending(l => l.Id).Take(count).ToListAsync();
    }

    public async Task<List<AppLogLevelCount>> GetLevelCountsAsync(DateTime? from = null, DateTime? to = null)
    {
        var q = _db.AppLogs.AsNoTracking();
        if (from.HasValue) q = q.Where(l => l.Timestamp >= from.Value);
        if (to.HasValue)   q = q.Where(l => l.Timestamp <  to.Value);

        var rows = await q.GroupBy(l => l.Level)
                          .Select(g => new { Level = g.Key, Count = g.Count() })
                          .ToListAsync();
        return rows.Select(r => new AppLogLevelCount(r.Level, r.Count)).ToList();
    }

    public Task<int> PurgeOlderThanAsync(DateTime cutoffUtc) =>
        _db.AppLogs.Where(l => l.Timestamp < cutoffUtc).ExecuteDeleteAsync();
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
