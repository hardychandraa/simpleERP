using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SimpleERP.Web.Services;
namespace SimpleERP.Web.Pages.Inventory;
public class AdjustModel : PageModel {
    private readonly IInventoryService _inv;
    private readonly IProductService   _prod;
    private readonly IStringLocalizer<SharedResource> _loc;
    public AdjustModel(IInventoryService i, IProductService p, IStringLocalizer<SharedResource> loc) { _inv=i; _prod=p;  _loc = loc; }
    [BindProperty] public StockAdjustmentDto Input { get; set; } = new();
    public List<SelectListItem> Products { get; set; } = new();
    public decimal CurrentStock { get; set; }
    public string? Error   { get; set; }
    public string? Success { get; set; }

    public async Task OnGetAsync(Guid? productId) {
        ViewData["Title"] = "Stock Adjustment";
        await Load();
        if (productId.HasValue) {
            Input.ProductId = productId.Value;
            CurrentStock    = await _inv.GetCurrentStockAsync(productId.Value);
            Input.QtyActual = CurrentStock;
        }
    }

    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"] = "Stock Adjustment";
        await Load();
        if (!ModelState.IsValid) return Page();
        if (string.IsNullOrWhiteSpace(Input.Reason)) { Error=_loc["Reason is required."]; return Page(); }
        CurrentStock = await _inv.GetCurrentStockAsync(Input.ProductId);
        var r = await _inv.AdjustStockAsync(Input, this.CurrentUserName());
        if (!r.Success) { Error=r.Error; return Page(); }
        Success = _loc["Stock adjusted. New stock: {0}", Input.QtyActual.ToString("N0")];
        Input = new StockAdjustmentDto();
        CurrentStock = 0;
        return Page();
    }

    private async Task Load() {
        var list = await _prod.GetAllActiveAsync();
        Products = list.Select(p => new SelectListItem(
            $"{p.Name} ({p.SKU}) — Stock: {p.CurrentStock:N0}", p.Id.ToString())).ToList();
    }
}
