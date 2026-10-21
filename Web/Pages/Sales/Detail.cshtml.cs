using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Sales;

public class DetailModel : PageModel
{
    private readonly ISaleService        _sales;
    private readonly ICommissionService  _commissions;
    private readonly IReturnService      _returns;
    private readonly ICreditNoteService  _notes;
    private readonly IAppSettingsService _settings;

    private readonly IAppSettingsRepository _settingsRepo;

    public DetailModel(ISaleService s, ICommissionService commissions,
                       IReturnService returns, ICreditNoteService notes, IAppSettingsService cfg,
                       IAppSettingsRepository settingsRepo)
    { _sales = s; _commissions = commissions; _returns = returns; _notes = notes; _settings = cfg;
      _settingsRepo = settingsRepo; }

    public SaleDto        Sale           { get; set; } = null!;
    public AppSettingsDto AppSettings    { get; set; } = null!;
    public List<PaymentRecordDto> Payments      { get; set; } = new();
    public List<CommissionAccrualDto> Commissions { get; set; } = new();
    public List<ReturnListDto>    Returns       { get; set; } = new();
    /// <summary>Credit notes applied to this invoice, reversed ones included.</summary>
    public List<CreditNoteApplicationDto> NoteApplications { get; set; } = new();
    public decimal        TotalCollected { get; set; }
    public decimal        StillOwed      { get; set; }

    [BindProperty] public RecordPaymentDto PayInput { get; set; } = new();
    /// <summary>Staff may cancel or revise only a sale entered today (G2); Admin any.</summary>
    public bool CanChange { get; set; }
    /// <summary>Staff payments waiting for, or decided by, Admin (R1). Shown to both: it is the Staff member's own entry.</summary>
    public List<PendingPaymentDto> PendingPayments { get; set; } = new();
    public decimal PendingTotal => PendingPayments.Where(p => p.Status == "Pending").Sum(p => p.Amount);
    /// <summary>A Staff request to cancel or revise still waiting for Admin (R3): its reason, or null.</summary>
    public string? ChangeRequest { get; set; }
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        ViewData["Title"] = "Invoice";
        var sale = await _sales.GetByIdAsync(id);
        if (sale == null) return RedirectToPage("/Sales/Index");

