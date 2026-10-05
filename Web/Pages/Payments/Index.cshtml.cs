using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Payments;

public class IndexModel : PageModel
{
    private readonly ISaleService _sales;
    public IndexModel(ISaleService s) => _sales = s;

    public List<SaleListDto> Outstanding    { get; set; } = new();
    public decimal           TotalOutstanding { get; set; }
    public int               OverdueCount   { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Due Payments";
        var all = await _sales.GetAllAsync();
        // Net of credit notes applied to each invoice, like /Sales/Due and the ageing
        // report. On the gross BalanceDue an invoice fully covered by a note stayed on this
        // list as owing its whole amount, and the total disagreed with /Sales/Due (2026-10-05).
        Outstanding = all
            .Where(s => s.Status == "Active"
                     && s.NetBalanceDue > 0
                     && s.PaymentType != "Cash")
            .OrderBy(s => s.IsOverdue ? 0 : 1)
            .ThenBy(s => s.DueDate ?? DateTime.MaxValue)
            .ToList();
        TotalOutstanding = Outstanding.Sum(s => s.NetBalanceDue);
        OverdueCount     = Outstanding.Count(s => s.IsOverdue);
    }
}
