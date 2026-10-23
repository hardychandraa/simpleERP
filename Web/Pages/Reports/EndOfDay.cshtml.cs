using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
namespace SimpleERP.Web.Pages.Reports;
public class EndOfDayModel : PageModel {
    private readonly IReportService _svc;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> L;
    public EndOfDayModel(IReportService svc, IAppSettingsService settings, IStringLocalizer<SharedResource> loc)
    { _svc = svc; _settings = settings; L = loc; }
    public EndOfDayDto Report { get; set; } = null!;
    public DateTime SelectedDate { get; set; }
    public async Task OnGetAsync(DateTime? date) {
        ViewData["Title"] = "End of Day";
        SelectedDate = date?.Date ?? DateTime.Today;
        Report = await _svc.GetEndOfDayAsync(SelectedDate);
    }

    /// <summary>The day as on screen (HC, 2026-10-21): the summary, the sales and the payments, one sheet each.</summary>
    public async Task<IActionResult> OnGetExcelAsync(DateTime? date) {
        var day = date?.Date ?? DateTime.Today;
        var d = await _svc.GetEndOfDayAsync(day);
        var amount = new List<XlColumn> { new("", Width: 40), new(L["Amount"], XlKind.Number, 18) };
        var summary = new List<object?[]> {
            new object?[] { L["Total Sales"].Value, d.TotalSales },
            new object?[] { L["Cash Sales"].Value, d.CashSales },
            new object?[] { L["Due Sales"].Value, d.DueSales },
            new object?[] { L["Cancelled"].Value, d.CancelledSales },
            new object?[] { L["Cash sales revenue"].Value, d.CashRevenue },
            new object?[] { L["Due sales revenue (invoiced)"].Value, d.DueRevenue },
            new object?[] { L["Total Revenue"].Value, d.TotalRevenue },
            new object?[] { L["Payments Collected Today"].Value, d.PaymentsCollected },
        };
        if (d.NotesNetted > 0) summary.Add(new object?[] { "(−) " + L["Settled by credit notes (no cash)"], d.NotesNetted });
        summary.Add(new object?[] { L["All-time Outstanding Due"].Value, d.OutstandingDueTotal });

        var sales = new XlSheet(L["Sales Today"],
            new List<XlColumn> { new(L["Invoice"]), new(L["Customer"]), new(L["Payment"]), new(L["Total"], XlKind.Number),
                new(L["Paid"], XlKind.Number), new(L["Balance"], XlKind.Number) },
            d.SalesList.Select(s => new object?[] { s.InvoiceNumber, s.CustomerName, L[EnumDisplay.Key("PaymentType", s.PaymentType)].Value,
                s.GrandTotal, s.AmountPaid + s.AppliedNotesTotal, s.NetBalanceDue }),
            new object?[] { L["Total"].Value, null, null, d.TotalRevenue, d.SalesList.Sum(s => s.AmountPaid + s.AppliedNotesTotal),
                d.SalesList.Sum(s => s.NetBalanceDue) });
        var payments = new XlSheet(L["Payments Collected Today"],
            new List<XlColumn> { new(L["Invoice"]), new(L["Customer"]), new(L["Amount"], XlKind.Number) },
            d.PaymentsList.Select(p => new object?[] { p.InvoiceNumber, p.CustomerName, p.Amount }),
            new object?[] { L["Total"].Value, null, d.PaymentsList.Sum(p => p.Amount) });

        return ExcelExport.File($"Tutup-Hari_{day:yyyy-MM-dd}", (await _settings.GetAsync()).StoreName, L["End of Day Report"],
            day.ToString("dddd, dd MMMM yyyy"),
            new XlSheet(L["Summary"], amount, summary, new object?[] { L["Total Cash In Hand"].Value, d.TotalCashIn }), sales, payments);
    }
}
