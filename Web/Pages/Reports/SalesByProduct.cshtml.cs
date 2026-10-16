using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Reports;

/// <summary>Margin by product (HC, 2026-10-15). Admin only (Program.cs): it shows HPP and margin.</summary>
public class SalesByProductModel : PeriodReportModel
{
    private readonly ISalesAnalysisService _svc;
    public SalesByProductModel(ISalesAnalysisService svc, IAppSettingsService settings, IPurchaseService purchases,
        IStringLocalizer<SharedResource> loc) : base(settings, purchases, loc) => _svc = svc;

    public SalesAnalysisDto Report { get; set; } = null!;

    public async Task OnGetAsync(DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Margin by Product";
        await SetPeriodAsync(from, to);
        Report = await _svc.GetByProductAsync(From, To);
    }

    public async Task<IActionResult> OnGetExcelAsync(DateTime? from, DateTime? to)
    {
        await SetPeriodAsync(from, to);
        var d = await _svc.GetByProductAsync(From, To);
        var cols = new List<XlColumn> {
            new(L["Product"]), new("SKU"), new(L["Category"]), new(L["Unit"]),
            new(L["Invoices"], XlKind.Number), new(L["Qty sold"], XlKind.Number), new(L["Qty returned"], XlKind.Number),
            new(L["Net qty"], XlKind.Number), new("Penjualan (DPP)", XlKind.Number), new("Retur (DPP)", XlKind.Number),
            new("Penjualan Bersih", XlKind.Number), new("HPP Bersih", XlKind.Number), new("Laba Kotor", XlKind.Number),
            new("Margin %", XlKind.Percent), new(L["Share %"], XlKind.Percent) };
        var rows = d.Rows.Select(r => new object?[] { r.Name, r.Code, r.Group, r.Unit, r.InvoiceCount, r.QtySold, r.QtyReturned,
            r.NetQty, r.Sales, r.Returns, r.NetSales, r.NetCogs, r.Margin, r.MarginPercent, r.SharePercent });
        var totals = new object?[] { L["Total"].Value, null, null, null, d.InvoiceCount, d.Rows.Sum(r => r.QtySold),
            d.Rows.Sum(r => r.QtyReturned), d.Rows.Sum(r => r.NetQty), d.Sales, d.Returns, d.NetSales, d.NetCogs, d.Margin,
            d.MarginPercent, d.Rows.Count > 0 ? 100m : 0m };
        return ExcelExport.File(FileStem("Margin-per-Produk"), await StoreNameAsync(), L["Margin by Product"], PeriodLine(),
            new XlSheet(L["Margin by Product"], cols, rows, totals));
    }
}
