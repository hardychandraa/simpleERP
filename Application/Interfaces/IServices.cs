using SimpleERP.Application.DTOs;
using SimpleERP.Domain.Enums;

namespace SimpleERP.Application.Interfaces;

public interface IAuditService
{
    Task<List<AuditLogDto>> GetRecentAsync(int count = 200);
}

/// <summary>
/// Reads the diagnostic log for the viewer page. Read-only by design — entries are
/// written by the Serilog sink, never by application code calling into here.
/// </summary>
public interface IAppLogService
{
    Task<List<AppLogDto>> GetAsync(string? minLevel = null, DateTime? from = null, DateTime? to = null,
                                   string? search = null, int count = 200);
    Task<AppLogSummaryDto> GetSummaryAsync(DateTime? from = null, DateTime? to = null);
}
public interface IProductService {
    Task<List<ProductDto>> GetAllAsync(string? search = null);
    Task<List<ProductDto>> GetAllActiveAsync(string? search = null);
    Task<ProductDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(CreateProductDto dto);
    Task<ServiceResult> UpdateAsync(UpdateProductDto dto);
    Task<ServiceResult> DeactivateAsync(Guid id);
}
public interface ICustomerService {
    Task<List<CustomerDto>> GetAllAsync(string? search = null);
    Task<List<CustomerDto>> GetAllActiveAsync(string? search = null);
    Task<CustomerDto?> GetByIdAsync(Guid id);
    /// <param name="setsSaleDefaults">Admin only (G6): Staff's default discount / term / salesperson are ignored.</param>
    Task<ServiceResult> CreateAsync(CreateCustomerDto dto, bool setsSaleDefaults);
    Task<ServiceResult> UpdateAsync(UpdateCustomerDto dto, bool setsSaleDefaults);
}
/// <summary>
/// One line of a purchase to receive into stock: the quantity, and the real ex-PPN
/// cost per unit after every discount. Cost is passed in rather than derived here —
/// working out what a line actually cost is PurchaseService's job.
/// </summary>
public record PurchaseReceiptLine(Guid ProductId, decimal Qty, decimal UnitCost);

/// <summary>
/// One line of a whole-document stock movement. ProductName rides along so a guarded
/// stock-out can name the product that failed — the caller no longer has the entity
/// handy once the whole document is passed at once.
/// </summary>
public record StockMovementLine(Guid ProductId, string ProductName, decimal Qty, decimal UnitCost);

