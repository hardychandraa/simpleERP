using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrintController : ControllerBase
{
    private readonly ISaleService             _sales;
    private readonly IAppSettingsRepository   _settings;
    private readonly IAntiforgery             _antiforgery;
    private readonly ILogger<PrintController> _logger;

    public PrintController(ISaleService sales, IAppSettingsRepository settings, IAntiforgery antiforgery,
        ILogger<PrintController> logger)
    { _sales=sales; _settings=settings; _antiforgery=antiforgery; _logger=logger; }

    /// POST /api/print/invoice/{id}
    [HttpPost("invoice/{id:guid}")]
    public async Task<IActionResult> PrintInvoice(Guid id)
    {
        // Validate CSRF token for this state-changing operation
        try { await _antiforgery.ValidateRequestAsync(HttpContext); }
        catch (Exception ex)
        {
            // Was `catch { return Forbid(); }` — the exception discarded, so a rejected
            // token was invisible. It usually means an expired session or a stale page
            // rather than a genuine forgery, and the two are indistinguishable from the
            // client's side without this.
            _logger.LogWarning(ex, "Antiforgery validation failed for print request on sale {SaleId}.", id);

            // StatusCode(403), NOT Forbid(). Forbid() asks the authentication stack to
            // challenge, and this application has no authentication registered at all, so
            // it threw "No authenticationScheme was specified" and turned every rejected
            // token into a 500 with a stack trace. Found by the logging added here — the
            // failure was real before, just silent.
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Your session has expired. Reload the page and try again." });
        }

        var cfg = await _settings.GetAsync();
        if (!cfg.PrinterEnabled)
            return BadRequest(new { error = "Printer not enabled. Go to Settings to enable it." });
        if (string.IsNullOrWhiteSpace(cfg.PrinterName))
            return BadRequest(new { error = "No printer selected. Go to Settings → Printer." });

        var sale = await _sales.GetByIdAsync(id);
        if (sale == null) return NotFound(new { error = "Sale not found." });

        var bytes  = EscpBuilder.BuildInvoice(sale, cfg);
        var result = RawPrinter.Send(cfg.PrinterName, bytes);

        if (!result.Success)
        {
            // A failed print is a failed business action — the invoice exists but the
            // customer has no paper copy. The user sees the message; this records which
            // invoice and which printer, which is what makes a pattern visible later.
            _logger.LogWarning("Printing invoice {InvoiceNumber} to {Printer} failed: {Reason}",
                sale.InvoiceNumber, cfg.PrinterName, result.Error);
            return BadRequest(new { error = result.Error });
        }

        _logger.LogInformation("Invoice {InvoiceNumber} sent to printer {Printer}.",
            sale.InvoiceNumber, cfg.PrinterName);
        return Ok(new { message = $"Sent to printer: {cfg.PrinterName}" });
    }

    /// GET /api/print/printers — read-only, no CSRF needed
    [HttpGet("printers")]
    public IActionResult GetPrinters() => Ok(RawPrinter.GetInstalledPrinters(_logger));
}
