using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Payments;

/// <summary>
/// Payments Staff recorded, waiting for Admin to confirm the money arrived (R1, HC 2026-10-16).
/// Admin only: the Payments folder is AdminOnly in Program.cs. Check them against the cash
/// drawer or the bank, then verify (it counts, dated when received) or reject (it never counts).
/// </summary>
public class PendingModel : PageModel
{
    private readonly ISaleService _sales;
    public PendingModel(ISaleService sales) => _sales = sales;

    public List<PendingPaymentDto> Rows { get; set; } = new();
    public string? Msg { get; set; }
    public bool IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Payments to verify";
        Rows = await _sales.GetPendingPaymentsAsync();
        Msg = msg; IsErr = err;
    }

    public async Task<IActionResult> OnPostVerifyAsync(Guid pendingId)
    {
        var r = await _sales.VerifyPaymentAsync(pendingId, User.ChecksStaffWork(), this.CurrentUserName());
        return RedirectToPage(new { msg = r.Success ? "Payment verified." : r.Error, err = !r.Success });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid pendingId, string? rejectReason)
    {
        var r = await _sales.RejectPaymentAsync(pendingId, rejectReason, User.ChecksStaffWork(), this.CurrentUserName());
        return RedirectToPage(new { msg = r.Success ? "Payment rejected." : r.Error, err = !r.Success });
    }
}
