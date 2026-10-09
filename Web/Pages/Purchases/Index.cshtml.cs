using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Purchases;

public class IndexModel : PageModel
{
    private readonly IPurchaseService _svc;
    public IndexModel(IPurchaseService svc) => _svc = svc;

    public List<PurchaseListDto> Purchases { get; set; } = new();
    public DateTime? From   { get; set; }
    public DateTime? To     { get; set; }
    public string?   Search { get; set; }
    public string?   Msg    { get; set; }
    public bool      IsErr  { get; set; }
    /// <summary>Only purchases still flagged Perlu dicek (the Admin's checking worklist), any date.</summary>
    public bool      PendingOnly  { get; set; }
    public int       PendingCount { get; set; }

    public async Task OnGetAsync(DateTime? from, DateTime? to, string? search, string? msg, bool err = false, bool pending = false)
    {
        ViewData["Title"] = "Purchases";
        From = from; To = to; Search = search; Msg = msg; IsErr = err; PendingOnly = pending;
        var waiting = await _svc.GetNeedingReviewAsync();
        PendingCount = waiting.Count;
        if (pending) { Purchases = waiting; return; }
        // Include the whole 'to' day by pushing the upper bound to its end.
        var toExclusive = to?.Date.AddDays(1).AddTicks(-1);
        Purchases = await _svc.GetAllAsync(from?.Date, toExclusive, search);
    }
}
