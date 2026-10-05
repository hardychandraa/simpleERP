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
        Commissions = await _commissions.GetAccrualsForSaleAsync(id);
        Returns     = await _returns.GetReturnsForSaleAsync(id);
        NoteApplications = await _notes.GetApplicationsForSaleAsync(id);
        TotalCollected = Payments.Sum(p => p.Amount);
        // Net of applied credit notes, so the payment form's cap matches what the service
        // will actually accept rather than inviting a refusal.
        StillOwed   = Math.Max(0, sale.NetBalanceDue);
        PayInput.SaleId = id;
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostPaymentAsync(Guid id)
    {
        // The form posts only Amount/Notes; the sale comes from the route. Without this
        // PayInput.SaleId is Guid.Empty on POST and the service rejects it as not found.
        PayInput.SaleId = id;
        var user   = this.CurrentUserName();
        var result = await _sales.RecordPaymentAsync(PayInput, user);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Payment recorded successfully." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var user   = this.CurrentUserName();
        var result = await _sales.CancelAsync(id, user);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Sale cancelled. Stock reversed." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostReverseApplicationAsync(Guid id, Guid applicationId)
    {
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
        if (sale == null) return NotFound();
        var cfg   = await _settingsRepo.GetAsync();
        var pages = EscpBuilder.RenderPages(sale, cfg, this.CurrentUserName(), DateTime.Now);
        var txt   = string.Join("\f\r\n", pages.Select(p => string.Join("\r\n", p.Select(l => l.Text.TrimEnd())) + "\r\n"));
        return File(System.Text.Encoding.ASCII.GetBytes(txt), "text/plain", $"{sale.InvoiceNumber}.txt");
    }
}
