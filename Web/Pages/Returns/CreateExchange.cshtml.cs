using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Returns;

/// <summary>
/// Tukar barang (HC, 2026-10-22): swap a faulty unit for the same product, new, against one
/// invoice. The lines are what that invoice sold, each capped at what the customer still holds.
/// Staff and Admin; Staff's wait for Admin's check, like Staff returns.
/// </summary>
public class CreateExchangeModel : PageModel
{
    private readonly IExchangeService _exchanges;
    private readonly IStringLocalizer<SharedResource> _loc;
    public CreateExchangeModel(IExchangeService exchanges, IStringLocalizer<SharedResource> loc) { _exchanges = exchanges; _loc = loc; }

    public ExchangeFormDto Form { get; set; } = null!;

    [BindProperty] public DateTime? ExchangeDate { get; set; }
    [BindProperty] public string    Reason       { get; set; } = "";
    [BindProperty] public string?   Notes        { get; set; }
    [BindProperty] public List<ReturnLineInput> Lines { get; set; } = new();

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid saleId)
    {
        var form = await _exchanges.GetFormAsync(saleId);
        if (form == null) return RedirectToPage("/Sales/Index");
        Form = form;
        ViewData["Title"] = $"Exchange on {form.InvoiceNumber}";
        ExchangeDate = DateTime.Now.Date;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid saleId)
    {
        var form = await _exchanges.GetFormAsync(saleId);
        if (form == null) return RedirectToPage("/Sales/Index");
        Form = form;
        ViewData["Title"] = $"Exchange on {form.InvoiceNumber}";

        // Rows left blank simply aren't part of this exchange.
        var items = Lines.Where(l => l.Qty > 0)
            .Select(l => new CreateReturnItemDto { SourceItemId = l.SourceItemId, Qty = l.Qty, Notes = l.Notes }).ToList();
        if (items.Count == 0) { Error = _loc["Enter a quantity on at least one line."]; return Page(); }

        var result = await _exchanges.CreateAsync(new CreateExchangeDto {
            SaleId = saleId, ExchangeDate = ExchangeDate, Reason = Reason, Notes = Notes, Items = items
        }, this.CurrentUserName(), User.ChecksStaffWork());
        if (!result.Success) { Error = result.Error; return Page(); }

        var msg = _loc["Exchange {0} recorded: {1} unit(s) swapped. Send the faulty unit back to the supplier (Retur Pembelian) or write it off.",
                       result.Data!.ExchangeNumber, result.Data.Units.ToString("N0")].Value;
        return Redirect($"/Returns/Exchange/{result.Data.Id}?msg={Uri.EscapeDataString(msg)}");
    }
}
