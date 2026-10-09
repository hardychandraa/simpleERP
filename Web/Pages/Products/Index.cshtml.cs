using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;
namespace SimpleERP.Web.Pages.Products;
public class IndexModel : PageModel {
    private readonly IProductService _svc;
    private readonly IInventoryService _inv;
    public IndexModel(IProductService svc, IInventoryService inv) { _svc = svc; _inv = inv; }
    public List<ProductDto> Products { get; set; } = new();
    public string? Search   { get; set; }
    public string? Msg      { get; set; }
    public bool    IsErr    { get; set; }
    /// <summary>Products with exactly 0 in stock are hidden unless this is on (HC, 2026-10-07).</summary>
    public bool    ShowEmpty { get; set; }
    public int     HiddenEmpty { get; set; }
    /// <summary>
    /// Stock value per product and in total, Admin only. Stock Levels was folded into this page
    /// (HC, 2026-10-09); these are its figures, from the same query, so they agree with it.
    /// </summary>
    public Dictionary<Guid, decimal> StockValue { get; set; } = new();
    public decimal TotalStockValue { get; set; }
    public async Task OnGetAsync(string? search, string? msg, bool err = false, bool showEmpty = false) {
        ViewData["Title"] = "Products";
        Search = search?.Trim(); Msg = msg; IsErr = err; ShowEmpty = showEmpty;
        var all = await _svc.GetAllAsync();
        if (!string.IsNullOrEmpty(Search)) {
            var s = Search.ToLower();
            all = all.Where(p => p.Name.ToLower().Contains(s) || p.SKU.ToLower().Contains(s)
                              || (p.Category?.ToLower().Contains(s) ?? false)).ToList();
        }
        // Negative stock is never hidden: it would point at a problem.
        if (!ShowEmpty) {
            HiddenEmpty = all.Count(p => p.CurrentStock == 0);
            all = all.Where(p => p.CurrentStock != 0).ToList();
        }
        // Staff see harga jual and stock, never cost (HC, 2026-10-08).
        if (!User.SeesCost())
            foreach (var p in all) { p.AvgCost = 0; p.PurchasePrice = 0; }
        else
        {
            var levels = await _inv.GetAllStockLevelsAsync();
            StockValue = levels.ToDictionary(l => l.ProductId, l => l.StockValue);
            TotalStockValue = levels.Sum(l => l.StockValue);   // the whole stock, not just this filter
        }
        Products = all;
    }
    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, string? search, bool showEmpty = false) {
        if (!User.MaintainsMasterData()) return this.Refuse("product master is Admin-only");
        var r = await _svc.DeactivateAsync(id);
        return RedirectToPage(new { search, showEmpty, msg = r.Success ? "Product deactivated." : r.Error, err = !r.Success });
    }
}
