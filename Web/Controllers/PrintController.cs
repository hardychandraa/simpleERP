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

    public PrintController(ISaleService sales, IAppSettingsRepository settings, IAntiforgery antiforgery)
    { _sales=sales; _settings=settings; _antiforgery=antiforgery; }

    /// POST /api/print/invoice/{id}
    [HttpPost("invoice/{id:guid}")]
    public async Task<IActionResult> PrintInvoice(Guid id)
    {
        // Validate CSRF token for this state-changing operation
        try { await _antiforgery.ValidateRequestAsync(HttpContext); }
        catch { return Forbid(); }

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
            return BadRequest(new { error = result.Error });

        return Ok(new { message = $"Sent to printer: {cfg.PrinterName}" });
    }

    /// GET /api/print/printers — read-only, no CSRF needed
    [HttpGet("printers")]
    public IActionResult GetPrinters() => Ok(RawPrinter.GetInstalledPrinters());
}
