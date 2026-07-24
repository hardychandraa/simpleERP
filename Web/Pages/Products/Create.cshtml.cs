using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Products;
public class CreateModel : PageModel {
    private readonly IProductService _svc;
    public CreateModel(IProductService svc) => _svc = svc;
    [BindProperty] public CreateProductDto Input { get; set; } = new();
    public string? Error { get; set; }
    public void OnGet() { ViewData["Title"]="Add Product"; }
    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"]="Add Product";
        if (!ModelState.IsValid) return Page();
        var r = await _svc.CreateAsync(Input);
        if (!r.Success) { Error=r.Error; return Page(); }
        return RedirectToPage("/Products/Index", new { msg="Product created." });
    }
}