public interface IInventoryService {
    Task<ServiceResult> StockInAsync(StockInDto dto);
    /// <param name="at">The document's ledger date (UTC); null = now. See LedgerDate.</param>
    Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId, DateTime? at = null);
    Task StockInForCancelAsync(Guid productId, decimal qty, decimal unitCost, Guid referenceId, Guid branchId);
    Task StockInForPurchaseAsync(IEnumerable<PurchaseReceiptLine> lines, Guid purchaseId, Guid branchId, DateTime? at = null);
    Task<ServiceResult> StockOutForPurchaseCancelAsync(IEnumerable<StockMovementLine> lines, Guid purchaseId, Guid branchId);
    Task StockInForRebateAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForCustomerReturnAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId, DateTime? at = null);
    Task<ServiceResult> StockOutForCustomerReturnCancelAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task<ServiceResult> StockOutForSupplierReturnAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId, DateTime? at = null);
    Task StockInForSupplierReturnCancelAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task<ServiceResult> AdjustStockAsync(StockAdjustmentDto dto, string user);
    /// <summary>Hold these products' stock until the operation saves (see IInventoryLedgerRepository.LockProductsAsync).</summary>
    Task LockStockAsync(IEnumerable<Guid> productIds);
    Task<decimal> GetCurrentStockAsync(Guid productId);
    Task<decimal> GetCurrentAvgCostAsync(Guid productId);
    Task<List<StockLevelDto>> GetAllStockLevelsAsync();
    /// <summary>Stock card for one product (or all), movements in [fromUtc, toUtc) by document date.</summary>
    Task<StockCardDto> GetStockCardAsync(Guid? productId, DateTime? fromUtc, DateTime? toUtc);
}
/// <summary>End-of-day stock check: the daily stock sheet and stock counts (HC, 2026-10-14).</summary>
public interface IStockCountService {
    /// <summary>Opening, in, out and closing per product for one local calendar day.</summary>
    Task<DailyStockSheetDto> GetDailySheetAsync(DateTime localDay);
    /// <summary>Counts whose count moment (submit, or creation for a draft) falls in the local days given.</summary>
    Task<List<StockCountListDto>> GetListAsync(DateTime? fromLocal, DateTime? toLocal, StockCountStatus? status);
    Task<int> CountAwaitingReviewAsync();
    /// <param name="withReview">Also work out, for lines waiting for Admin, the difference as the ledger says it now and the documents entered after the count.</param>
    Task<StockCountDto?> GetAsync(Guid id, bool withReview);
    Task<ServiceResult<Guid>> CreateAsync(StockCountScope scope, IEnumerable<Guid>? productIds, string user);
    Task<ServiceResult> SaveDraftAsync(Guid id, IEnumerable<StockCountLineInput> inputs, string? notes, bool isAdmin, string user);
    Task<ServiceResult> AddProductsAsync(Guid id, IEnumerable<Guid> productIds, bool isAdmin, string user);
    Task<ServiceResult> RemoveLineAsync(Guid id, Guid lineId, bool isAdmin, string user);
    /// <param name="openedAtUtc">When the counting form was loaded: products that moved since are sent back to be checked.</param>
    Task<StockCountSubmitResult> SubmitAsync(Guid id, IEnumerable<StockCountLineInput> inputs, string? notes, DateTime openedAtUtc, bool isAdmin, string user);
    /// <summary>Admin settles one difference: post it as an adjustment, or explain it without changing stock.</summary>
    Task<ServiceResult> DecideAsync(Guid id, Guid lineId, bool postAdjustment, string? note, bool isAdmin, string user);
    Task<ServiceResult> CancelAsync(Guid id, string? reason, bool isAdmin, string user);
}
public interface ISaleService {
    /// <param name="isAdmin">Staff sales are limited to today/yesterday and flagged Perlu dicek when below cost, over the discount limit, dated yesterday or with another salesperson (G4/G5/G7).</param>
    Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user, bool isAdmin);
    /// <summary>
    /// Cancels <paramref name="originalId"/> and creates <paramref name="dto"/> in its place,
    /// as one transaction — both happen or neither does. Refused when the original has
    /// recorded payments, paid-out commission, a return or an applied credit note.
    /// </summary>
    Task<ServiceResult<SaleDto>> ReviseAsync(Guid originalId, CreateSaleDto dto, string user, bool isAdmin, string? reason);
    /// <summary>Admin only since R3 (HC 2026-10-16); Staff send a request (RequestChangeAsync). Admin: any open-period sale.</summary>
    Task<ServiceResult> CancelAsync(Guid saleId, string user, bool isAdmin, string? reason);
    Task<List<SaleListDto>> GetNeedingReviewAsync();
    Task<List<SaleListDto>> GetCancelledTodayAsync();
    Task<ServiceResult> MarkReviewedAsync(Guid saleId, bool isAdmin, string user);
    /// <param name="isAdmin">Staff's payment is pending until Admin verifies it (R1, HC 2026-10-16): it changes nothing until then.</param>
    Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user, bool isAdmin);
    /// <summary>Admin: the payment counts, dated when Staff received it.</summary>
    Task<ServiceResult> VerifyPaymentAsync(Guid pendingId, bool isAdmin, string user);
    /// <summary>Admin: the payment never counts; the reason is kept.</summary>
    Task<ServiceResult> RejectPaymentAsync(Guid pendingId, string? reason, bool isAdmin, string user);
    /// <summary>
    /// Admin takes back a payment entered wrong (HC, 2026-10-21): the row stays, marked reversed;
    /// the invoice owes it again; its commission is voided, or clawed back if already paid out.
    /// </summary>
    Task<ServiceResult> ReversePaymentAsync(Guid paymentId, string? reason, bool isAdmin, string user);
    /// <summary>Received settlements (STL-R-…), newest first, with their lines.</summary>
    Task<List<PaymentBatchDto>> GetReceivedSettlementsAsync(DateTime? from = null, DateTime? to = null, Guid? customerId = null);
    Task<List<PendingPaymentDto>> GetPendingPaymentsAsync();
    Task<List<PendingPaymentDto>> GetPendingPaymentsForSaleAsync(Guid saleId);
    /// <summary>Staff ask Admin to cancel or revise (R3, HC 2026-10-16).</summary>
    Task<ServiceResult> RequestChangeAsync(Guid saleId, string? reason, string user);
    /// <summary>
    /// Settles several of one customer's open invoices in one action, optionally netting
    /// open credit notes off the cash collected. All-or-nothing: if any line or note fails
    /// validation, nothing is written.
    /// </summary>
    Task<ServiceResult<PaymentBatchDto>> RecordBatchPaymentAsync(RecordSaleBatchPaymentDto dto, string user);
    /// <summary>
    /// One customer's statement of account — every open invoice plus the credit notes held
    /// against them. Optionally narrowed to invoices dated in [from, to]; narrowing the
    /// list never changes what's payable.
    /// </summary>
    Task<PaymentStatementDto?> GetCustomerStatementAsync(Guid customerId, DateTime? from = null, DateTime? to = null);
    Task<SaleDto?> GetByIdAsync(Guid id);
    Task<List<SaleListDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    /// <summary>
    /// Tanda terima: one customer's non-cancelled invoices dated <paramref name="from"/> to
    /// <paramref name="to"/> (local calendar dates, both inclusive), with what is still owed
    /// on each after payments and applied credit notes. Null if the customer doesn't exist.
    /// </summary>
    Task<InvoiceReceiptDto?> GetInvoiceReceiptAsync(Guid customerId, DateTime from, DateTime to);
    Task<List<DueCustomerDto>> GetDueSummaryAsync();
    /// <summary>One customer's plafon, open credit balance and overdue invoices (Admin only).</summary>
    Task<CustomerCreditDto> GetCreditStatusAsync(Guid customerId);
    /// <summary>
    /// The same for every active customer, by id: the New Sale page's credit panel (Admin only).
    /// <paramref name="excludeSaleId"/> leaves out the invoice being revised, as saving does.
    /// </summary>
    Task<Dictionary<Guid, CustomerCreditDto>> GetCreditStatusesAsync(Guid? excludeSaleId = null);
    /// <summary>A suggested plafon per active customer, from their last 90 days of credit sales (Admin only).</summary>
    Task<Dictionary<Guid, CreditSuggestionDto>> GetCreditSuggestionsAsync();
}
public interface IReportService {
    Task<EndOfDayDto> GetEndOfDayAsync(DateTime date);
    Task<List<WarrantyItemDto>> GetWarrantiesAsync(string? search = null, bool activeOnly = true);
    Task<List<AuditLogDto>> GetAuditLogAsync(int count = 100);
}
public interface IExpenseService {
    Task<List<ExpenseDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, Guid? categoryId = null);
    Task<ExpenseDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(CreateExpenseDto dto, string user);
    Task<ServiceResult> UpdateAsync(UpdateExpenseDto dto, string user);
    Task<ServiceResult> DeleteAsync(Guid id, string user);

    Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false);
    Task<ServiceResult> CreateCategoryAsync(ExpenseCategoryDto dto, string user);
    Task<ServiceResult> UpdateCategoryAsync(ExpenseCategoryDto dto, string user);
    Task<ServiceResult> DeleteCategoryAsync(Guid id, string user);
}
public interface IPaymentTermService {
    Task<List<PaymentTermDto>> GetAllAsync(bool activeOnly = false);
    Task<PaymentTermDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(PaymentTermDto dto, string user);
    Task<ServiceResult> UpdateAsync(PaymentTermDto dto, string user);
    Task<ServiceResult> DeleteAsync(Guid id, string user);
}
public interface ISupplierService {
    Task<List<SupplierDto>> GetAllAsync(bool activeOnly = false);
    Task<SupplierDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(SupplierDto dto, string user);
    Task<ServiceResult> UpdateAsync(SupplierDto dto, string user);
    Task<ServiceResult> DeleteAsync(Guid id, string user);
}
public interface IPurchaseService {
    Task<ServiceResult<PurchaseDto>> CreateAsync(CreatePurchaseDto dto, string user);
    Task<ServiceResult> CancelAsync(Guid purchaseId, string user);
    /// <summary>Barang Masuk (Staff): lines costed at the master harga beli, flagged Perlu dicek. Returns the id only.</summary>
    Task<ServiceResult<Guid>> CreateUnpricedAsync(CreateUnpricedPurchaseDto dto, string user);
    /// <summary>Staff edit of a Barang Masuk entry; existing lines keep their price; flags Perlu dicek again.</summary>
    Task<ServiceResult> EditUnpricedAsync(Guid purchaseId, CreateUnpricedPurchaseDto dto, string user);
    /// <summary>Admin correction in place, with re-costing of everything posted after it.</summary>
    Task<ServiceResult> ReviseAsync(Guid purchaseId, CreatePurchaseDto dto, bool markReviewed, string user);
    Task<ServiceResult> MarkReviewedAsync(Guid purchaseId, string user);
    Task<List<PurchaseQtyListDto>> GetQtyListAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<PurchaseQtyDto?> GetQtyDetailAsync(Guid id);
    Task<List<PurchaseListDto>> GetNeedingReviewAsync();
    Task<ServiceResult<SupplierPaymentDto>> RecordPaymentAsync(RecordSupplierPaymentDto dto, string user);
    /// <summary>
    /// Settles several of one supplier's open purchases in one action — the shape a real
    /// supplier statement takes — optionally netting open debit notes off the cash paid.
    /// All-or-nothing: if any line or note fails validation, nothing is written.
    /// </summary>
    Task<ServiceResult<PaymentBatchDto>> RecordBatchPaymentAsync(RecordSupplierBatchPaymentDto dto, string user);
    /// <summary>
    /// One supplier's statement of account — every open purchase, the debit notes held
    /// against them, and (for information only) any outstanding cash rebate.
    /// </summary>
    Task<PaymentStatementDto?> GetSupplierStatementAsync(Guid supplierId, DateTime? from = null, DateTime? to = null);
    Task<PurchaseDto?> GetByIdAsync(Guid id);
    Task<List<PurchaseListDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<List<DueSupplierDto>> GetDueSummaryAsync();
}
public interface ISalesPersonService {
    Task<List<SalesPersonDto>> GetAllAsync(bool activeOnly = false);
    Task<SalesPersonDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(SalesPersonDto dto, string user);
    Task<ServiceResult> UpdateAsync(SalesPersonDto dto, string user);
    Task<ServiceResult> DeleteAsync(Guid id, string user);
}
/// <summary>
/// Verifies credentials. Deliberately separate from <see cref="IUserService"/>: this is
/// the only path that reads a password hash, and it is reachable anonymously, so it is
/// worth keeping distinct from the account-management surface that requires Admin.
/// </summary>
public interface IAuthService {
    /// <summary>
    /// Returns the account on success. Failure is deliberately undifferentiated — wrong
    /// password, unknown user, deactivated and locked out all return the same message, so
    /// the login form cannot be used to discover which usernames exist.
    /// </summary>
    Task<ServiceResult<AuthenticatedUserDto>> LoginAsync(string username, string password);
    /// <summary>Records the logout in the audit trail. No state to clear server-side.</summary>
    Task LogoutAsync(string user);
}

