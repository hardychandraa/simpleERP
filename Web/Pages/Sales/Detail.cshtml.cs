using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Sales;

public class DetailModel : PageModel
{
    private readonly ISaleService        _sales;
    private readonly IAppSettingsService _settings;

    public DetailModel(ISaleService s, IAppSettingsService cfg)
    { _sales = s; _settings = cfg; }

    public SaleDto        Sale           { get; set; } = null!;
    public AppSettingsDto AppSettings    { get; set; } = null!;
    public List<PaymentRecordDto> Payments      { get; set; } = new();
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
        TotalCollected = Payments.Sum(p => p.Amount);
        StillOwed   = Math.Max(0, sale.GrandTotal - sale.AmountPaid);
        PayInput.SaleId = id;
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostPaymentAsync(Guid id)
    {
        // The form posts only Amount/Notes; the sale comes from the route. Without this
        // PayInput.SaleId is Guid.Empty on POST and the service rejects it as not found.
        PayInput.SaleId = id;
        var user   = User.Identity?.Name ?? "staff";
        var result = await _sales.RecordPaymentAsync(PayInput, user);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Payment recorded successfully." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var user   = User.Identity?.Name ?? "staff";
        var result = await _sales.CancelAsync(id, user);
        return RedirectToPage(new {
            id,
            msg = result.Success ? "Sale cancelled. Stock reversed." : result.Error,
            err = !result.Success
        });
    }

    public async Task<IActionResult> OnGetTxtAsync(Guid id)
    {
        var txt   = await _sales.GenerateTxtInvoiceAsync(id);
        var bytes = System.Text.Encoding.UTF8.GetBytes(txt);
        return File(bytes, "text/plain", $"invoice-{id}.txt");
    }
}
