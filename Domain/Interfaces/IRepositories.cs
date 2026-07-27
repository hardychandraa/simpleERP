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

public interface IExpenseCategoryRepository {
    Task<List<ExpenseCategory>> GetAllAsync(bool activeOnly = false);
    Task<ExpenseCategory?> GetByIdAsync(Guid id);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(ExpenseCategory category);
    void Update(ExpenseCategory category);
    void Remove(ExpenseCategory category);
}

public interface IExpenseRepository {
    Task<List<Expense>> GetAllAsync(DateTime? from = null, DateTime? to = null, Guid? categoryId = null);
    Task<Expense?> GetByIdAsync(Guid id);
    Task AddAsync(Expense expense);
    void Update(Expense expense);
    void Remove(Expense expense);
    /// <summary>
    /// Per-category totals for [from, to), aggregated in SQL. Backs the Biaya
    /// Usaha section of the P&amp;L without materialising every expense row.
    /// </summary>
    Task<List<ExpenseCategoryTotal>> GetCategoryTotalsAsync(DateTime from, DateTime to);
}

/// <summary>One Biaya Usaha line on the P&amp;L.</summary>
public record ExpenseCategoryTotal(
    Guid    CategoryId,
    string  CategoryName,
    bool    IsTaxDeductible,
    int     EntryCount,
    decimal Amount);

public interface IPaymentTermRepository {
    Task<List<PaymentTerm>> GetAllAsync(bool activeOnly = false);
    Task<PaymentTerm?> GetByIdAsync(Guid id);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    /// <summary>True if any posted sale references this term — blocks deletion.</summary>
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(PaymentTerm term);
    void Update(PaymentTerm term);
    void Remove(PaymentTerm term);
}

public interface ISupplierRepository {
    Task<Supplier?> GetByIdAsync(Guid id);
    Task<List<Supplier>> GetAllAsync(bool activeOnly = false);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    /// <summary>True if any posted purchase references this supplier — blocks deletion.</summary>
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(Supplier supplier);
    void Update(Supplier supplier);
    void Remove(Supplier supplier);
}

public interface IPurchaseRepository {
    Task<Purchase?> GetByIdWithItemsAsync(Guid id);
    Task<List<Purchase>> GetAllAsync(DateTime? from = null, DateTime? to = null);
    /// <summary>Active purchases still owing money, oldest due first — the AP ageing list.</summary>
    Task<List<Purchase>> GetDuePurchasesAsync();
    Task<string> GeneratePurchaseNumberAsync();
    /// <summary>
    /// True if this supplier already has a purchase carrying the same document number.
    /// Scoped per supplier deliberately: two suppliers reusing a number is normal, the
    /// same supplier billing the same number twice is a duplicate entry.
    /// </summary>
    Task<bool> SupplierDocumentExistsAsync(Guid supplierId, string documentNumber, Guid? excludeId = null);
    Task AddAsync(Purchase purchase);
    void Update(Purchase purchase);
    /// <summary>
    /// Aggregated purchase figures for [from, to), active purchases only. Set-based —
    /// a period can span a full year, which GetAllAsync would materialise.
    /// </summary>
    Task<PurchasePeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
    /// <summary>
    /// Total active-purchase quantity of a product from a supplier within [from, to].
    /// Backs the Volume rebate threshold — the "have we bought enough this period?"
    /// check. Excludes the purchase currently being posted (not yet saved), so the
    /// caller adds the current line's qty itself.
    /// </summary>
    Task<decimal> GetPurchasedQtyAsync(Guid supplierId, Guid productId, DateTime? from, DateTime? to);
}

/// <summary>
/// Period totals for the purchase side. <paramref name="NetPurchases"/> is ex-PPN
/// (the DPP): input VAT is reclaimable, not a cost, so it never belongs in a
/// purchase or COGS figure. <paramref name="TaxPaid"/> carries it separately for the
/// monthly PPN summary (output tax − input tax).
/// </summary>
public record PurchasePeriodTotals(
    int     PurchaseCount,
    decimal NetPurchases,
    decimal TaxPaid,
    decimal GrossPurchases);

public interface ISupplierPaymentRepository {
    Task AddAsync(SupplierPayment payment);
    Task<List<SupplierPayment>> GetByPurchaseAsync(Guid purchaseId);
    Task<List<SupplierPayment>> GetByDateRangeAsync(DateTime from, DateTime to);
}