/// <summary>Account management. Every method here is Admin-only except ChangeOwnPasswordAsync.</summary>
public interface IUserService {
    /// <summary>
    /// True when no account exists at all. Without a way to detect this the app would be
    /// permanently unusable on a new database: every page needs a login, and creating a
    /// login needs an Admin who does not exist yet.
    /// </summary>
    Task<bool> AnyUserExistsAsync();
    /// <summary>
    /// Creates the very first account, always as an Admin. Refuses once any account
    /// exists, so the anonymous setup page cannot be replayed later to mint an Admin.
    /// </summary>
    Task<ServiceResult> CreateFirstAdminAsync(string username, string displayName, string password);
    Task<List<UserDto>> GetAllAsync(bool activeOnly = false);
    Task<UserDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(UserDto dto, string password, string user);
    /// <summary>Display name, role and active flag only — never the username or password.</summary>
    Task<ServiceResult> UpdateAsync(UserDto dto, string user);
    /// <summary>Admin-driven reset. There is no email in this app, so there is no self-service link.</summary>
    Task<ServiceResult> ResetPasswordAsync(Guid id, string newPassword, string user);
    /// <summary>Returns the new security stamp, for re-issuing the caller's own cookie.</summary>
    Task<ServiceResult<string>> ChangeOwnPasswordAsync(string username, string currentPassword, string newPassword);
}

