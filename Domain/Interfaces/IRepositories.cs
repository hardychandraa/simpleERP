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
    /// <summary>
    /// What the stock on hand is currently worth, for the Position Summary.
    /// One grouped query rather than the per-product loop in
    /// <c>InventoryService.GetAllStockLevelsAsync</c> — a position report must not
    /// issue two round trips per product.
    /// </summary>
    Task<InventoryValuation> GetValuationAsync(Guid branchId);
}

/// <summary>
/// Stock-on-hand valuation at a point in time. <paramref name="TotalValue"/> uses the
/// same basis as the rest of the app — quantity on hand × the unit cost of the most
/// recent stock-in — so the Position Summary agrees with the Inventory screen.
/// Products whose stock has gone to zero contribute nothing and aren't counted.
/// </summary>
public record InventoryValuation(int ProductsInStock, decimal TotalValue);

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
    /// Active credit sales still owing money <em>after</em> any credit notes applied to
    /// them, oldest due first — the AR ageing list. Optionally scoped to one customer,
    /// which is what a statement of account needs.
    ///
    /// An invoice fully covered by an applied credit note drops out here even though its
    /// own AmountPaid never moved: a note is not a payment, so it never touches
    /// AmountPaid, but it does settle the balance.
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
    /// <summary>
    /// Outstanding receivables as of now — the same population as
    /// <see cref="GetDueSalesAsync"/>, aggregated in SQL instead of materialised.
    /// Tax-inclusive, because a receivable is cash owed, not turnover.
    /// </summary>
    Task<ReceivablesTotals> GetReceivablesTotalAsync();
    /// <summary>
    /// The same net-of-notes population as <see cref="GetReceivablesTotalAsync"/>, split
    /// by age instead of summed flat. Bucketed on boundary dates rather than a computed
    /// day count, so it stays a single set-based query.
    /// </summary>
    Task<ReceivablesAging> GetReceivablesAgingAsync();
}

/// <summary>
/// AR position, all figures tax-inclusive.
///
/// <paramref name="GrossTotal"/> is what the invoices themselves still say is owed
/// (GrandTotal − AmountPaid). <paramref name="NetTotal"/> takes off credit notes
/// applied to those specific invoices, and is the figure that actually represents cash
/// collectable — it is what the Position Summary and the Due screens report.
/// Per-document netting is capped so no single invoice can go below zero, which is why
/// <paramref name="NetTotal"/> can never be negative.
///
/// <paramref name="Overdue"/> is the net slice already past its due date — a coarse
/// flag kept for continuity; <see cref="ReceivablesAging"/> is the real bucketing.
/// </summary>
public record ReceivablesTotals(int OpenInvoices, decimal GrossTotal, decimal NetTotal,
                                decimal Overdue, decimal AppliedNotesTotal);

/// <summary>
/// AR ageing by how long each net-positive invoice has been past due.
///
/// <paramref name="NoDueDate"/> is a real population, not a rounding edge: a credit sale
/// with no payment term chosen is open credit with no agreed date, which the app allows
/// deliberately rather than inventing a deadline for. It is reported on its own rather
/// than folded into any aged bucket.
/// </summary>
public record ReceivablesAging(decimal NoDueDate, decimal NotYetDue, decimal Days1To30,
                               decimal Days31To60, decimal Days61To90, decimal Days90Plus)
{
    public decimal Total => NoDueDate + NotYetDue + Days1To30 + Days31To60 + Days61To90 + Days90Plus;
    /// <summary>Everything actually past due — reconciles with <see cref="ReceivablesTotals.Overdue"/>.</summary>
    public decimal Overdue => Days1To30 + Days31To60 + Days61To90 + Days90Plus;
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
    /// Active purchases still owing money <em>after</em> any debit notes applied to them,
    /// oldest due first — the AP ageing list. Optionally scoped to one supplier, which is
    /// what a statement of account needs. AP mirror of <see cref="ISaleRepository.GetDueSalesAsync"/>.
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
    /// <summary>
    /// Outstanding payables as of now — the AP mirror of
    /// <see cref="ISaleRepository.GetReceivablesTotalAsync"/>, aggregated in SQL.
    /// </summary>
    Task<PayablesTotals> GetPayablesTotalAsync();
    /// <summary>AP mirror of <see cref="ISaleRepository.GetReceivablesAgingAsync"/>.</summary>
    Task<PayablesAging> GetPayablesAgingAsync();
}

