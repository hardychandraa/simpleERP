using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class PositionModel : PageModel {
    private readonly IFinancialReportService _svc;
    private readonly IPurchaseService _purchases;
    public PositionModel(IFinancialReportService svc, IPurchaseService purchases) { _svc = svc; _purchases = purchases; }
    public int PendingChecks { get; set; }
    public PositionSummaryDto Report { get; set; } = null!;
    public async Task OnGetAsync() {
        PendingChecks = (await _purchases.GetNeedingReviewAsync()).Count;
        ViewData["Title"] = "Position Summary";
        // No date range: this is "where things stand now", read from current balances.
        Report = await _svc.GetPositionSummaryAsync();
    }
}
