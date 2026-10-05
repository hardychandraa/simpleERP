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
    Task<ServiceResult> CreateAsync(CreateCustomerDto dto);
    Task<ServiceResult> UpdateAsync(UpdateCustomerDto dto);
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
    Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForCancelAsync(Guid productId, decimal qty, decimal unitCost, Guid referenceId, Guid branchId);
    Task StockInForPurchaseAsync(IEnumerable<PurchaseReceiptLine> lines, Guid purchaseId, Guid branchId);
    Task<ServiceResult> StockOutForPurchaseCancelAsync(IEnumerable<StockMovementLine> lines, Guid purchaseId, Guid branchId);
    Task StockInForRebateAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForCustomerReturnAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task<ServiceResult> StockOutForCustomerReturnCancelAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task<ServiceResult> StockOutForSupplierReturnAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task StockInForSupplierReturnCancelAsync(IEnumerable<StockMovementLine> lines, Guid returnId, Guid branchId);
    Task<ServiceResult> AdjustStockAsync(StockAdjustmentDto dto, string user);
    Task<decimal> GetCurrentStockAsync(Guid productId);
    Task<decimal> GetCurrentAvgCostAsync(Guid productId);
    Task<List<StockLevelDto>> GetAllStockLevelsAsync();
    Task<List<InventoryLedgerDto>> GetLedgerAsync(DateTime? from = null, DateTime? to = null);
}
public interface ISaleService {
    Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user);
    /// <summary>
    /// Cancels <paramref name="originalId"/> and creates <paramref name="dto"/> in its place,
    /// as one transaction — both happen or neither does. Refused when the original has
    /// recorded payments, paid-out commission, a return or an applied credit note.
    /// </summary>
    Task<ServiceResult<SaleDto>> ReviseAsync(Guid originalId, CreateSaleDto dto, string user);
    Task<ServiceResult> CancelAsync(Guid saleId, string user);
    Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user);
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
    Task<List<DueCustomerDto>> GetDueSummaryAsync();
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
    Task<ServiceResult> ChangeOwnPasswordAsync(string username, string currentPassword, string newPassword);
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
    Task<ServiceResult<CustomerReturnDto>> CreateCustomerReturnAsync(CreateCustomerReturnDto dto, string user);
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
    Task<ServiceResult> CreateAsync(CreateCreditNoteDto dto, string user);
    /// <summary>
    /// Settle a note outright with free text, no document behind it — a price dispute
    /// refunded in cash, say. Still the right path when there is nothing to apply
    /// against; <see cref="ApplyAsync"/> is for when there is.
    /// </summary>
    Task<ServiceResult> SettleAsync(SettleCreditNoteDto dto, string user);
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
}