public interface ICommissionService {
    // Rules
    Task<List<CommissionRuleDto>> GetRulesAsync(bool activeOnly = false);
    Task<CommissionRuleDto?> GetRuleAsync(Guid id);
    Task<ServiceResult> CreateRuleAsync(CommissionRuleDto dto, string user);
    Task<ServiceResult> UpdateRuleAsync(CommissionRuleDto dto, string user);
    Task<ServiceResult> DeleteRuleAsync(Guid id, string user);

    // Accruals / payouts
    Task<List<CommissionAccrualDto>> GetAccrualsAsync(Guid? salesPersonId = null, bool? unpaidOnly = null);
    Task<List<CommissionAccrualDto>> GetAccrualsForSaleAsync(Guid saleId);
    Task<List<CommissionUnpaidDto>> GetUnpaidSummaryAsync();
    Task<List<CommissionPayoutDto>> GetPayoutsAsync(Guid? salesPersonId = null);
    Task<ServiceResult> PayoutAsync(PayoutCommissionDto dto, string user);
}
public interface IRebateService {
    // Rules
    Task<List<RebateRuleDto>> GetRulesAsync(bool activeOnly = false);
    Task<RebateRuleDto?> GetRuleAsync(Guid id);
    Task<ServiceResult> CreateRuleAsync(RebateRuleDto dto, string user);
    Task<ServiceResult> UpdateRuleAsync(RebateRuleDto dto, string user);
    Task<ServiceResult> DeleteRuleAsync(Guid id, string user);
    /// <summary>What the rule would accrue on purchases posted before it existed. Writes nothing.</summary>
    Task<ServiceResult<RebateBackfillDto>> PreviewApplyToEarlierAsync(Guid ruleId);
    /// <summary>Accrues the rule on those purchases, in one transaction; the result lists what was written.</summary>
    Task<ServiceResult<RebateBackfillDto>> ApplyToEarlierAsync(Guid ruleId, string user);

