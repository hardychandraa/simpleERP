using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// What we owe, per supplier — the AP mirror of Sales/Due. The summary behind it has
/// existed since the Purchase module shipped but was never surfaced anywhere.
/// </summary>
public class DueModel : PageModel
{
    private readonly IPurchaseService _svc;
    public DueModel(IPurchaseService svc) => _svc = svc;

    public List<DueSupplierDto> Summary { get; set; } = new();
    public decimal TotalOutstanding { get; set; }
    public string? Msg { get; set; }

    public async Task OnGetAsync(string? msg)
    {
        ViewData["Title"] = "Payables by Supplier";
        Msg = msg;
        Summary = await _svc.GetDueSummaryAsync();
        TotalOutstanding = Summary.Sum(d => d.TotalDue);
    }
}
