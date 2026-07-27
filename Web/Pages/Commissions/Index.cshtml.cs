using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Commissions;

/// <summary>
/// The commission payout centre: what's owed to each salesperson, a one-click payout
/// that settles all their unpaid accruals, and the payout history.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICommissionService _svc;
    public IndexModel(ICommissionService svc) => _svc = svc;

    public List<CommissionUnpaidDto>  Unpaid  { get; set; } = new();
    public List<CommissionPayoutDto>  History { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Commissions";
        Msg = msg; IsErr = err;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Unpaid  = await _svc.GetUnpaidSummaryAsync();
        History = await _svc.GetPayoutsAsync();
    }

    public async Task<IActionResult> OnPostPayoutAsync(PayoutCommissionDto input)
    {
        var r = await _svc.PayoutAsync(input, User.Identity?.Name ?? "staff");
        return Redirect(r.Success
            ? "/Commissions?msg=Commission+paid+out."
            : $"/Commissions?err=true&msg={Uri.EscapeDataString(r.Error!)}");
    }
}
