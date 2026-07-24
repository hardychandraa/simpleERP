using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IProductService   _p;
    private readonly ICustomerService  _c;
    private readonly ISaleService      _s;
    private readonly IInventoryService _inv;

    public IndexModel(IProductService p, ICustomerService c, ISaleService s, IInventoryService inv)
    { _p = p; _c = c; _s = s; _inv = inv; }

    public int     TotalProducts     { get; set; }
    public int     TotalCustomers    { get; set; }
    public int     TodaySales        { get; set; }
    public decimal TodayRevenue      { get; set; }
    public decimal TodayCashIn       { get; set; }
    public decimal TotalOutstanding  { get; set; }
    public int     OutstandingCount  { get; set; }
    public int     OverdueCount      { get; set; }

    public List<Application.DTOs.SaleListDto>   RecentSales { get; set; } = new();
    public List<Application.DTOs.StockLevelDto> LowStock    { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Dashboard";
        TotalProducts  = (await _p.GetAllActiveAsync()).Count;
        TotalCustomers = (await _c.GetAllActiveAsync()).Count;

        var today      = DateTime.UtcNow.Date;
        var todaySales = (await _s.GetAllAsync(today, today.AddDays(1)))
                         .Where(s => s.Status == "Active").ToList();
        TodaySales   = todaySales.Count;
        TodayRevenue = todaySales.Sum(s => s.GrandTotal);
        TodayCashIn  = todaySales.Where(s => s.PaymentType == "Cash").Sum(s => s.GrandTotal);

        // Outstanding dues: all active non-cash sales with balance
        var allSales = await _s.GetAllAsync();
        var outstanding = allSales
            .Where(s => s.Status == "Active"
                     && s.BalanceDue > 0
                     && s.PaymentType != "Cash")
            .ToList();
        OutstandingCount = outstanding.Count;
        TotalOutstanding = outstanding.Sum(s => s.BalanceDue);
        OverdueCount     = outstanding.Count(s => s.IsOverdue);

        RecentSales = (await _s.GetAllAsync()).Take(8).ToList();

        LowStock = (await _inv.GetAllStockLevelsAsync())
                    .Where(s => s.IsLow)
                    .OrderBy(s => s.CurrentStock)
                    .Take(8)
                    .ToList();
    }
}