/// <summary>
/// AP position, tax-inclusive. Mirrors <see cref="ReceivablesTotals"/> exactly:
/// <paramref name="NetTotal"/> is gross less debit notes applied to those specific
/// purchases, and is what we actually still have to pay.
/// </summary>
public record PayablesTotals(int OpenPurchases, decimal GrossTotal, decimal NetTotal,
                             decimal Overdue, decimal AppliedNotesTotal);

/// <summary>AP ageing. Mirrors <see cref="ReceivablesAging"/>.</summary>
public record PayablesAging(decimal NoDueDate, decimal NotYetDue, decimal Days1To30,
                            decimal Days31To60, decimal Days61To90, decimal Days90Plus)
{
    public decimal Total => NoDueDate + NotYetDue + Days1To30 + Days31To60 + Days61To90 + Days90Plus;
    public decimal Overdue => Days1To30 + Days31To60 + Days61To90 + Days90Plus;
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
    /// <summary>
    /// Rebate earned in [from, to), voided accruals excluded — the P&amp;L's
    /// Pendapatan Rebat line. Set-based, since a P&amp;L can span a year.
    /// <c>InKindCount</c>/<c>LuckyDrawCount</c> are carried because those two reward
    /// types accrue <c>Amount = 0</c> by design (unvaluable until settled), so a
    /// zero cash figure alongside a non-zero count is meaningful, not a bug.
    /// </summary>
    Task<RebateAccrualPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
    /// <summary>Unclaimed rebate as of now, across all suppliers — the Position Summary asset line.</summary>
    Task<RebateAccrualPeriodTotals> GetOutstandingTotalAsync();
}

/// <summary>
/// Rebate accrual rollup. <paramref name="CashAccrued"/> is gross of withholding:
/// the 15% deduction happens at settlement and is a prepaid tax credit, not a cost
/// of earning the rebate, so income is recognised at the gross figure.
/// </summary>
public record RebateAccrualPeriodTotals(
    int     AccrualCount,
    decimal CashAccrued,
    int     InKindCount,
    int     LuckyDrawCount);

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
    /// <summary>
    /// Rebate actually settled in [from, to). Mostly a reconciliation memo — the P&amp;L
    /// recognises rebate when it accrues — except for <c>LuckyDraw</c>, which accrues
    /// zero because its value is unknowable until drawn, so it can only be recognised
    /// here. Set-based.
    /// </summary>
    Task<RebateRealizationPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
}

/// <summary>
/// Rebate settlement rollup. <paramref name="LuckyDrawGross"/> is separated out
/// because it is the one slice the accrual side cannot see, so it is the only part
/// of this record the P&amp;L adds to income — adding the rest would double-count
/// rebate already recognised when it accrued.
/// </summary>
public record RebateRealizationPeriodTotals(
    int     RealizationCount,
    decimal Gross,
    decimal Withholding,
    decimal Net,
    decimal LuckyDrawGross);

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
    /// <summary>
    /// Commission earned in [from, to), voided accruals excluded — the P&amp;L's Komisi
    /// Penjualan line. Note this is an <em>earned-on-collection</em> figure: an accrual's
    /// date is when the money came in, not when the invoice was raised, so commission
    /// in a period will not tie to that period's Penjualan. That is intended.
    /// </summary>
    Task<CommissionPeriodTotals> GetPeriodTotalsAsync(DateTime from, DateTime to);
    /// <summary>Unpaid commission as of now — the Position Summary liability line.</summary>
    Task<CommissionPeriodTotals> GetUnpaidTotalAsync();
}

