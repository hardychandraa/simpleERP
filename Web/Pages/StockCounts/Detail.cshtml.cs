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
/// A submitted count. Admin sees the system figure and the difference, the documents entered
/// after the count, and settles each difference: post it as an adjustment, or explain it.
/// Staff see what they counted and whether it matched, never the system figure.
/// </summary>
public class DetailModel : PageModel
{
    private readonly IStockCountService _counts;
    private readonly IStringLocalizer<SharedResource> _loc;
    public DetailModel(IStockCountService counts, IStringLocalizer<SharedResource> loc) { _counts = counts; _loc = loc; }

    public StockCountDto Count { get; set; } = null!;
    public bool Reviews { get; set; }
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        Reviews = User.ReviewsStockCounts();
        var c = await _counts.GetAsync(id, withReview: Reviews);
        if (c == null) return RedirectToPage("/StockCounts/Index");
        if (c.Status == StockCountStatus.Draft) return RedirectToPage("/StockCounts/Count", new { id, msg, err });
        if (!Reviews)
            foreach (var l in c.Lines)
            {
                l.SystemQty = null; l.CurrentDifference = null; l.AdjustedQty = null;
                l.EnteredAfterCount = new();
            }
        Count = c; Msg = msg; IsErr = err;
        ViewData["Title"] = c.CountNumber ?? "Stock count";
        return Page();
    }

    public async Task<IActionResult> OnPostDecideAsync(Guid id, Guid lineId, string decision, string? note)
    {
        // Razor Pages ignore [Authorize] on handlers; this page is shared with Staff.
        if (!User.ReviewsStockCounts()) return this.Refuse("settling a stock count difference is Admin-only");
        var r = await _counts.DecideAsync(id, lineId, decision == "adjust", note, isAdmin: true, this.CurrentUserName());
        return RedirectToPage("/StockCounts/Detail", new {
            id, msg = r.Success ? (decision == "adjust" ? _loc["Adjustment posted."].Value : _loc["Settled without adjustment."].Value) : r.Error,
            err = !r.Success });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, string? reason)
    {
        if (!User.ReviewsStockCounts()) return this.Refuse("cancelling a submitted stock count is Admin-only");
        var r = await _counts.CancelAsync(id, reason, isAdmin: true, this.CurrentUserName());
        return RedirectToPage("/StockCounts/Detail", new { id, msg = r.Success ? _loc["Count cancelled."].Value : r.Error, err = !r.Success });
    }
}
