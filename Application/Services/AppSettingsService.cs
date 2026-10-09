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
    private readonly IPurchaseRepository _purchases;
    private readonly IAuditLogRepository _audit;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<AppSettingsService> _log;
    public AppSettingsService(IAppSettingsRepository repo, IBranchRepository branches,
        IPurchaseRepository purchases, IAuditLogRepository audit,
        IStringLocalizer<SharedResource> loc, ILogger<AppSettingsService> log)
    { _repo = repo; _branches = branches; _purchases = purchases; _audit = audit; _loc = loc; _log = log; }

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
            RebateWithholdingPercent = s.RebateWithholdingRate * 100m,
            BooksClosedThrough = s.BooksClosedThrough?.Date };
    }

    public async Task<ServiceResult> SetBooksClosedThroughAsync(DateTime? monthEnd, string user)
    {
        var target = monthEnd?.Date;
        if (target is { } t)
        {
            // Always a month-end, and only a month that has fully ended: the current month stays
            // open, so anything dated "now" (payments, stock in, adjustments) can always be posted.
            if (t.AddDays(1).Day != 1)
                return _log.Refuse(_loc["Pick the last day of a month."]);
            var firstOfThisMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (t >= firstOfThisMonth)
                return _log.Refuse(_loc["Only a month that has already ended can be closed."]);
        }

        var s = await _repo.GetAsync();
        var current = s.BooksClosedThrough?.Date;
        if (current == target) return _log.Refuse(_loc["Nothing to change."]);

        var closing = target != null && (current == null || target > current);
        if (closing)
        {
            // A purchase still waiting for Admin's check could not be revised once its month is
            // closed, so its cost and HPP would be frozen at the master price.
            var pending = await _purchases.GetNeedingReviewAsync(target);
            if (pending.Count > 0)
                return _log.Refuse(_loc["{0} purchase(s) dated in this period still need checking (Perlu dicek), e.g. {1}. Check them before closing the books.",
                                        pending.Count, pending[0].PurchaseNumber]);
        }

        s.BooksClosedThrough = target;
        await _audit.LogAsync(user, closing ? "Period.Close" : "Period.Reopen",
            $"books closed through {(target?.ToString("yyyy-MM-dd") ?? "none")} (was {(current?.ToString("yyyy-MM-dd") ?? "none")})");
        _log.LogInformation("Books closed through {Target} (was {Current}), by {User}",
            target?.ToString("yyyy-MM-dd") ?? "none", current?.ToString("yyyy-MM-dd") ?? "none", user);
        await _repo.SaveAsync(s);
        return ServiceResult.Ok();
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
