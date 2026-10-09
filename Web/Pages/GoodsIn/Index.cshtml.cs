using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.GoodsIn;

/// <summary>
/// Every purchase, quantities only (HC, 2026-10-09: Staff see all purchases without amounts).
/// Fed by a DTO that has no amount fields at all, so there is nothing to scrub.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IPurchaseService _purchases;
    public IndexModel(IPurchaseService purchases) => _purchases = purchases;

    public List<PurchaseQtyListDto> Rows { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    public string?  Search { get; set; }
    public bool     PendingOnly { get; set; }

    public async Task OnGetAsync(DateTime? from, DateTime? to, string? search, bool pending = false)
    {
        ViewData["Title"] = "Goods In";
        var today = DateTime.Now.Date;
        From = (from ?? today.AddDays(-30)).Date; To = (to ?? today).Date;
        Search = search; PendingOnly = pending;
        Rows = await _purchases.GetQtyListAsync(From, To, search);
        if (pending) Rows = Rows.Where(r => r.NeedsReview && r.Status != "Cancelled").ToList();
    }
}
