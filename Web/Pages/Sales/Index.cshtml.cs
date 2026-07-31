using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Sales;

public class IndexModel : PageModel
{
    private readonly ISaleService _svc;
    public IndexModel(ISaleService svc) => _svc = svc;

    public List<SaleListDto> Sales { get; set; } = new();
    public DateTime? From  { get; set; }
    public DateTime? To    { get; set; }
    public string? Search  { get; set; }
    public string? Msg     { get; set; }
    public bool IsErr      { get; set; }

    public async Task OnGetAsync(DateTime? from, DateTime? to, string? search, string? msg, bool err = false)
    {
        ViewData["Title"] = "Sales History";
        From = from; To = to; Search = search; Msg = msg; IsErr = err;
        Sales = await _svc.GetAllAsync(from?.ToUniversalTime(), to?.ToUniversalTime().AddDays(1), search);
    }
}
