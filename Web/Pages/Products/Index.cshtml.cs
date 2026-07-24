using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Products;
public class IndexModel : PageModel {
    private readonly IProductService _svc;
    public IndexModel(IProductService svc) => _svc = svc;
    public List<ProductDto> Products { get; set; } = new();
    public string? Search   { get; set; }
    public string? Msg      { get; set; }
    public bool    IsErr    { get; set; }
    public async Task OnGetAsync(string? search, string? msg, bool err = false) {
        ViewData["Title"] = "Products";
        Search = search?.Trim(); Msg = msg; IsErr = err;
        var all = await _svc.GetAllAsync();
        if (!string.IsNullOrEmpty(Search)) {
            var s = Search.ToLower();
            all = all.Where(p => p.Name.ToLower().Contains(s) || p.SKU.ToLower().Contains(s)
                              || (p.Category?.ToLower().Contains(s) ?? false)).ToList();
        }
        Products = all;
    }
    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, string? search) {
        var r = await _svc.DeactivateAsync(id);
        return RedirectToPage(new { search, msg = r.Success ? "Product deactivated." : r.Error, err = !r.Success });
    }
}
