using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Financial/accounting reports, kept separate from ReportService (which stays
/// focused on operational reports: EndOfDay, Warranty, Audit).
///
/// Produces the commercial P&amp;L — the same basis as the "SPT" column in the
/// annual statement. Fiscal corrections and PPh Badan remain the tax consultant's
/// work; this supplies the inputs rather than replacing that step.
/// </summary>
public class FinancialReportService : IFinancialReportService
{
    private readonly ISaleRepository    _sales;
    private readonly IExpenseRepository _expenses;

    public FinancialReportService(ISaleRepository sales, IExpenseRepository expenses)
    { _sales = sales; _expenses = expenses; }

    public async Task<ProfitAndLossDto> GetProfitAndLossAsync(DateTime from, DateTime to)
    {
        // Normalise to a half-open [from, to) range over whole days, so a caller
        // passing the same date for both still gets that entire day rather than
        // an empty window.
        var start = from.Date;
        var end   = to.Date.AddDays(1);

        var totals       = await _sales.GetPeriodTotalsAsync(start, end);
        var expenseTotals = await _expenses.GetCategoryTotalsAsync(start, end);

        return new ProfitAndLossDto {
            From         = start,
            To           = end.AddDays(-1),
            InvoiceCount = totals.InvoiceCount,
            Revenue      = totals.Revenue,
            Cogs         = totals.Cogs,
            TaxCollected = totals.TaxCollected,
            GrossSales   = totals.GrossSales,

            ExpenseLines = expenseTotals.Select(t => new ProfitAndLossExpenseLineDto {
                CategoryName    = t.CategoryName,
                IsTaxDeductible = t.IsTaxDeductible,
                EntryCount      = t.EntryCount,
                Amount          = t.Amount
            }).ToList(),

            // Named explicitly so the report can show what it cannot yet account for.
            // Each disappears when its module lands.
            PendingSections = {
                "Pendapatan Luar Usaha (rebate/support income) — needs the Purchase and Rebate modules",
                "Komisi Penjualan (sales commission) — needs the SalesPerson and Commission modules"
            }
        };
    }
}
