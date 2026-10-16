using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Reports;

/// <summary>Sales and margin by customer, with what each owes now (HC, 2026-10-15). Admin only (Program.cs).</summary>
public class SalesByCustomerModel : PeriodReportModel
{
    private readonly ISalesAnalysisService _svc;
    public SalesByCustomerModel(ISalesAnalysisService svc, IAppSettingsService settings, IPurchaseService purchases,
        IStringLocalizer<SharedResource> loc) : base(settings, purchases, loc) => _svc = svc;

    public SalesAnalysisDto Report { get; set; } = null!;

    public async Task OnGetAsync(DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Sales by Customer";
        await SetPeriodAsync(from, to);
        Report = await _svc.GetByCustomerAsync(From, To);
    }

    public async Task<IActionResult> OnGetExcelAsync(DateTime? from, DateTime? to)
    {
        await SetPeriodAsync(from, to);
        var d = await _svc.GetByCustomerAsync(From, To);
        var cols = new List<XlColumn> {
            new(L["Customer"]), new(L["Phone"]), new(L["Invoices"], XlKind.Number),
            new("Penjualan (DPP)", XlKind.Number), new("Retur (DPP)", XlKind.Number), new("Penjualan Bersih", XlKind.Number),
            new("HPP Bersih", XlKind.Number), new("Laba Kotor", XlKind.Number), new("Margin %", XlKind.Percent),
            new(L["Share %"], XlKind.Percent), new(L["Owed now"], XlKind.Number), new(L["Past due"], XlKind.Number) };
        var rows = d.Rows.Select(r => new object?[] { r.Name, r.Code, r.InvoiceCount, r.Sales, r.Returns, r.NetSales,
            r.NetCogs, r.Margin, r.MarginPercent, r.SharePercent, r.OwedNow, r.Overdue });
        var totals = new object?[] { L["Total"].Value, null, d.InvoiceCount, d.Sales, d.Returns, d.NetSales, d.NetCogs,
            d.Margin, d.MarginPercent, d.Rows.Count > 0 ? 100m : 0m, d.OwedNow, d.Overdue };
        return ExcelExport.File(FileStem("Penjualan-per-Pelanggan"), await StoreNameAsync(), L["Sales by Customer"], PeriodLine(),
            new XlSheet(L["Sales by Customer"], cols, rows, totals));
    }
}