public interface IRebateRuleRepository {
    Task<RebateRule?> GetByIdAsync(Guid id);
    Task<List<RebateRule>> GetAllAsync(bool activeOnly = false);
    /// <summary>Active rules for a supplier, both supplier-wide and product-scoped — the evaluation set.</summary>
    Task<List<RebateRule>> GetActiveForSupplierAsync(Guid supplierId);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    /// <summary>True if any accrual references this rule — blocks deletion.</summary>
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(RebateRule rule);
    void Update(RebateRule rule);
    void Remove(RebateRule rule);
}

public interface IRebateAccrualRepository {
    Task AddAsync(RebateAccrual accrual);
    void Update(RebateAccrual accrual);
    Task<RebateAccrual?> GetByIdAsync(Guid id);
    /// <summary>Accruals triggered by a purchase — used to void them when it's cancelled and to show them on its detail.</summary>
    Task<List<RebateAccrual>> GetByPurchaseAsync(Guid purchaseId);
    /// <summary>Outstanding (unsettled, not voided) accruals for a supplier — the claim worklist.</summary>
    Task<List<RebateAccrual>> GetOutstandingBySupplierAsync(Guid supplierId);
    /// <summary>All accruals in a window, optionally filtered by supplier/settled-state, for the list UI.</summary>
    Task<List<RebateAccrual>> GetAllAsync(Guid? supplierId = null, bool? outstandingOnly = null);
    /// <summary>Suppliers that currently have any outstanding accrual, with counts — the claim landing page.</summary>
    Task<List<RebateOutstandingBySupplier>> GetOutstandingSummaryAsync();
}

/// <summary>One supplier's outstanding-rebate rollup.</summary>
public record RebateOutstandingBySupplier(
    Guid    SupplierId,
    string  SupplierName,
    int     CashAccrualCount,
    decimal CashAmount,
    int     InKindAccrualCount,
    int     LuckyDrawCount);

public interface IRebateRealizationRepository {
    Task AddAsync(RebateRealization realization);
    Task<RebateRealization?> GetByIdAsync(Guid id);
    Task<List<RebateRealization>> GetAllAsync(Guid? supplierId = null, DateTime? from = null, DateTime? to = null);
}

public interface ICommissionRuleRepository {
    Task<CommissionRule?> GetByIdAsync(Guid id);
    Task<List<CommissionRule>> GetAllAsync(bool activeOnly = false);
    /// <summary>Active rules that could apply to a salesperson (their own + the all-salesperson ones).</summary>
    Task<List<CommissionRule>> GetActiveForSalesPersonAsync(Guid salesPersonId);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(CommissionRule rule);
    void Update(CommissionRule rule);
    void Remove(CommissionRule rule);
}

public interface ICommissionAccrualRepository {
    Task AddAsync(CommissionAccrual accrual);
    void Update(CommissionAccrual accrual);
    /// <summary>Accruals a sale generated — voided when the sale is cancelled, shown on its detail.</summary>
    Task<List<CommissionAccrual>> GetBySaleAsync(Guid saleId);
    /// <summary>Unpaid, not-voided accruals for a salesperson — the payout worklist.</summary>
    Task<List<CommissionAccrual>> GetUnpaidBySalesPersonAsync(Guid salesPersonId);
    Task<List<CommissionAccrual>> GetAllAsync(Guid? salesPersonId = null, bool? unpaidOnly = null);
    /// <summary>Salespeople with any unpaid accrual, with counts and totals — the payout landing page.</summary>
    Task<List<CommissionUnpaidBySalesPerson>> GetUnpaidSummaryAsync();
}

/// <summary>One salesperson's unpaid-commission rollup.</summary>
public record CommissionUnpaidBySalesPerson(
    Guid    SalesPersonId,
    string  SalesPersonName,
    int     AccrualCount,
    decimal Amount);

public interface ICommissionPayoutRepository {
    Task AddAsync(CommissionPayout payout);
    Task<CommissionPayout?> GetByIdAsync(Guid id);
    Task<List<CommissionPayout>> GetAllAsync(Guid? salesPersonId = null);
}

public interface ISalesPersonRepository {
    Task<List<SalesPerson>> GetAllAsync(bool activeOnly = false);
    Task<SalesPerson?> GetByIdAsync(Guid id);
    Task<bool> NameExistsAsync(string name, Guid? excludeId = null);
    /// <summary>True if any posted sale is credited to this person — blocks deletion.</summary>
    Task<bool> IsInUseAsync(Guid id);
    Task AddAsync(SalesPerson person);
    void Update(SalesPerson person);
    void Remove(SalesPerson person);
}

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
