using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Sales;

/// <summary>
/// Tanda terima faktur: every invoice issued to one customer in a period, printed for the
/// customer to sign when the invoices are handed over, so they know what to pay. Read-only —
/// nothing is posted from here.
/// </summary>
public class TandaTerimaModel : PageModel
{
    private readonly ISaleService           _sales;
    private readonly ICustomerService       _customers;
    private readonly IAppSettingsService    _settings;
    private readonly IAppSettingsRepository _settingsRepo;

    public TandaTerimaModel(ISaleService sales, ICustomerService customers,
                            IAppSettingsService settings, IAppSettingsRepository settingsRepo)
    { _sales = sales; _customers = customers; _settings = settings; _settingsRepo = settingsRepo; }

    public List<CustomerDto> Customers { get; set; } = new();
    public Guid?    CustomerId { get; set; }
    public DateTime From       { get; set; }
    public DateTime To         { get; set; }
    public InvoiceReceiptDto? Receipt     { get; set; }
    public AppSettingsDto     AppSettings { get; set; } = null!;
    public string? Error { get; set; }
    /// <summary>The shown receipt's query string, shared by the TXT link and the print call.</summary>
    public string Query => Receipt == null ? "" :
        $"customerId={Receipt.CustomerId}&from={Receipt.From:yyyy-MM-dd}&to={Receipt.To:yyyy-MM-dd}";

    public async Task OnGetAsync(Guid? customerId, DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Tanda Terima";
        // Default to the current month so far — the usual collection run.
        var today = DateTime.Today;
        From = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        To   = (to   ?? today).Date;
        CustomerId  = customerId;
        AppSettings = await _settings.GetAsync();

        // Inactive customers stay pickable: they can still owe on invoices from before.
        Customers = (await _customers.GetAllAsync()).OrderBy(c => c.Name).ToList();

        if (customerId is not { } id) return;
        if (From > To) { Error = "The start date is after the end date."; return; }
        Receipt = await _sales.GetInvoiceReceiptAsync(id, From, To);
        if (Receipt == null) Error = "Customer not found.";
    }

    public async Task<IActionResult> OnGetTxtAsync(Guid customerId, DateTime from, DateTime to)
    {
        // The dot-matrix pages as plain text, sheets separated by a form feed — same
        // convention as the invoice TXT.
        if (from > to) return BadRequest();
        var doc = await _sales.GetInvoiceReceiptAsync(customerId, from, to);
        if (doc == null) return NotFound();
        var cfg   = await _settingsRepo.GetAsync();
        var pages = EscpBuilder.RenderReceiptPages(doc, cfg, this.CurrentUserName(), DateTime.Now);
        var txt   = string.Join("\f\r\n", pages.Select(p => string.Join("\r\n", p.Select(l => l.Text.TrimEnd())) + "\r\n"));
        var name  = new string(doc.CustomerName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        return File(System.Text.Encoding.ASCII.GetBytes(txt), "text/plain",
                    $"TandaTerima_{name}_{from:yyyyMMdd}-{to:yyyyMMdd}.txt");
    }
}
