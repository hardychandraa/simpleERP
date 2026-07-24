using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Reports;
public class WarrantyModel : PageModel {
    private readonly IReportService _svc;
    public WarrantyModel(IReportService svc) => _svc = svc;
    public List<WarrantyItemDto> Items  { get; set; } = new();
    public string? Search      { get; set; }
    public bool    ActiveOnly  { get; set; } = true;
    public async Task OnGetAsync(string? search, bool activeOnly = true) {
        ViewData["Title"] = "Warranty Lookup";
        Search = search?.Trim(); ActiveOnly = activeOnly;
        Items = await _svc.GetWarrantiesAsync(Search, activeOnly);
    }
}
