using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class ProfitLossModel : PageModel {
    private readonly IFinancialReportService _svc;
    public ProfitLossModel(IFinancialReportService svc) => _svc = svc;
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
    }
}
