using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Settings;

/// <summary>
/// Month lock (tutup buku), Admin only through the /Settings folder convention. Separate from
/// the main Settings form so saving that form can never overwrite the lock date. The rule
/// itself lives in PeriodLock; this page only moves the date, and every move is audited.
/// </summary>
public class PeriodModel : PageModel
{
    private readonly IAppSettingsService _settings;
    private readonly IPurchaseService    _purchases;
    private readonly IStringLocalizer<SharedResource> _loc;
    public PeriodModel(IAppSettingsService settings, IPurchaseService purchases, IStringLocalizer<SharedResource> loc)
    { _settings = settings; _purchases = purchases; _loc = loc; }

    public DateTime? ClosedThrough { get; set; }
    /// <summary>Month-ends that can still be closed: after the current lock, up to last month.</summary>
    public List<DateTime> Closable { get; set; } = new();
    /// <summary>Purchases waiting for Admin's check that are dated in an ended month (they block closing it).</summary>
    public List<PurchaseListDto> Pending { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Close the Books";
        Msg = msg; IsErr = err;
        ClosedThrough = (await _settings.GetAsync()).BooksClosedThrough;

        var lastEnded = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddDays(-1);
        // From the month after the lock (or a year back when nothing is closed yet), up to last month.
        var start = ClosedThrough?.AddDays(1) ?? new DateTime(lastEnded.Year, lastEnded.Month, 1).AddMonths(-11);
        for (var m = new DateTime(start.Year, start.Month, 1); m <= lastEnded; m = m.AddMonths(1))
            Closable.Add(m.AddMonths(1).AddDays(-1));
        Closable.Reverse();   // most recent first: closing last month is the usual action

        Pending = (await _purchases.GetNeedingReviewAsync())
            .Where(p => p.PurchaseDate.Date <= lastEnded).ToList();
    }

    public async Task<IActionResult> OnPostCloseAsync(DateTime monthEnd)
    {
        var r = await _settings.SetBooksClosedThroughAsync(monthEnd, this.CurrentUserName());
        return RedirectToPage(new {
            msg = r.Success ? _loc["Books closed through {0}.", monthEnd.ToString("dd MMM yyyy")].Value : r.Error,
            err = !r.Success });
    }

    /// <summary>Reopens the last closed month only: the lock steps back one month.</summary>
    public async Task<IActionResult> OnPostReopenAsync()
    {
        var current = (await _settings.GetAsync()).BooksClosedThrough;
        if (current == null) return RedirectToPage(new { msg = _loc["Nothing is closed."].Value, err = true });
        var previous = new DateTime(current.Value.Year, current.Value.Month, 1).AddDays(-1);
        var r = await _settings.SetBooksClosedThroughAsync(previous, this.CurrentUserName());
        return RedirectToPage(new {
            msg = r.Success ? _loc["{0} reopened.", current.Value.ToString("MMMM yyyy")].Value : r.Error,
            err = !r.Success });
    }
}
