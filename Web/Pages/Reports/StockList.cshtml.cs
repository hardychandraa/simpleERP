using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Reports;

/// <summary>
/// Stock list and slow-moving stock (HC, 2026-10-21): every product with its stock, value, last
/// sale and last Goods In, printable and to Excel. "Not sold for N days" narrows it to the money
/// sitting on the shelf, biggest value first. Admin only (Program.cs): it shows cost.
/// </summary>
public class StockListModel : PageModel
{
    private readonly ISalesAnalysisService _svc;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> L;
    public StockListModel(ISalesAnalysisService svc, IAppSettingsService settings, IStringLocalizer<SharedResource> loc)
    { _svc = svc; _settings = settings; L = loc; }

    /// <summary>The choices for "not sold for": 0 = the whole list.</summary>
    public static readonly int[] IdleChoices = { 0, 30, 60, 90 };

    public StockListDto Report { get; set; } = null!;
    public List<StockListRowDto> Rows { get; set; } = new();
    public int  Idle      { get; set; }
    public bool ShowEmpty { get; set; }

    public async Task OnGetAsync(int idle = 0, bool showEmpty = false)
    {
        ViewData["Title"] = "Stock List";
        Report = await _svc.GetStockListAsync();
        Rows = Filter(Report, idle, showEmpty);
    }

    public async Task<IActionResult> OnGetExcelAsync(int idle = 0, bool showEmpty = false)
    {
        var d = await _svc.GetStockListAsync();
        var rows = Filter(d, idle, showEmpty);
        var cols = new List<XlColumn> {
            new(L["Product"]), new("SKU"), new(L["Category"]), new(L["Unit"]), new(L["Stock"], XlKind.Number),
            new(L["Avg Cost"], XlKind.Number), new(L["Stock Value"], XlKind.Number), new(L["Price"], XlKind.Number),
            new(L["Last sale"], XlKind.Date), new(L["Last Goods In"], XlKind.Date), new(L["Days unsold"], XlKind.Number),
            new(L["Sold 30 days"], XlKind.Number), new(L["Sold 90 days"], XlKind.Number), new(L["Status"]) };
        var data = rows.Select(r => new object?[] { r.Name, r.SKU, r.Category, r.Unit, r.Stock, r.AvgCost, r.StockValue,
            r.UnitPrice, r.LastSale, r.LastPurchase, r.IdleDays, r.Sold30, r.Sold90,
            r.IsActive ? L["Active"].Value : L["Inactive"].Value });
        var totals = new object?[] { L["Total"].Value, null, null, null, rows.Sum(r => r.Stock), null,
            rows.Sum(r => r.StockValue), null, null, null, null, rows.Sum(r => r.Sold30), rows.Sum(r => r.Sold90), null };
        var stem = Idle > 0 ? $"Stok-tidak-laku-{Idle}hari" : "Daftar-Stok";
        return ExcelExport.File($"{stem}_{d.AsOf:yyyy-MM-dd}", (await _settings.GetAsync()).StoreName, Heading(),
            L["As of {0}", d.AsOf.ToString("dd MMM yyyy HH:mm")], new XlSheet(Heading(), cols, data, totals));
    }

    /// <summary>The page's and the file's title: the filter is part of what the list is.</summary>
    public string Heading() => Idle > 0 ? L["Not sold for {0}+ days", Idle] : L["Stock List"];

    public string ExcelUrl => $"{Request.Path}?handler=Excel&idle={Idle}&showEmpty={(ShowEmpty ? "true" : "false")}";

    /// <summary>
    /// 0 stock is hidden unless asked for (as on Products); negative stock is never hidden. With a
    /// "not sold for", only stock on hand that has sat that long (never moved counts as long).
    /// </summary>
    private List<StockListRowDto> Filter(StockListDto d, int idle, bool showEmpty)
    {
        Idle = IdleChoices.Contains(idle) ? idle : 0;
        ShowEmpty = showEmpty && Idle == 0;
        return d.Rows.Where(r => Idle > 0 ? r.Stock > 0 && (r.IdleDays ?? int.MaxValue) >= Idle
                                          : ShowEmpty || r.Stock != 0).ToList();
    }
}
