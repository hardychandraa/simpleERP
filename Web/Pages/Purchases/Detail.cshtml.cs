using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Purchases;

public class DetailModel : PageModel
{
    private readonly IPurchaseService    _purchases;
    private readonly IRebateService      _rebates;
    private readonly IReturnService      _returns;
    private readonly ICreditNoteService  _notes;
    private readonly IAppSettingsService _settings;

    public DetailModel(IPurchaseService purchases, IRebateService rebates,
                       IReturnService returns, ICreditNoteService notes, IAppSettingsService settings)
    { _purchases = purchases; _rebates = rebates; _returns = returns; _notes = notes; _settings = settings; }

    public PurchaseDto    Purchase       { get; set; } = null!;
    public AppSettingsDto AppSettings    { get; set; } = null!;
    public List<SupplierPaymentDto> Payments { get; set; } = new();
    public List<RebateAccrualDto>   Rebates  { get; set; } = new();
    public List<ReturnListDto>      Returns  { get; set; } = new();
    /// <summary>Debit notes applied to this purchase, reversed ones included.</summary>
    public List<CreditNoteApplicationDto> NoteApplications { get; set; } = new();
    public decimal        StillOwed      { get; set; }

    [BindProperty] public RecordSupplierPaymentDto PayInput { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        ViewData["Title"] = "Purchase";
        var purchase = await _purchases.GetByIdAsync(id);
        if (purchase == null) return RedirectToPage("/Purchases/Index");

        Purchase    = purchase;
        AppSettings = await _settings.GetAsync();
        Payments    = purchase.PaymentHistory;
        Rebates     = await _rebates.GetAccrualsForPurchaseAsync(id);
        Returns     = await _returns.GetReturnsForPurchaseAsync(id);
        NoteApplications = await _notes.GetApplicationsForPurchaseAsync(id);
        // Net of applied debit notes, so the payment form's cap matches what the service
        // will actually accept rather than inviting a refusal.
        StillOwed   = Math.Max(0, purchase.NetBalanceDue);
        PayInput.PurchaseId = id;
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostPaymentAsync(Guid id)
    {
        // The form only posts Amount/Notes; the purchase comes from the route.
        PayInput.PurchaseId = id;
        var result = await _purchases.RecordPaymentAsync(PayInput, this.CurrentUserName());
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Payment to supplier recorded." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await _purchases.CancelAsync(id, this.CurrentUserName());
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Purchase cancelled. Received stock reversed." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostReverseApplicationAsync(Guid id, Guid applicationId)
    {
        var result = await _notes.ReverseApplicationAsync(applicationId, this.CurrentUserName());
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Debit note application reversed." : result.Error,
            err = !result.Success
        });
    }
}
