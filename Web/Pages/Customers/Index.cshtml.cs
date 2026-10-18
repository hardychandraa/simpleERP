using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;
namespace SimpleERP.Web.Pages.Customers;
public class IndexModel : PageModel {
    private readonly ICustomerService _svc;
    private readonly ISaleService _sales;
    public IndexModel(ICustomerService svc, ISaleService sales) { _svc = svc; _sales = sales; }
    public List<CustomerDto> Customers { get; set; } = new();
    /// <summary>Suggested plafon per customer; filled for Admin only (Staff never see limits).</summary>
    public Dictionary<Guid, CreditSuggestionDto> Suggestions { get; set; } = new();
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
        // Staff never receive a customer's balance or limit, not even in the model (HC, 2026-10-18).
        if (!User.MaintainsMasterData())
            foreach (var c in all) { c.TotalDue = 0; c.CreditLimit = null; }
        Customers = all;
        if (User.MaintainsMasterData()) Suggestions = await _sales.GetCreditSuggestionsAsync();
    }
}
