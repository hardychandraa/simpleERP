using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsRepository _repo;
    public AppSettingsService(IAppSettingsRepository repo) => _repo = repo;

    public async Task<AppSettingsDto> GetAsync()
    {
        var s = await _repo.GetAsync();
        return new AppSettingsDto {
            AppName = s.AppName, StoreName = s.StoreName,
            StoreAddress = s.StoreAddress, StorePhone = s.StorePhone,
            StoreFooter = s.StoreFooter ?? "Thank you for your purchase!",
            PrinterName = s.PrinterName, PaperColumns = s.PaperColumns,
            PrinterEnabled = s.PrinterEnabled,
            // Stored as a fraction, surfaced to the UI as a percentage.
            VatRatePercent = s.VatRate * 100m,
            RebateWithholdingPercent = s.RebateWithholdingRate * 100m };
    }

    public async Task<ServiceResult> SaveAsync(AppSettingsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.StoreName)) return ServiceResult.Fail("Store name is required.");
        if (dto.VatRatePercent < 0m || dto.VatRatePercent > 100m)
            return ServiceResult.Fail("PPN rate must be between 0 and 100 percent.");
        if (dto.RebateWithholdingPercent < 0m || dto.RebateWithholdingPercent > 100m)
            return ServiceResult.Fail("Rebate withholding rate must be between 0 and 100 percent.");
        var s = await _repo.GetAsync();
        s.AppName      = string.IsNullOrWhiteSpace(dto.AppName) ? "SimpleERP" : dto.AppName.Trim();
        s.StoreName    = dto.StoreName.Trim();
        s.StoreAddress = dto.StoreAddress?.Trim();
        s.StorePhone   = dto.StorePhone?.Trim();
        s.StoreFooter  = string.IsNullOrWhiteSpace(dto.StoreFooter) ? "Thank you for your purchase!" : dto.StoreFooter.Trim();
        s.PrinterName  = dto.PrinterName?.Trim() ?? "";
        s.PaperColumns = dto.PaperColumns == 132 ? 132 : 80;
        s.PrinterEnabled = dto.PrinterEnabled;
        s.VatRate      = dto.VatRatePercent / 100m;
        s.RebateWithholdingRate = dto.RebateWithholdingPercent / 100m;
        await _repo.SaveAsync(s);
        return ServiceResult.Ok();
    }
}
