using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class ProfitLossModel : PageModel {
    private readonly IFinancialReportService _svc;
    private readonly IPurchaseService _purchases;
    private readonly IAppSettingsService _settings;
    public ProfitLossModel(IFinancialReportService svc, IPurchaseService purchases, IAppSettingsService settings)
    { _svc = svc; _purchases = purchases; _settings = settings; }
    /// <summary>Purchases dated in the period still flagged Perlu dicek: their HPP can still change.</summary>
    public int PendingChecks { get; set; }
    /// <summary>Set when the whole period lies in closed months: the figures are final.</summary>
    public DateTime? ClosedThrough { get; set; }
    public ProfitAndLossDto Report { get; set; } = null!;
    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    public async Task OnGetAsync(DateTime? from, DateTime? to) {
        ViewData["Title"] = "Profit & Loss";
        // Default to the current month to date — the period most often checked.
        To   = to?.Date   ?? DateTime.Today;
        From = from?.Date ?? new DateTime(To.Year, To.Month, 1);
        if (From > To) (From, To) = (To, From);   // tolerate reversed inputs
        Report = await _svc.GetProfitAndLossAsync(From, To);
        PendingChecks = (await _purchases.GetNeedingReviewAsync())
            .Count(p => p.PurchaseDate.Date >= From && p.PurchaseDate.Date <= To);
        var closed = (await _settings.GetAsync()).BooksClosedThrough;
        if (closed is { } c && To <= c) ClosedThrough = c;
    }
}
