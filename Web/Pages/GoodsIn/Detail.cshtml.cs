using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.GoodsIn;

/// <summary>One purchase as Staff may see it: what came in, from whom, and whether Admin has checked it. No amounts.</summary>
public class DetailModel : PageModel
{
    private readonly IPurchaseService _purchases;
    public DetailModel(IPurchaseService purchases) => _purchases = purchases;

    public PurchaseQtyDto Purchase { get; set; } = null!;
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        var p = await _purchases.GetQtyDetailAsync(id);
        if (p == null) return RedirectToPage("/GoodsIn/Index");
        Purchase = p; Msg = msg; IsErr = err;
        ViewData["Title"] = p.PurchaseNumber;
        return Page();
    }
}
