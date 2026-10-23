using SimpleERP.Domain.Interfaces;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Sales;

/// <summary>
/// The invoice for a browser printer (HC, 2026-10-22): the very sheets the LX and the TXT get
/// (EscpBuilder.RenderPages), in a fixed-width font at the form's size, one sheet per page.
/// The page sets no margin, so the browser has nowhere to print its own header and footer
/// (the "localhost" URL, date and page number). A cancelled invoice is not printed.
/// </summary>
public class PrintModel : PageModel
{
    private readonly ISaleService _sales;
    private readonly IAppSettingsRepository _settings;
    public PrintModel(ISaleService sales, IAppSettingsRepository settings) { _sales = sales; _settings = settings; }

    public List<List<EscpBuilder.PrintLine>> Pages { get; set; } = new();
    public string InvoiceNumber { get; set; } = "";
    public int Columns { get; set; }
    public int Lines   { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var sale = await _sales.GetByIdAsync(id);
        if (sale == null || sale.Status != "Active") return NotFound();
        var cfg = await _settings.GetAsync();
        Pages = EscpBuilder.RenderPages(sale, cfg, this.CurrentUserName(), DateTime.Now);
        InvoiceNumber = sale.InvoiceNumber;
        Columns = cfg.PaperColumns; Lines = cfg.PaperLines;
        return Page();
    }
}
