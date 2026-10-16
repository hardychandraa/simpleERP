using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Reports;

/// <summary>
/// PPN Keluaran vs Masukan for a period, for the tax consultant's monthly SPT Masa PPN (HC,
/// 2026-10-15). Admin only (Program.cs). The PPN stored on each document, never recalculated.
/// </summary>
public class PpnModel : PeriodReportModel
{
    private readonly ISalesAnalysisService _svc;
    public PpnModel(ISalesAnalysisService svc, IAppSettingsService settings, IPurchaseService purchases,
        IStringLocalizer<SharedResource> loc) : base(settings, purchases, loc) => _svc = svc;

    public PpnSummaryDto Report { get; set; } = null!;

    public async Task OnGetAsync(DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "PPN Summary";
        await SetPeriodAsync(from, to);
        Report = await _svc.GetPpnSummaryAsync(From, To);
    }

    public async Task<IActionResult> OnGetExcelAsync(DateTime? from, DateTime? to)
    {
        await SetPeriodAsync(from, to);
        var d = await _svc.GetPpnSummaryAsync(From, To);
        static decimal Sum(IEnumerable<TaxDocDto> x, Func<TaxDocDto, decimal> f) => x.Sum(f);
        string total = L["Total"];

        var summary = new XlSheet(L["Summary"],
            new List<XlColumn> { new(""), new(L["Documents"], XlKind.Number), new("DPP", XlKind.Number), new("PPN", XlKind.Number) },
            new List<object?[]> {
                new object?[] { $"PPN Keluaran ({L["Sales"]})", d.Sales.Count, Sum(d.Sales, x => x.TaxBase), Sum(d.Sales, x => x.TaxAmount) },
                new object?[] { L["less sales returns"].Value, d.SalesReturns.Count, -Sum(d.SalesReturns, x => x.TaxBase), -Sum(d.SalesReturns, x => x.TaxAmount) },
                new object?[] { $"PPN Keluaran {L["Net"]}", null, Sum(d.Sales, x => x.TaxBase) - Sum(d.SalesReturns, x => x.TaxBase), d.OutputTax },
                new object?[] { $"PPN Masukan ({L["Purchases"]})", d.Purchases.Count, Sum(d.Purchases, x => x.TaxBase), Sum(d.Purchases, x => x.TaxAmount) },
                new object?[] { L["less purchase returns"].Value, d.PurchaseReturns.Count, -Sum(d.PurchaseReturns, x => x.TaxBase), -Sum(d.PurchaseReturns, x => x.TaxAmount) },
                new object?[] { $"PPN Masukan {L["Net"]}", null, Sum(d.Purchases, x => x.TaxBase) - Sum(d.PurchaseReturns, x => x.TaxBase), d.InputTax },
            },
            new object?[] { (d.Difference >= 0 ? L["Keluaran minus Masukan (to pay)"] : L["Keluaran minus Masukan (overpaid)"]).Value, null, null, d.Difference });

        var sales = new XlSheet("PPN Keluaran",
            new List<XlColumn> { new(L["Date"], XlKind.Date), new(L["Invoice No."]), new(L["Customer"]), new("NPWP"), new("NIK"),
                new("DPP", XlKind.Number), new("PPN", XlKind.Number), new(L["Total"], XlKind.Number) },
            d.Sales.Select(x => new object?[] { x.Date, x.Number, x.Party, x.TaxId, x.NationalId, x.TaxBase, x.TaxAmount, x.GrandTotal }),
            new object?[] { total, null, null, null, null, Sum(d.Sales, x => x.TaxBase), Sum(d.Sales, x => x.TaxAmount), Sum(d.Sales, x => x.GrandTotal) });

        var salesReturns = new XlSheet(L["Sales Returns"],
            new List<XlColumn> { new(L["Date"], XlKind.Date), new(L["Return No."]), new(L["Invoice No."]), new(L["Customer"]), new("NPWP"), new("NIK"),
                new("DPP", XlKind.Number), new("PPN", XlKind.Number), new(L["Total"], XlKind.Number) },
            d.SalesReturns.Select(x => new object?[] { x.Date, x.Number, x.OtherNumber, x.Party, x.TaxId, x.NationalId, x.TaxBase, x.TaxAmount, x.GrandTotal }),
            new object?[] { total, null, null, null, null, null, Sum(d.SalesReturns, x => x.TaxBase), Sum(d.SalesReturns, x => x.TaxAmount), Sum(d.SalesReturns, x => x.GrandTotal) });

        var purchases = new XlSheet("PPN Masukan",
            new List<XlColumn> { new(L["Date"], XlKind.Date), new(L["Purchase No."]), new(L["Supplier Invoice No."]), new(L["Supplier"]), new("NPWP"),
                new("DPP", XlKind.Number), new("PPN", XlKind.Number), new(L["Total"], XlKind.Number), new(L["Not yet checked"]) },
            d.Purchases.Select(x => new object?[] { x.Date, x.Number, x.OtherNumber, x.Party, x.TaxId, x.TaxBase, x.TaxAmount, x.GrandTotal,
                x.Unchecked ? L["Yes"].Value : "" }),
            new object?[] { total, null, null, null, null, Sum(d.Purchases, x => x.TaxBase), Sum(d.Purchases, x => x.TaxAmount), Sum(d.Purchases, x => x.GrandTotal), null });

        var purchaseReturns = new XlSheet(L["Purchase Returns"],
            new List<XlColumn> { new(L["Date"], XlKind.Date), new(L["Return No."]), new(L["Supplier Invoice No."]), new(L["Supplier"]), new("NPWP"),
                new("DPP", XlKind.Number), new("PPN", XlKind.Number), new(L["Total"], XlKind.Number) },
            d.PurchaseReturns.Select(x => new object?[] { x.Date, x.Number, x.OtherNumber, x.Party, x.TaxId, x.TaxBase, x.TaxAmount, x.GrandTotal }),
            new object?[] { total, null, null, null, null, Sum(d.PurchaseReturns, x => x.TaxBase), Sum(d.PurchaseReturns, x => x.TaxAmount), Sum(d.PurchaseReturns, x => x.GrandTotal) });

        return ExcelExport.File(FileStem("Rekap-PPN"), await StoreNameAsync(), L["PPN Summary"], PeriodLine(),
            summary, sales, salesReturns, purchases, purchaseReturns);
    }
}