    // Accruals / claims
    Task<List<RebateAccrualDto>> GetAccrualsAsync(Guid? supplierId = null, bool? outstandingOnly = null);
    Task<List<RebateAccrualDto>> GetAccrualsForPurchaseAsync(Guid purchaseId);
    Task<List<RebateOutstandingDto>> GetOutstandingSummaryAsync();

    // Realizations
    Task<List<RebateRealizationDto>> GetRealizationsAsync(Guid? supplierId = null);
    Task<ServiceResult> RealizeCashAsync(RealizeCashDto dto, string user);
    Task<ServiceResult> RealizeLuckyDrawAsync(RealizeLuckyDrawDto dto, string user);
    Task<ServiceResult> RealizeInKindAsync(RealizeInKindDto dto, string user);
}
public interface IReturnService {
    // ── Sales returns ──
    /// <summary>The invoice and what's still returnable on it. Null if the sale can't be returned against.</summary>
    Task<ReturnFormDto?> GetCustomerReturnFormAsync(Guid saleId);
    /// <param name="isAdmin">A Staff return on a cash sale is flagged and its note waits for Admin (G3).</param>
    Task<ServiceResult<CustomerReturnDto>> CreateCustomerReturnAsync(CreateCustomerReturnDto dto, string user, bool isAdmin);
    Task<ServiceResult> MarkCustomerReturnReviewedAsync(Guid id, bool isAdmin, string user);
    Task<int> CountCustomerReturnsNeedingReviewAsync();
    Task<CustomerReturnDto?> GetCustomerReturnAsync(Guid id);
    Task<List<ReturnListDto>> GetCustomerReturnsAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<List<ReturnListDto>> GetReturnsForSaleAsync(Guid saleId);
    Task<ServiceResult> CancelCustomerReturnAsync(Guid id, string user);

