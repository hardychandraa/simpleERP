using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// What we owe, per supplier — the AP mirror of Sales/Due. The summary behind it has
/// existed since the Purchase module shipped but was never surfaced anywhere.
/// </summary>
public class DueModel : PageModel
{
    private readonly IPurchaseService _svc;
    private readonly ISalesAnalysisService _analysis;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> _loc;
    public DueModel(IPurchaseService svc, ISalesAnalysisService analysis, IAppSettingsService settings, IStringLocalizer<SharedResource> loc)
    { _svc = svc; _analysis = analysis; _settings = settings; _loc = loc; }

    public List<DueSupplierDto> Summary { get; set; } = new();
    public decimal TotalOutstanding { get; set; }
    public string? Msg { get; set; }

    public int PendingChecks { get; set; }
    public async Task OnGetAsync(string? msg)
    {
        ViewData["Title"] = "Payables by Supplier";
        Msg = msg;
        Summary = await _svc.GetDueSummaryAsync();
        PendingChecks = (await _svc.GetNeedingReviewAsync()).Count;
        TotalOutstanding = Summary.Sum(d => d.TotalDue);
    }

    /// <summary>Payables as Excel (HC, 2026-10-15): per-supplier ageing, and every open purchase, oldest due first.</summary>
    public async Task<IActionResult> OnGetExcelAsync()
    {
        var L = _loc;
        var summary = await _svc.GetDueSummaryAsync();
        var open = await _analysis.GetOpenPayablesAsync();
        var bySupplier = new XlSheet(L["By supplier"],
            new List<XlColumn> { new(L["Supplier"]), new(L["Phone"]), new(L["Open POs"], XlKind.Number), new(L["Total Owed"], XlKind.Number),
                new(L["Not yet due"], XlKind.Number), new("1–30", XlKind.Number), new("31–60", XlKind.Number), new("61–90", XlKind.Number),
                new("90+", XlKind.Number), new(L["No due date"], XlKind.Number) },
            summary.Select(d => new object?[] { d.SupplierName, d.Phone, d.OpenPurchases, d.TotalDue, d.Aging.NotYetDue,
                d.Aging.Days1To30, d.Aging.Days31To60, d.Aging.Days61To90, d.Aging.Days90Plus, d.Aging.NoDueDate }),
            new object?[] { L["Total"].Value, null, summary.Sum(d => d.OpenPurchases), summary.Sum(d => d.TotalDue),
                summary.Sum(d => d.Aging.NotYetDue), summary.Sum(d => d.Aging.Days1To30), summary.Sum(d => d.Aging.Days31To60),
                summary.Sum(d => d.Aging.Days61To90), summary.Sum(d => d.Aging.Days90Plus), summary.Sum(d => d.Aging.NoDueDate) });
        var byPurchase = new XlSheet(L["By purchase"],
            new List<XlColumn> { new(L["Supplier"]), new(L["Phone"]), new(L["Purchase No."]), new(L["Supplier Invoice No."]),
                new(L["Date"], XlKind.Date), new(L["Due Date"], XlKind.Date), new(L["Days late"], XlKind.Number), new(L["Total"], XlKind.Number),
                new(L["Paid"], XlKind.Number), new(L["Debit notes"], XlKind.Number), new(L["Owed"], XlKind.Number) },
            open.OrderByDescending(o => o.DaysLate).ThenBy(o => o.Party).Select(o => new object?[] { o.Party, o.Phone, o.Number, o.OtherNumber,
                o.Date, o.DueDate, o.DaysLate, o.GrandTotal, o.Paid, o.NotesApplied, o.Owed }),
            new object?[] { L["Total"].Value, null, null, null, null, null, null, open.Sum(o => o.GrandTotal), open.Sum(o => o.Paid),
                open.Sum(o => o.NotesApplied), open.Sum(o => o.Owed) });
        var store = (await _settings.GetAsync()).StoreName;
        return ExcelExport.File($"Hutang_{DateTime.Today:yyyy-MM-dd}", store, L["Payables by Supplier"],
            L["As of {0}", DateTime.Now.ToString("dd MMM yyyy HH:mm")], bySupplier, byPurchase);
    }
}
