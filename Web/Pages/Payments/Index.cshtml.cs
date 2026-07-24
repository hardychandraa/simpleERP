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
        Outstanding = all
            .Where(s => s.Status == "Active"
                     && s.BalanceDue > 0
                     && s.PaymentType != "Cash")
            .OrderBy(s => s.IsOverdue ? 0 : 1)
            .ThenBy(s => s.DueDate ?? DateTime.MaxValue)
            .ToList();
        TotalOutstanding = Outstanding.Sum(s => s.BalanceDue);
        OverdueCount     = Outstanding.Count(s => s.IsOverdue);
    }
}