/// <summary>
/// Commission rollup. Gross — commission carries no withholding split today
/// (unlike rebate); whether PPh 21 applies is an open question for the consultant.
/// </summary>
public record CommissionPeriodTotals(int AccrualCount, decimal Amount);

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
    /// Remaining value of notes still Open, by direction — face value less whatever has
    /// already been applied to a specific document.
    ///
    /// This used to be the figure AR/AP netted off in aggregate. It no longer is: an
    /// applied note reduces the balance of the document it was applied to, so netting it
    /// off the ledger total as well would double-count it. What's left here is the slice
    /// not yet tied to any document, which the Position Summary shows as information
    /// rather than subtracting.
    /// </summary>
    Task<decimal> GetOpenTotalAsync(CreditDebitType type);
    Task AddAsync(CreditNote note);
    void Update(CreditNote note);
}

/// <summary>
/// Credit/debit notes applied against one named invoice or purchase.
///
/// Every aggregate here excludes reversed rows. The bulk (plural) overloads exist
/// because the Due lists, the statements and the Position Summary all need applied
/// totals for many documents at once — one round trip, not one per document.
/// </summary>
public interface ICreditNoteApplicationRepository {
    Task<CreditNoteApplication?> GetByIdAsync(Guid id);
    /// <summary>A note's own application history, reversed rows included — this is the audit trail.</summary>
    Task<List<CreditNoteApplication>> GetByCreditNoteAsync(Guid creditNoteId);
    /// <summary>Notes applied against one invoice, reversed rows included, for its detail page.</summary>
    Task<List<CreditNoteApplication>> GetBySaleAsync(Guid saleId);
    Task<List<CreditNoteApplication>> GetByPurchaseAsync(Guid purchaseId);
    Task AddAsync(CreditNoteApplication application);
    void Update(CreditNoteApplication application);

    /// <summary>Live (non-reversed) note value applied against one invoice.</summary>
    Task<decimal> GetAppliedTotalForSaleAsync(Guid saleId);
    Task<decimal> GetAppliedTotalForPurchaseAsync(Guid purchaseId);
    /// <summary>Applied totals for many invoices in one query. Documents with none are absent from the map.</summary>
    Task<Dictionary<Guid, decimal>> GetAppliedTotalsForSalesAsync(IEnumerable<Guid> saleIds);
    Task<Dictionary<Guid, decimal>> GetAppliedTotalsForPurchasesAsync(IEnumerable<Guid> purchaseIds);
    /// <summary>How much of one note has been applied — face value less this is what's left.</summary>
    Task<decimal> GetAppliedTotalForNoteAsync(Guid creditNoteId);
    Task<Dictionary<Guid, decimal>> GetAppliedTotalsForNotesAsync(IEnumerable<Guid> creditNoteIds);
    /// <summary>Cheap existence check — blocks cancelling a note or a document out from under a live application.</summary>
    Task<bool> HasLiveApplicationsForNoteAsync(Guid creditNoteId);
    Task<bool> HasLiveApplicationsForSaleAsync(Guid saleId);
    Task<bool> HasLiveApplicationsForPurchaseAsync(Guid purchaseId);
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

/// <summary>
/// Reads the diagnostic log. Deliberately read-only: entries are written by the Serilog
/// sink over raw Npgsql, outside any request scope and outside <see cref="IUnitOfWork"/>,
/// because a log write must not join — or be rolled back with — the business transaction
/// that produced it.
/// </summary>
public interface IAppLogRepository {
    /// <summary>
    /// Newest first, filtered. <paramref name="minLevel"/> is a level name (Warning,
    /// Error, Fatal); null means all. <paramref name="search"/> matches message,
    /// exception text and source, case-insensitively.
    /// </summary>
    Task<List<AppLog>> GetAsync(string? minLevel = null, DateTime? from = null, DateTime? to = null,
                                string? search = null, int count = 200);
    /// <summary>Per-level counts over the same window, for the summary tiles.</summary>
    Task<List<AppLogLevelCount>> GetLevelCountsAsync(DateTime? from = null, DateTime? to = null);
    /// <summary>
    /// Drops entries older than the cutoff, returning how many went. Keeps this table
    /// from growing without bound, the same discipline as the 30-dump backup retention —
    /// and it lands in every nightly pg_dump, so unbounded growth would be paid for 30
    /// times over.
    /// </summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc);
}

/// <summary>One row of the log viewer's level summary.</summary>
public record AppLogLevelCount(string Level, int Count);

public interface IUnitOfWork {
    Task<int> SaveChangesAsync();
}

public interface IAppSettingsRepository {
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
}
