using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.StockCounts;

/// <summary>
/// Stock counts (stock opname, HC 2026-10-14): the history, and starting a new count. Staff and
/// Admin both count; only Admin settles differences (on the detail page).
/// </summary>
public class IndexModel : PageModel
{
    private readonly IStockCountService _counts;
    private readonly IProductService    _products;
    public IndexModel(IStockCountService counts, IProductService products) { _counts = counts; _products = products; }

    public List<StockCountListDto> Rows { get; set; } = new();
    /// <summary>Name and SKU only: the picker must not show stock (blind count).</summary>
    public List<(Guid Id, string Name, string SKU)> Products { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    public bool WaitingOnly { get; set; }
    public int  Waiting     { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync(DateTime? from, DateTime? to, bool waiting = false)
    {
        ViewData["Title"] = "Stock counts";
        var today = DateTime.Today;
        From = (from ?? today.AddDays(-30)).Date; To = (to ?? today).Date;
        WaitingOnly = waiting;
        Rows = waiting
            ? await _counts.GetListAsync(null, null, StockCountStatus.Submitted)
            : await _counts.GetListAsync(From, To, null);
        Waiting  = await _counts.CountAwaitingReviewAsync();
        Products = (await _products.GetAllActiveAsync()).OrderBy(p => p.Name).Select(p => (p.Id, p.Name, p.SKU)).ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync(StockCountScope scope, List<Guid>? productIds)
    {
        if (!Enum.IsDefined(scope)) scope = StockCountScope.MovedToday;
        var r = await _counts.CreateAsync(scope, productIds, this.CurrentUserName());
        if (!r.Success)
        {
            await OnGetAsync(null, null);
            Error = r.Error;
            return Page();
        }
        return RedirectToPage("/StockCounts/Count", new { id = r.Data });
    }
}
