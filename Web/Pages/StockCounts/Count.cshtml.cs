using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Enums;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.StockCounts;

/// <summary>
/// Counting a draft (HC, 2026-10-14). Blind: nothing on this page or its print shows the system
/// stock. The page remembers when it was loaded; on submit, a product that moved since then is
/// sent back to be counted again instead of recording a difference nobody can explain.
/// </summary>
public class CountModel : PageModel
{
    private readonly IStockCountService _counts;
    private readonly IProductService    _products;
    private readonly IStringLocalizer<SharedResource> _loc;
    public CountModel(IStockCountService counts, IProductService products, IStringLocalizer<SharedResource> loc)
    { _counts = counts; _products = products; _loc = loc; }

    public StockCountDto Count { get; set; } = null!;
    public List<(Guid Id, string Name, string SKU)> Addable { get; set; } = new();
    /// <summary>UTC ticks of when this form was rendered.</summary>
    public long OpenedAt { get; set; }
    public HashSet<Guid> Moved { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    [BindProperty] public List<StockCountLineInput> Lines { get; set; } = new();
    [BindProperty] public string? Notes { get; set; }

    private async Task<bool> Load(Guid id)
    {
        var c = await _counts.GetAsync(id, withReview: false);
        if (c == null || c.Status != StockCountStatus.Draft) return false;
        // Blind: the system figure never reaches this page (it is only set on submit anyway).
        foreach (var l in c.Lines) l.SystemQty = null;
        Count = c;
        var have = c.Lines.Select(l => l.ProductId).ToHashSet();
        Addable = (await _products.GetAllActiveAsync()).Where(p => !have.Contains(p.Id))
                  .OrderBy(p => p.Name).Select(p => (p.Id, p.Name, p.SKU)).ToList();
        OpenedAt = DateTime.UtcNow.Ticks;
        ViewData["Title"] = "Stock count";
        return true;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        if (!await Load(id)) return RedirectToPage("/StockCounts/Detail", new { id });
        Msg = msg; IsErr = err;
        return Page();
    }

    private IActionResult Back(Guid id, ServiceResult r, string ok) =>
        RedirectToPage("/StockCounts/Count", new { id, msg = r.Success ? ok : r.Error, err = !r.Success });

    public async Task<IActionResult> OnPostSaveAsync(Guid id) =>
        Back(id, await _counts.SaveDraftAsync(id, Lines, Notes, User.ReviewsStockCounts(), this.CurrentUserName()), _loc["Saved. You can continue later."]);

    public async Task<IActionResult> OnPostAddAsync(Guid id, Guid productId)
    {
        var user = this.CurrentUserName();
        var saved = await _counts.SaveDraftAsync(id, Lines, Notes, User.ReviewsStockCounts(), user);
        if (!saved.Success) return Back(id, saved, "");
        return Back(id, await _counts.AddProductsAsync(id, new[] { productId }, User.ReviewsStockCounts(), user), _loc["Product added."]);
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, Guid lineId)
    {
        var user = this.CurrentUserName();
        var saved = await _counts.SaveDraftAsync(id, Lines, Notes, User.ReviewsStockCounts(), user);
        if (!saved.Success) return Back(id, saved, "");
        return Back(id, await _counts.RemoveLineAsync(id, lineId, User.ReviewsStockCounts(), user), _loc["Product removed from this count."]);
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var r = await _counts.CancelAsync(id, null, User.ReviewsStockCounts(), this.CurrentUserName());
        if (!r.Success) return Back(id, r, "");
        return RedirectToPage("/StockCounts/Index");
    }

    public async Task<IActionResult> OnPostSubmitAsync(Guid id, long openedAt)
    {
        var opened = openedAt > 0 ? new DateTime(openedAt, DateTimeKind.Utc) : DateTime.UtcNow;
        var r = await _counts.SubmitAsync(id, Lines, Notes, opened, User.ReviewsStockCounts(), this.CurrentUserName());
        if (r.Success)
            return RedirectToPage("/StockCounts/Detail", new { id, msg = _loc["Count submitted."].Value });
        // What was typed has been saved; reload with a fresh "opened" time and the moved products marked.
        if (!await Load(id)) return RedirectToPage("/StockCounts/Detail", new { id, msg = r.Error, err = true });
        Moved = r.MovedProductIds.ToHashSet();
        Msg = r.Error; IsErr = true;
        return Page();
    }
}
