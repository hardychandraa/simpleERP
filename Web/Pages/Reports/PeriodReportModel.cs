using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Pages.Reports;

/// <summary>
/// What every period report shares (2026-10-15): the from/to filter (default: this month to
/// date, the period most often checked, as on Laba Rugi), the warning for purchases still
/// awaiting Admin's check in the period, and the Excel heading.
/// </summary>
public abstract class PeriodReportModel : PageModel
{
    protected readonly IAppSettingsService Settings;
    protected readonly IPurchaseService Purchases;
    protected readonly IStringLocalizer<SharedResource> L;
    protected PeriodReportModel(IAppSettingsService settings, IPurchaseService purchases, IStringLocalizer<SharedResource> loc)
    { Settings = settings; Purchases = purchases; L = loc; }

    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    /// <summary>Purchases dated in the period still Perlu dicek: HPP and PPN Masukan can still change.</summary>
    public int PendingChecks { get; set; }

    protected async Task SetPeriodAsync(DateTime? from, DateTime? to)
    {
        To   = to?.Date   ?? DateTime.Today;
        From = from?.Date ?? new DateTime(To.Year, To.Month, 1);
        if (From > To) (From, To) = (To, From);   // tolerate reversed inputs
        PendingChecks = (await Purchases.GetNeedingReviewAsync())
            .Count(p => p.PurchaseDate.Date >= From && p.PurchaseDate.Date <= To);
    }

    protected async Task<string> StoreNameAsync() => (await Settings.GetAsync()).StoreName;

    /// <summary>"Period 01 Oct 2026 – 15 Oct 2026 · printed 15 Oct 2026 21:40", for the Excel heading.</summary>
    protected string PeriodLine() =>
        L["Period {0} – {1} · printed {2}", From.ToString("dd MMM yyyy"), To.ToString("dd MMM yyyy"), DateTime.Now.ToString("dd MMM yyyy HH:mm")];

    protected string FileStem(string name) => $"{name}_{From:yyyy-MM-dd}_{To:yyyy-MM-dd}";

    /// <summary>The period filter, with this page's Excel link for the same period.</summary>
    public ReportFilterVm Filter => new(From, To, $"{Request.Path}?handler=Excel&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}");
}

/// <summary>The _ReportFilter partial: the period shown and where its Excel download is.</summary>
public record ReportFilterVm(DateTime From, DateTime To, string ExcelUrl);

/// <summary>The _SalesAnalysis partial's model.</summary>
public record SalesAnalysisVm(SimpleERP.Application.DTOs.SalesAnalysisDto Report, bool ByProduct);
