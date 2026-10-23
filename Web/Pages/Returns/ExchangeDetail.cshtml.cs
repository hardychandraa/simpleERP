using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Returns;

/// <summary>One Tukar barang (HC, 2026-10-22). Staff open and cancel it, like a return; only Admin checks it.</summary>
public class ExchangeDetailModel : PageModel
{
    private readonly IExchangeService _exchanges;
    public ExchangeDetailModel(IExchangeService exchanges) => _exchanges = exchanges;

    public ExchangeDto Exchange { get; set; } = null!;
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        var x = await _exchanges.GetAsync(id);
        if (x == null) return RedirectToPage("/Returns/Index");
        Exchange = x;
        ViewData["Title"] = $"Exchange {x.ExchangeNumber}";
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostReviewedAsync(Guid id)
    {
        if (!User.ChecksStaffWork()) return this.Refuse("checking an exchange is Admin-only");
        var result = await _exchanges.MarkReviewedAsync(id, isAdmin: true, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Exchange checked." : result.Error, err = !result.Success });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await _exchanges.CancelAsync(id, this.CurrentUserName());
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Exchange cancelled. The replacement is back in stock and the faulty unit taken out." : result.Error,
            err = !result.Success
        });
    }
}
