using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.Payments;

/// <summary>
/// Every settlement received (STL-R-…), newest first, with the invoices each one paid
/// (HC, 2026-10-21: a settlement was only ever shown in the message right after saving it).
/// Admin only: the Payments folder is AdminOnly in Program.cs. Read-only; a line is reversed
/// from its invoice.
/// </summary>
public class SettlementsModel : PageModel
{
    private readonly ISaleService _sales;
    public SettlementsModel(ISaleService sales) => _sales = sales;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To   { get; set; }

    public List<PaymentBatchDto> Rows { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Settlements";
        Rows = await _sales.GetReceivedSettlementsAsync(From, To);
    }
}
