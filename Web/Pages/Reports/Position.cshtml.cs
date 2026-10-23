using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
namespace SimpleERP.Web.Pages.Reports;
public class PositionModel : PageModel {
    private readonly IFinancialReportService _svc;
    private readonly IPurchaseService _purchases;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> L;
    public PositionModel(IFinancialReportService svc, IPurchaseService purchases, IAppSettingsService settings,
        IStringLocalizer<SharedResource> loc) { _svc = svc; _purchases = purchases; _settings = settings; L = loc; }
    public int PendingChecks { get; set; }
    public PositionSummaryDto Report { get; set; } = null!;
    public async Task OnGetAsync() {
        PendingChecks = (await _purchases.GetNeedingReviewAsync()).Count;
        ViewData["Title"] = "Position Summary";
        // No date range: this is "where things stand now", read from current balances.
        Report = await _svc.GetPositionSummaryAsync();
    }

    /// <summary>
    /// The summary as on screen, one line per row, with both ageings (HC, 2026-10-21). Amounts
    /// only noted, never subtracted on screen (open notes, in-kind rebates) stay in the notes column.
    /// </summary>
    public async Task<IActionResult> OnGetExcelAsync() {
        var d = await _svc.GetPositionSummaryAsync();
        var rows = new List<object?[]>();
        void Line(string label, decimal? amount, string? note = null) => rows.Add(new object?[] { label, amount, note });
        void Ageing(AgingBucketsDto a) {
            Line("      " + L["Not yet due"], a.NotYetDue);
            Line("      1–30", a.Days1To30);
            Line("      31–60", a.Days31To60);
            Line("      61–90", a.Days61To90);
            Line("      90+", a.Days90Plus);
            if (a.NoDueDate != 0) Line("      " + L["No due date"], a.NoDueDate);
        }

        Line("Aset", null);
        Line("   Persediaan", d.InventoryValue, L["{0} products in stock", d.ProductsInStock]);
        Line("   Piutang Usaha (incl. PPN)", d.ReceivablesGross, L["{0} open invoices", d.OpenInvoices]);
        if (d.CreditNotesApplied > 0) Line("   (−) Nota Kredit diterapkan", d.CreditNotesApplied, L["Credit notes applied to specific invoices"]);
        Line("   Piutang Bersih", d.ReceivablesNet,
            d.OpenCreditNotes > 0 ? L["Plus {0} in open credit notes not yet applied to any invoice", d.OpenCreditNotes.ToString("N0")] : null);
        Ageing(d.ReceivablesAging);
        Line("   Rebat belum diklaim", d.UnclaimedRebate, L["{0} cash accruals", d.UnclaimedRebateCount]
            + (d.RebateInKindOutstanding > 0 || d.RebateLuckyDrawOutstanding > 0
                ? "; " + L["plus {0} in-kind and {1} lucky-draw accrual(s) outstanding, carried without a cash value until settled",
                           d.RebateInKindOutstanding, d.RebateLuckyDrawOutstanding] : ""));
        Line("Total Aset Lancar", d.TotalAssets);
        Line("Kewajiban", null);
        Line("   Utang Usaha (incl. PPN)", d.PayablesGross, L["{0} open purchases", d.OpenPurchases]);
        if (d.DebitNotesApplied > 0) Line("   (−) Nota Debit diterapkan", d.DebitNotesApplied, L["Debit notes applied to specific purchases"]);
        Line("   Utang Bersih", d.PayablesNet);
        Ageing(d.PayablesAging);
        Line("   Komisi terutang", d.CommissionPayable, L["{0} unpaid accruals", d.CommissionPayableCount]);
        Line("Total Kewajiban", d.TotalLiabilities);

        var cols = new List<XlColumn> { new("", Width: 36), new(L["Amount"], XlKind.Number, 18), new(L["Notes"], Width: 60) };
        var asOf = d.AsOf.ToLocalTime();
        return ExcelExport.File($"Ringkasan-Posisi_{asOf:yyyy-MM-dd}", (await _settings.GetAsync()).StoreName, "Ringkasan Posisi",
            L["As of {0}", asOf.ToString("dd MMM yyyy HH:mm")],
            new XlSheet("Ringkasan Posisi", cols, rows, new object?[] { "Posisi Bersih", d.NetPosition }));
    }
}
