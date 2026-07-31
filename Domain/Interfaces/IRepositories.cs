using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Interfaces;

public interface IProductRepository {
    Task<Product?> GetByIdAsync(Guid id);
    Task<List<Product>> GetAllActiveAsync();
    Task<List<Product>> GetAllAsync();
    /// <summary>SKU is uniquely indexed in the database; check here first so a duplicate
    /// returns a readable message instead of surfacing as a constraint violation.</summary>
    Task<bool> SkuExistsAsync(string sku, Guid? excludeId = null);
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
    /// <summary>
    /// Several sales in one round trip, same includes as GetByIdWithItemsAsync. Backs
    /// multi-invoice settlement, where loading N invoices one at a time would be N queries
    /// against a list the user already has in front of them.
    /// </summary>
    Task<List<Sale>> GetByIdsWithItemsAsync(IEnumerable<Guid> ids);
    Task<List<Sale>> GetAllAsync(DateTime? from = null, DateTime? to = null);
    /// <summary>
    /// Active credit sales still owing money, oldest due first — the AR ageing list.
    /// Optionally scoped to one customer, which is what a statement of account needs.
    /// </summary>
    Task<List<Sale>> GetDueSalesAsync(Guid? customerId = null);
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
    /// <summary>Several purchases in one round trip — the AP mirror, backing supplier statements.</summary>
    Task<List<Purchase>> GetByIdsWithItemsAsync(IEnumerable<Guid> ids);
    Task<List<Purchase>> GetAllAsync(DateTime? from = null, DateTime? to = null);
    /// <summary>
    /// Active purchases still owing money, oldest due first — the AP ageing list.
    /// Optionally scoped to one supplier, which is what a statement of account needs.
    /// </summary>
    Task<List<Purchase>> GetDuePurchasesAsync(Guid? supplierId = null);
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

public interface ICustomerReturnRepository {
    Task<CustomerReturn?> GetByIdWithItemsAsync(Guid id);
    Task<List<CustomerReturn>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    /// <summary>Returns raised against one invoice — shown on the sale, and used to cap further returns.</summary>
    Task<List<CustomerReturn>> GetBySaleAsync(Guid saleId);
    Task<string> GenerateReturnNumberAsync();
    /// <summary>
    /// What has already been returned per SaleItem on this invoice, active returns only.
    /// The quantity is the cap — nobody can hand back more of a line than was sold on it —
    /// and the amount lets the return that closes a line out absorb the rounding residual.
    /// </summary>
    Task<Dictionary<Guid, ReturnedLineTally>> GetReturnedQtyBySaleItemAsync(Guid saleId);
    /// <summary>
    /// True if the invoice has an active return. Blocks cancelling the sale: cancel
    /// restocks every sold unit, so doing it after a return had already restocked some
    /// would put the same goods into stock twice.
    /// </summary>
    Task<bool> HasActiveReturnAsync(Guid saleId);
    Task AddAsync(CustomerReturn ret);
    void Update(CustomerReturn ret);
    /// <summary>
    /// Aggregated sales-return figures for [from, to), active returns only. Set-based —
    /// the P&amp;L needs Retur Penjualan and its COGS reversal without loading rows.
    /// </summary>
    Task<ReturnPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
}

public interface ISupplierReturnRepository {
    Task<SupplierReturn?> GetByIdWithItemsAsync(Guid id);
    Task<List<SupplierReturn>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<List<SupplierReturn>> GetByPurchaseAsync(Guid purchaseId);
    Task<string> GenerateReturnNumberAsync();
    /// <summary>What has already been returned per PurchaseItem, active returns only — the cap.</summary>
    Task<Dictionary<Guid, ReturnedLineTally>> GetReturnedQtyByPurchaseItemAsync(Guid purchaseId);
    /// <summary>
    /// True if the purchase has an active return. Blocks cancelling the purchase, which
    /// would try to reverse stock a return has already sent back.
    /// </summary>
    Task<bool> HasActiveReturnAsync(Guid purchaseId);
    Task AddAsync(SupplierReturn ret);
    void Update(SupplierReturn ret);
    Task<ReturnPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
}

/// <summary>
/// How much of one source line has already gone back. <paramref name="Amount"/> is the
/// credit/debit already raised against it, so the return that finally closes the line out
/// can be derived by subtraction instead of accumulating per-slice rounding error.
/// </summary>
public record ReturnedLineTally(decimal Qty, decimal Amount);

/// <summary>
/// Period totals for one side's returns. <paramref name="NetAmount"/> is ex-PPN, so it
/// nets straight against the matching revenue or purchases line;
/// <paramref name="TaxReversed"/> carries the PPN back out for the monthly tax summary,
/// and <paramref name="GrossAmount"/> is the tax-inclusive figure the credit/debit note
/// was actually issued for. <paramref name="StockValue"/> is what the goods were worth
/// as they moved — cost restocked on a sales return, inventory released on a purchase
/// return.
/// </summary>
public record ReturnPeriodTotals(
    int     ReturnCount,
    decimal NetAmount,
    decimal TaxReversed,
    decimal GrossAmount,
    decimal StockValue);

public interface ICreditNoteRepository {
    Task<CreditNote?> GetByIdAsync(Guid id);
    /// <summary>
    /// Notes filtered by any combination of direction, status, date window and counterparty.
    /// The counterparty filters back the statement of account: "what does this supplier
    /// already owe us back, that should come off what we're about to pay them."
    /// </summary>
    Task<List<CreditNote>> GetAllAsync(CreditDebitType? type = null, CreditNoteStatus? status = null,
                                       DateTime? from = null, DateTime? to = null,
                                       Guid? customerId = null, Guid? supplierId = null);
    /// <summary>Several notes in one round trip — the ones ticked for netting into a settlement.</summary>
    Task<List<CreditNote>> GetByIdsAsync(IEnumerable<Guid> ids);
    /// <summary>The note a return generated, so cancelling the return can cancel it too.</summary>
    Task<CreditNote?> GetByCustomerReturnAsync(Guid customerReturnId);
    Task<CreditNote?> GetBySupplierReturnAsync(Guid supplierReturnId);
    /// <summary>Numbered per direction — CN- for credit, DN- for debit — so the two run independently.</summary>
    Task<string> GenerateDocumentNumberAsync(CreditDebitType type);
    /// <summary>
    /// Face value of notes still Open, by direction. What AR (credit) and AP (debit)
    /// reporting has to net off, since posted invoices are never edited.
    /// </summary>
    Task<decimal> GetOpenTotalAsync(CreditDebitType type);
    Task AddAsync(CreditNote note);
    void Update(CreditNote note);
}

public interface IPaymentBatchRepository {
    Task AddAsync(PaymentBatch batch);
    Task<PaymentBatch?> GetByIdAsync(Guid id);
    Task<List<PaymentBatch>> GetAllAsync(PaymentBatchDirection? direction = null,
                                         Guid? customerId = null, Guid? supplierId = null,
                                         DateTime? from = null, DateTime? to = null);
    /// <summary>Numbered per direction, so received and paid settlements run independently.</summary>
    Task<string> GenerateBatchNumberAsync(PaymentBatchDirection direction);
    // Deliberately no Remove: a settlement is history, like RebateRealization and
    // CommissionPayout. Correcting one means reversing its payments, not deleting it.
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
