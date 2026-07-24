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
public interface IInventoryService {
    Task<ServiceResult> StockInAsync(StockInDto dto);
    Task<ServiceResult> StockOutAsync(Guid productId, decimal qty, Guid referenceId, Guid branchId);
    Task StockInForCancelAsync(Guid productId, decimal qty, decimal unitCost, Guid referenceId, Guid branchId);
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
public interface IPaymentTermService {
    Task<List<PaymentTermDto>> GetAllAsync(bool activeOnly = false);
    Task<PaymentTermDto?> GetByIdAsync(Guid id);
    Task<ServiceResult> CreateAsync(PaymentTermDto dto, string user);
    Task<ServiceResult> UpdateAsync(PaymentTermDto dto, string user);
    Task<ServiceResult> DeleteAsync(Guid id, string user);
}
public interface IFinancialReportService {
    /// <summary>Commercial P&amp;L over an inclusive date range.</summary>
    Task<ProfitAndLossDto> GetProfitAndLossAsync(DateTime from, DateTime to);
}
public interface IAppSettingsService {
    Task<AppSettingsDto> GetAsync();
    Task<ServiceResult> SaveAsync(AppSettingsDto dto);
}
