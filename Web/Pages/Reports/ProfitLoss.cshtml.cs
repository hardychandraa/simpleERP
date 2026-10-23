using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
namespace SimpleERP.Web.Pages.Reports;
public class ProfitLossModel : PeriodReportModel {
    private readonly IFinancialReportService _svc;
    public ProfitLossModel(IFinancialReportService svc, IPurchaseService purchases, IAppSettingsService settings,
        IStringLocalizer<SharedResource> loc) : base(settings, purchases, loc) => _svc = svc;
    /// <summary>Set when the whole period lies in closed months: the figures are final.</summary>
    public DateTime? ClosedThrough { get; set; }
    public ProfitAndLossDto Report { get; set; } = null!;
    public async Task OnGetAsync(DateTime? from, DateTime? to) {
        ViewData["Title"] = "Profit & Loss";
        // Default to the current month to date — the period most often checked.
        await SetPeriodAsync(from, to);
        Report = await _svc.GetProfitAndLossAsync(From, To);
        var closed = (await Settings.GetAsync()).BooksClosedThrough;
        if (closed is { } c && To <= c) ClosedThrough = c;
    }

    /// <summary>
    /// The statement as on screen, one line per row (HC, 2026-10-21): the same figures, the same
    /// Indonesian line names and the same signs (a "(−)" line holds the amount taken off), so the
    /// file ties to the printed page line for line.
    /// </summary>
    public async Task<IActionResult> OnGetExcelAsync(DateTime? from, DateTime? to) {
        await SetPeriodAsync(from, to);
        var d = await _svc.GetProfitAndLossAsync(From, To);
        var rows = new List<object?[]>();
        void Line(string label, decimal? amount, string? note = null) => rows.Add(new object?[] { label, amount, note });

        Line("Penjualan (excl. PPN)", d.Revenue, L["{0} invoices", d.InvoiceCount]);
        if (d.SalesReturnCount > 0) {
            Line("   (−) Retur Penjualan", d.SalesReturns, $"×{d.SalesReturnCount}");
            Line("Penjualan Bersih", d.NetRevenue);
        }
        Line("(−) Harga Pokok Penjualan", d.Cogs);
        if (d.SalesReturnCogs != 0) {
            Line("   (+) HPP atas Retur", d.SalesReturnCogs);
            Line("HPP Bersih", d.NetCogs);
        }
        Line("Laba Kotor", d.GrossProfit, $"Margin {d.GrossMarginPct:0.00}%");
        Line("Biaya Usaha", null);
        foreach (var e in d.ExpenseLines)
            Line("   " + e.CategoryName, e.Amount, $"×{e.EntryCount}" + (e.IsTaxDeductible ? "" : " · " + L["non-ded."]));
        Line("(−) Total Biaya Usaha", d.OperatingExpenses);
        Line("(−) Komisi Penjualan", d.CommissionExpense, d.CommissionAccrualCount > 0 ? $"×{d.CommissionAccrualCount}" : null);
        Line("Laba Usaha", d.OperatingProfit,
            d.NonDeductibleExpenses > 0 ? L["of which non-deductible (fiscal add-back)"] + " " + d.NonDeductibleExpenses.ToString("N0") : null);
        Line("Pendapatan / Beban Luar Usaha", null);
        Line("   (+) Pendapatan Rebat", d.RebateIncome, d.RebateAccrualCount > 0 ? $"×{d.RebateAccrualCount}" : null);
        if (d.PurchaseReturnCount > 0)
            Line("   (±) Selisih Retur Pembelian", d.PurchaseReturnVariance, $"×{d.PurchaseReturnCount}");
        var totals = new object?[] { "Laba Bersih", d.NetProfit, $"Margin bersih {d.NetMarginPct:0.00}%" };

        var ppn = new List<object?[]> {
            new object?[] { "PPN Keluaran", d.TaxCollected },
            new object?[] { "   (−) " + L["reversed by sales returns"], d.TaxOnSalesReturns },
            new object?[] { "PPN Masukan", d.TaxPaid },
            new object?[] { "   (−) " + L["reversed by purchase returns"], d.TaxOnPurchaseReturns },
            new object?[] { L["Total invoiced"] + " (incl. PPN)", d.GrossSales },
        };
        var ppnTotal = new object?[] { d.NetVatPosition >= 0 ? "PPN owed (Kurang Bayar)" : "PPN credit (Lebih Bayar)", Math.Abs(d.NetVatPosition) };

        var cols = new List<XlColumn> { new("", Width: 42), new(L["Amount"], XlKind.Number, 18), new(L["Notes"], Width: 40) };
        var sheets = new List<XlSheet> {
            new("Laba Rugi", cols, rows, totals),
            new("PPN", new List<XlColumn> { new("", Width: 42), new(L["Amount"], XlKind.Number, 18) }, ppn, ppnTotal) };
        if (!d.IsComplete)
            sheets.Add(new(L["Not yet included"], new List<XlColumn> { new(L["Not yet included"], Width: 90) },
                d.PendingSections.Select(s => new object?[] { s })));
        return ExcelExport.File(FileStem("Laba-Rugi"), await StoreNameAsync(), "Laporan Laba Rugi", PeriodLine(), sheets.ToArray());
    }
}
