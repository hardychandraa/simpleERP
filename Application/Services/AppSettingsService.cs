using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class AppSettingsService : IAppSettingsService
{
    private readonly IAppSettingsRepository _repo;
    private readonly IBranchRepository _branches;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<AppSettingsService> _log;
    public AppSettingsService(IAppSettingsRepository repo, IBranchRepository branches,
        IStringLocalizer<SharedResource> loc, ILogger<AppSettingsService> log)
    { _repo = repo; _branches = branches; _loc = loc; _log = log; }

    // 80 = 10 cpi on 8-inch paper, 96 = 12 cpi on the 9.5-inch (half-A4) form,
    // 132 = 10 cpi on a wide-carriage printer. Anything else falls back to 96.
    private static readonly int[] PaperColumnChoices = { 80, 96, 132 };

    public async Task<AppSettingsDto> GetAsync()
    {
        var s = await _repo.GetAsync();
        return new AppSettingsDto {
            AppName = s.AppName, StoreName = s.StoreName,
            StoreAddress = s.StoreAddress, StorePhone = s.StorePhone,
            StoreFooter = s.StoreFooter ?? "Thank you for your purchase!",
            PrinterName = s.PrinterName, PaperColumns = s.PaperColumns, PaperLines = s.PaperLines,
            PrinterEnabled = s.PrinterEnabled,
            WarehouseCode = (await _branches.GetDefaultAsync())?.Code,
            // Stored as a fraction, surfaced to the UI as a percentage.
            VatRatePercent = s.VatRate * 100m,
            RebateWithholdingPercent = s.RebateWithholdingRate * 100m };
    }

    public async Task<ServiceResult> SaveAsync(AppSettingsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.StoreName)) return _log.Refuse(_loc["Store name is required."]);
        if (dto.VatRatePercent < 0m || dto.VatRatePercent > 100m)
            return _log.Refuse(_loc["PPN rate must be between 0 and 100 percent."]);
        if (dto.RebateWithholdingPercent < 0m || dto.RebateWithholdingPercent > 100m)
            return _log.Refuse(_loc["Rebate withholding rate must be between 0 and 100 percent."]);
        // Below ~20 lines the invoice header and totals alone would not fit on one sheet.
        if (dto.PaperLines < 20 || dto.PaperLines > 132)
            return _log.Refuse(_loc["Paper height must be between 20 and 132 lines."]);
        var warehouseCode = string.IsNullOrWhiteSpace(dto.WarehouseCode) ? null : dto.WarehouseCode.Trim().ToUpperInvariant();
        if (warehouseCode?.Length > 10)
            return _log.Refuse(_loc["Warehouse code cannot exceed 10 characters."]);
        var s = await _repo.GetAsync();
        s.AppName      = string.IsNullOrWhiteSpace(dto.AppName) ? "SimpleERP" : dto.AppName.Trim();
        s.StoreName    = dto.StoreName.Trim();
        s.StoreAddress = dto.StoreAddress?.Trim();
        s.StorePhone   = dto.StorePhone?.Trim();
        s.StoreFooter  = string.IsNullOrWhiteSpace(dto.StoreFooter) ? "Thank you for your purchase!" : dto.StoreFooter.Trim();
        s.PrinterName  = dto.PrinterName?.Trim() ?? "";
        s.PaperColumns = PaperColumnChoices.Contains(dto.PaperColumns) ? dto.PaperColumns : 96;
        s.PaperLines   = dto.PaperLines;
        // The default branch is tracked by the same DbContext, so the settings save below
        // writes this too — one save, not two.
        var branch = await _branches.GetDefaultAsync();
        if (branch != null) branch.Code = warehouseCode;
        s.PrinterEnabled = dto.PrinterEnabled;
        s.VatRate      = dto.VatRatePercent / 100m;
        s.RebateWithholdingRate = dto.RebateWithholdingPercent / 100m;
        await _repo.SaveAsync(s);
        return ServiceResult.Ok();
    }
}
