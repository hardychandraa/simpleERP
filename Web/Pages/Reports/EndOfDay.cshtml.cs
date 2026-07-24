using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class EndOfDayModel : PageModel {
    private readonly IReportService _svc;
    public EndOfDayModel(IReportService svc) => _svc = svc;
    public EndOfDayDto Report { get; set; } = null!;
    public DateTime SelectedDate { get; set; }
    public async Task OnGetAsync(DateTime? date) {
        ViewData["Title"] = "End of Day";
        SelectedDate = date?.Date ?? DateTime.Today;
        Report = await _svc.GetEndOfDayAsync(SelectedDate);
    }
}
