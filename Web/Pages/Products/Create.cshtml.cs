using SimpleERP.Web.Services;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Products;
[HandlesBindingErrors]
public class CreateModel : PageModel {
    private readonly IProductService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public CreateModel(IProductService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }
    [BindProperty] public CreateProductDto Input { get; set; } = new();
    public string? Error { get; set; }
    public void OnGet() { ViewData["Title"]="Add Product"; }
    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"]="Add Product";
        if (!ModelState.IsValid) { Error=_loc["Please fill in every required field with a valid value."]; return Page(); }
        var r = await _svc.CreateAsync(Input);
        if (!r.Success) { Error=r.Error; return Page(); }
        return RedirectToPage("/Products/Index", new { msg=_loc["Product created."].Value });
    }
}
