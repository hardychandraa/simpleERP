using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Sales;
public class DueModel : PageModel {
    private readonly ISaleService _svc;
    public DueModel(ISaleService svc) => _svc = svc;
    public List<DueCustomerDto> Summary { get; set; } = new();
    public decimal TotalOutstanding { get; set; }
    public async Task OnGetAsync() {
        ViewData["Title"] = "Due Payments";
        Summary = await _svc.GetDueSummaryAsync();
        TotalOutstanding = Summary.Sum(d => d.TotalDue);
    }
}
