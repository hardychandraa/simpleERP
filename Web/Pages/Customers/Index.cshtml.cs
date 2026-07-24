using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Customers;
public class IndexModel : PageModel {
    private readonly ICustomerService _svc;
    public IndexModel(ICustomerService svc) => _svc = svc;
    public List<CustomerDto> Customers { get; set; } = new();
    public string? Search { get; set; }
    public string? Msg    { get; set; }
    public bool    IsErr  { get; set; }
    public async Task OnGetAsync(string? search, string? msg, bool err = false) {
        ViewData["Title"] = "Customers";
        Search = search?.Trim(); Msg = msg; IsErr = err;
        var all = await _svc.GetAllAsync();
        if (!string.IsNullOrEmpty(Search)) {
            var s = Search.ToLower();
            all = all.Where(c => c.Name.ToLower().Contains(s) || (c.Phone?.Contains(s) ?? false)
                              || (c.Address?.ToLower().Contains(s) ?? false)).ToList();
        }
        Customers = all;
    }
}