    // ── Purchase returns ──
    Task<ReturnFormDto?> GetSupplierReturnFormAsync(Guid purchaseId);
    Task<ServiceResult<SupplierReturnDto>> CreateSupplierReturnAsync(CreateSupplierReturnDto dto, string user);
    Task<SupplierReturnDto?> GetSupplierReturnAsync(Guid id);
    Task<List<ReturnListDto>> GetSupplierReturnsAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<List<ReturnListDto>> GetReturnsForPurchaseAsync(Guid purchaseId);
    Task<ServiceResult> CancelSupplierReturnAsync(Guid id, string user);
}
public interface ICreditNoteService {
    Task<List<CreditNoteDto>> GetAllAsync(CreditDebitType? type = null, CreditNoteStatus? status = null,
                                          DateTime? from = null, DateTime? to = null);
    Task<CreditNoteDto?> GetByIdAsync(Guid id);
    /// <param name="isAdmin">A credit note Staff raise waits for Admin's approval (G1).</param>
    Task<ServiceResult> CreateAsync(CreateCreditNoteDto dto, string user, bool isAdmin);
    Task<ServiceResult> ApproveAsync(Guid id, bool isAdmin, string user);
    Task<int> CountAwaitingApprovalAsync();
    /// <summary>
    /// Settle a note outright with free text, no document behind it — a price dispute
    /// refunded in cash, say. Still the right path when there is nothing to apply
    /// against; <see cref="ApplyAsync"/> is for when there is.
    /// </summary>
    /// <summary>Admin only (G1): settling is where a note becomes a refund or a payment.</summary>
    Task<ServiceResult> SettleAsync(SettleCreditNoteDto dto, string user, bool isAdmin);
    Task<ServiceResult> CancelAsync(Guid id, string user);
    /// <summary>
    /// Apply part or all of a note against one specific invoice or purchase, reducing
    /// that document's balance. Capped at both the note's remaining amount and the
    /// document's own remaining balance. Settles the note once nothing is left.
    /// </summary>
    Task<ServiceResult> ApplyAsync(ApplyCreditNoteDto dto, string user);
    /// <summary>Undo an application while both the note and the document are still open.</summary>
    Task<ServiceResult> ReverseApplicationAsync(Guid applicationId, string user);
    /// <summary>Notes applied against one invoice — its detail page's "credit notes applied" card.</summary>
    Task<List<CreditNoteApplicationDto>> GetApplicationsForSaleAsync(Guid saleId);
    Task<List<CreditNoteApplicationDto>> GetApplicationsForPurchaseAsync(Guid purchaseId);
    /// <summary>One application by id, or null. Lets a page check which document it belongs to before reversing it.</summary>
    Task<CreditNoteApplicationDto?> GetApplicationAsync(Guid applicationId);
    /// <summary>Remaining value of still-Open notes, by direction — the slice not yet tied to any document.</summary>
    Task<decimal> GetOpenTotalAsync(CreditDebitType type);
    /// <summary>
    /// Raises an Open debit note (category RebateSettlement) for a rebate realized in
    /// cash, so it flows through the same per-purchase <see cref="ApplyAsync"/> mechanism
    /// as any other debit note — closing the gap where a realized rebate previously
    /// reduced nothing. Called by RebateService, never directly from a page.
    /// Returns the new note's document number, or null (creating nothing) when
    /// netAmount is zero or negative. Does not call SaveChangesAsync — the caller owns
    /// the transaction, matching InventoryService.StockInForRebateAsync's shape.
    /// </summary>
    Task<string?> CreateRebateSettlementNoteAsync(Guid supplierId, decimal netAmount, string reference, string user);
}
public interface IFinancialReportService {
    /// <summary>Commercial P&amp;L over an inclusive date range.</summary>
    Task<ProfitAndLossDto> GetProfitAndLossAsync(DateTime from, DateTime to);
    /// <summary>
    /// Point-in-time position: inventory, AR, AP, unclaimed rebate and commission
    /// payable. No date range — it's "where things stand now", read from current
    /// balances rather than from a period's movements.
    /// </summary>
    Task<PositionSummaryDto> GetPositionSummaryAsync();
}
public interface IAppSettingsService {
    Task<AppSettingsDto> GetAsync();
    Task<ServiceResult> SaveAsync(AppSettingsDto dto);
    /// <summary>
    /// Month lock: close the books through the end of a fully ended month, or move the lock
    /// back (reopen). Null = nothing closed. Audited; refuses to close a month that still has
    /// purchases waiting for Admin's check.
    /// </summary>
    Task<ServiceResult> SetBooksClosedThroughAsync(DateTime? monthEnd, string user);
}

/// <summary>Admin's analysis reports (2026-10-15): all read-only, all on the P&amp;L's basis.</summary>
public interface ISalesAnalysisService {
    Task<SalesAnalysisDto> GetByProductAsync(DateTime from, DateTime to);
    Task<SalesAnalysisDto> GetByCustomerAsync(DateTime from, DateTime to);
    Task<PpnSummaryDto> GetPpnSummaryAsync(DateTime from, DateTime to);
    /// <summary>Every open credit invoice, oldest due first: the receivables collection list.</summary>
    Task<List<OpenDocumentDto>> GetOpenReceivablesAsync();
    /// <summary>Every open credit purchase, oldest due first.</summary>
    Task<List<OpenDocumentDto>> GetOpenPayablesAsync();
}
