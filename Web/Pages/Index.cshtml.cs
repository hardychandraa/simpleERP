using SimpleERP.Web.Services;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IProductService   _p;
    private readonly ICustomerService  _c;
    private readonly ISaleService      _s;
    private readonly IInventoryService _inv;
    private readonly IReportService    _reports;
    private readonly IPurchaseService  _purchases;

    public IndexModel(IProductService p, ICustomerService c, ISaleService s, IInventoryService inv,
                      IReportService reports, IPurchaseService purchases)
    { _p = p; _c = c; _s = s; _inv = inv; _reports = reports; _purchases = purchases; }

    /// <summary>Purchases waiting for Admin's check (Admin only; Staff never see this).</summary>
    public int PendingChecks { get; set; }

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
        if (User.SeesSupplierSide()) PendingChecks = (await _purchases.GetNeedingReviewAsync()).Count;
        TotalProducts  = (await _p.GetAllActiveAsync()).Count;
        TotalCustomers = (await _c.GetAllActiveAsync()).Count;

        // Today's figures come from the End of Day report so the two screens can never
        // disagree. This used to compute its own: "today" was the UTC day (07:00-to-07:00
        // local, the bug already fixed in End of Day), and "Cash In" left out the day's
        // collections that End of Day's identically-named figure includes (2026-10-05).
        var eod = await _reports.GetEndOfDayAsync(DateTime.Today);
        TodaySales   = eod.TotalSales;
        TodayRevenue = eod.TotalRevenue;
        TodayCashIn  = eod.TotalCashIn;

        // Outstanding dues, net of credit notes applied to each invoice — the same basis
        // as ageing and the Position report. Gross BalanceDue here counted an invoice
        // fully covered by a note as still owed.
        var allSales = await _s.GetAllAsync();
        var outstanding = allSales
            .Where(s => s.Status == "Active"
                     && s.NetBalanceDue > 0
                     && s.PaymentType != "Cash")
            .ToList();
        OutstandingCount = outstanding.Count;
        TotalOutstanding = outstanding.Sum(s => s.NetBalanceDue);
        OverdueCount     = outstanding.Count(s => s.IsOverdue);

        RecentSales = (await _s.GetAllAsync()).Take(8).ToList();

        LowStock = (await _inv.GetAllStockLevelsAsync())
                    .Where(s => s.IsLow && s.IsActive)
                    .OrderBy(s => s.CurrentStock)
                    .Take(8)
                    .ToList();
    }
}
