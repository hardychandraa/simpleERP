using SimpleERP.Web.Services;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Products;
[HandlesBindingErrors]
public class EditModel : PageModel {
    private readonly IProductService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public EditModel(IProductService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }
    [BindProperty] public UpdateProductDto Input { get; set; } = new();
    public string? Error { get; set; }
    public async Task<IActionResult> OnGetAsync(Guid id) {
        ViewData["Title"]="Edit Product";
        var p = await _svc.GetByIdAsync(id);
        if (p==null) return RedirectToPage("/Products/Index");
        Input = new UpdateProductDto {
            Id=p.Id, Name=p.Name, SKU=p.SKU, UnitPrice=p.UnitPrice, PurchasePrice=p.PurchasePrice, Unit=p.Unit,
            Category=p.Category, DefaultWarrantyMonths=p.DefaultWarrantyMonths,
            LowStockThreshold=p.LowStockThreshold, IsActive=p.IsActive
        };
        return Page();
    }
    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"]="Edit Product";
        if (!ModelState.IsValid) { Error=_loc["Please fill in every required field with a valid value."]; return Page(); }
        var r = await _svc.UpdateAsync(Input);
        if (!r.Success) { Error=r.Error; return Page(); }
        return RedirectToPage("/Products/Index", new { msg=_loc["Product updated."].Value });
    }
}
