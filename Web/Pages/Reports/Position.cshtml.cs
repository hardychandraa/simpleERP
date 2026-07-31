using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class PositionModel : PageModel {
    private readonly IFinancialReportService _svc;
    public PositionModel(IFinancialReportService svc) => _svc = svc;
    public PositionSummaryDto Report { get; set; } = null!;
    public async Task OnGetAsync() {
        ViewData["Title"] = "Position Summary";
        // No date range: this is "where things stand now", read from current balances.
        Report = await _svc.GetPositionSummaryAsync();
    }
}
