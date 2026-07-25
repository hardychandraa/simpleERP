using SimpleERP.Application.DTOs;

namespace SimpleERP.Application.Interfaces;

public interface IAuditService
{
    Task<List<AuditLogDto>> GetRecentAsync(int count = 200);
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

public interface IInventoryService {
    Task<ServiceResult> StockInAsync(StockInDto dto);
    Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForCancelAsync(Guid productId, decimal qty, decimal unitCost, Guid referenceId, Guid branchId);
    Task StockInForPurchaseAsync(IEnumerable<PurchaseReceiptLine> lines, Guid purchaseId, Guid branchId);
    Task<ServiceResult> StockOutForPurchaseCancelAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForRebateAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task<ServiceResult> AdjustStockAsync(StockAdjustmentDto dto, string user);
    Task<decimal> GetCurrentStockAsync(Guid productId);
    Task<decimal> GetCurrentAvgCostAsync(Guid productId);
    Task<List<StockLevelDto>> GetAllStockLevelsAsync();
    Task<List<InventoryLedgerDto>> GetLedgerAsync(DateTime? from = null, DateTime? to = null);
}
public interface ISaleService {
    Task<ServiceResult<SaleDto>> CreateAsync(CreateSaleDto dto, string user);
    Task<ServiceResult> CancelAsync(Guid saleId, string user);
    Task<ServiceResult<PaymentRecordDto>> RecordPaymentAsync(RecordPaymentDto dto, string user);
    Task<SaleDto?> GetByIdAsync(Guid id);
    Task<List<SaleListDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null);
    Task<List<DueCustomerDto>> GetDueSummaryAsync();
    Task<string> GenerateTxtInvoiceAsync(Guid saleId);
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
public interface IFinancialReportService {
    /// <summary>Commercial P&amp;L over an inclusive date range.</summary>
    Task<ProfitAndLossDto> GetProfitAndLossAsync(DateTime from, DateTime to);
}
public interface IAppSettingsService {
    Task<AppSettingsDto> GetAsync();
    Task<ServiceResult> SaveAsync(AppSettingsDto dto);
}