        Sale        = sale;
        AppSettings = await _settings.GetAsync();
        Payments    = sale.PaymentHistory;
        // Commission is Admin's (HC, 2026-10-08): not even loaded for Staff.
        if (User.SeesCommission())
            Commissions = await _commissions.GetAccrualsForSaleAsync(id);
        Returns     = await _returns.GetReturnsForSaleAsync(id);
        NoteApplications = await _notes.GetApplicationsForSaleAsync(id);
        TotalCollected = Payments.Where(p => !p.IsReversed).Sum(p => p.Amount);
        // Net of applied credit notes, so the payment form's cap matches what the service
        // will actually accept rather than inviting a refusal.
        StillOwed   = Math.Max(0, sale.NetBalanceDue);
        PayInput.SaleId = id;
        PayInput.ReceivedOn = DateTime.Now.Date;
        // Only Admin cancels or revises (R3, HC 2026-10-16); Staff send a request.
        CanChange = User.ChecksStaffWork();
        PendingPayments = await _sales.GetPendingPaymentsForSaleAsync(id);
        StillOwed = Math.Max(0, StillOwed - PendingTotal);
        ChangeRequest = sale.NeedsReview
            ? SimpleERP.Application.Services.SaleReview.Parse(sale.ReviewReasons)
                .Where(r => r[0] == SimpleERP.Application.Services.SaleReview.ChangeRequest).Select(r => r.Length > 2 ? r[2] : "").FirstOrDefault()
            : null;
        // The flag's reasons can name a product sold below cost: Admin only (G4).
        if (!User.ChecksStaffWork()) sale.ReviewReasons = null;
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostReviewedAsync(Guid id)
    {
        if (!User.ChecksStaffWork()) return this.Refuse("checking a flagged sale is Admin-only");
        var result = await _sales.MarkReviewedAsync(id, isAdmin: true, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Marked as checked." : result.Error, err = !result.Success });
    }

    public async Task<IActionResult> OnPostPaymentAsync(Guid id)
    {
        // The form posts only Amount/Notes; the sale comes from the route. Without this
        // PayInput.SaleId is Guid.Empty on POST and the service rejects it as not found.
        PayInput.SaleId = id;
        var user   = this.CurrentUserName();
        var result = await _sales.RecordPaymentAsync(PayInput, user, User.ChecksStaffWork());
        return RedirectToPage(new {
            id,
            msg = !result.Success ? result.Error
                : result.Data!.Pending ? "Payment recorded. It counts once an administrator has verified it."
                : "Payment recorded successfully.",
            err = !result.Success
        });
    }

    /// <summary>Admin confirms a Staff payment really arrived (R1).</summary>
    public async Task<IActionResult> OnPostVerifyPaymentAsync(Guid id, Guid pendingId)
    {
        if (!User.ChecksStaffWork()) return this.Refuse("verifying a payment is Admin-only");
        var result = await _sales.VerifyPaymentAsync(pendingId, isAdmin: true, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Payment verified." : result.Error, err = !result.Success });
    }

    /// <summary>Admin: the money did not arrive; the payment never counts (R1).</summary>
    public async Task<IActionResult> OnPostRejectPaymentAsync(Guid id, Guid pendingId, string? rejectReason)
    {
        if (!User.ChecksStaffWork()) return this.Refuse("rejecting a payment is Admin-only");
        var result = await _sales.RejectPaymentAsync(pendingId, rejectReason, isAdmin: true, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Payment rejected." : result.Error, err = !result.Success });
    }

    /// <summary>Admin takes back a payment entered wrong (HC, 2026-10-21).</summary>
    public async Task<IActionResult> OnPostReversePaymentAsync(Guid id, Guid paymentId, string? reverseReason)
    {
        if (!User.ChecksStaffWork()) return this.Refuse("reversing a payment is Admin-only");
        // Only a payment on THIS invoice: the service takes any payment id.
        var sale = await _sales.GetByIdAsync(id);
        if (sale == null || !sale.PaymentHistory.Any(p => p.Id == paymentId))
            return this.Refuse("payment does not belong to this invoice");
        var result = await _sales.ReversePaymentAsync(paymentId, reverseReason, isAdmin: true, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Payment reversed. The invoice owes that amount again." : result.Error, err = !result.Success });
    }

    /// <summary>Staff ask Admin to cancel or revise (R3).</summary>
    public async Task<IActionResult> OnPostRequestChangeAsync(Guid id, string? changeReason)
    {
        var result = await _sales.RequestChangeAsync(id, changeReason, this.CurrentUserName());
        return RedirectToPage(new { id, msg = result.Success ? "Request sent to the administrator." : result.Error, err = !result.Success });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, string? reason)
    {
        // Admin only (R3, HC 2026-10-16); the service refuses Staff too.
        if (!User.ChecksStaffWork()) return this.Refuse("cancelling a sale is Admin-only; Staff send a request");
        var user   = this.CurrentUserName();
        var result = await _sales.CancelAsync(id, user, User.ChecksStaffWork(), reason);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Sale cancelled. Stock reversed." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostReverseApplicationAsync(Guid id, Guid applicationId)
    {
        // Only an application on THIS invoice. The service takes any application id, so
        // without this a post here could reverse a debit note's application on a purchase.
        var application = await _notes.GetApplicationAsync(applicationId);
        if (application == null || application.SaleId != id)
            return this.Refuse("application does not belong to this invoice");

        var user   = this.CurrentUserName();
        var result = await _notes.ReverseApplicationAsync(applicationId, user);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Credit note application reversed." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnGetTxtAsync(Guid id)
    {
        // The same pages the dot-matrix printer gets, as plain text: what you see in this
        // file is what lands on the form. Sheets are separated by a form feed, so printing
        // the file to the LX through a text driver still breaks at each perforation.
        var sale = await _sales.GetByIdAsync(id);
        // A cancelled invoice is not printed (HC, 2026-10-09); the page hides the button.
        if (sale == null || sale.Status != "Active") return NotFound();
        var cfg   = await _settingsRepo.GetAsync();
        var pages = EscpBuilder.RenderPages(sale, cfg, this.CurrentUserName(), DateTime.Now);
        var txt   = string.Join("\f\r\n", pages.Select(p => string.Join("\r\n", p.Select(l => l.Text.TrimEnd())) + "\r\n"));
        return File(System.Text.Encoding.ASCII.GetBytes(txt), "text/plain", $"{sale.InvoiceNumber}.txt");
    }
}
