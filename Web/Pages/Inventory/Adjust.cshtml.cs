using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace SimpleERP.Web.Pages.Inventory;
public class AdjustModel : PageModel {
    private readonly IInventoryService _inv;
    private readonly IProductService   _prod;
    public AdjustModel(IInventoryService i, IProductService p) { _inv=i; _prod=p; }
    [BindProperty] public StockAdjustmentDto Input { get; set; } = new();
    public List<SelectListItem> Products { get; set; } = new();
    public decimal CurrentStock { get; set; }
    public string? Error   { get; set; }
    public string? Success { get; set; }

    public async Task OnGetAsync(Guid? productId) {
        ViewData["Title"] = "Stock Adjustment";
        await Load();
        if (productId.HasValue) {
            Input.ProductId  = productId.Value;
            CurrentStock     = await _inv.GetCurrentStockAsync(productId.Value);
            Input.AdjustedQty = CurrentStock;
        }
    }

    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"] = "Stock Adjustment";
        await Load();
        if (!ModelState.IsValid) return Page();
        if (string.IsNullOrWhiteSpace(Input.Reason)) { Error="Reason is required."; return Page(); }
        CurrentStock = await _inv.GetCurrentStockAsync(Input.ProductId);
        var r = await _inv.AdjustStockAsync(Input, User.Identity?.Name ?? "staff");
        if (!r.Success) { Error=r.Error; return Page(); }
        Success = $"Stock adjusted successfully. New stock: {Input.AdjustedQty:N0}";
        var prevProduct = Input.ProductId;
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
