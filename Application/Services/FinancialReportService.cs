using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Financial/accounting reports, kept separate from ReportService (which stays
/// focused on operational reports: EndOfDay, Warranty, Audit).
///
/// Produces the commercial P&amp;L — the same basis as the "SPT" column in the
/// annual statement. Fiscal corrections and PPh Badan remain the tax consultant's
/// work; this supplies the inputs rather than replacing that step.
///
/// Every figure here is read through a set-based repository aggregate (SUM/COUNT in
/// SQL). Nothing in this file materialises a document list: a P&amp;L can legitimately
/// span a full year, and the report must not get slower as the business gets busier.
/// </summary>
public class FinancialReportService : IFinancialReportService
{
    private readonly ISaleRepository               _sales;
    private readonly IExpenseRepository            _expenses;
    private readonly IPurchaseRepository           _purchases;
    private readonly ICustomerReturnRepository     _customerReturns;
    private readonly ISupplierReturnRepository     _supplierReturns;
    private readonly IRebateAccrualRepository      _rebateAccruals;
    private readonly IRebateRealizationRepository  _rebateRealizations;
    private readonly ICommissionAccrualRepository  _commissionAccruals;
    private readonly ICreditNoteRepository         _notes;
    private readonly IInventoryLedgerRepository    _ledger;
    private readonly IBranchRepository             _branches;

    public FinancialReportService(
        ISaleRepository sales,
        IExpenseRepository expenses,
        IPurchaseRepository purchases,
        ICustomerReturnRepository customerReturns,
        ISupplierReturnRepository supplierReturns,
        IRebateAccrualRepository rebateAccruals,
        IRebateRealizationRepository rebateRealizations,
        ICommissionAccrualRepository commissionAccruals,
        ICreditNoteRepository notes,
        IInventoryLedgerRepository ledger,
        IBranchRepository branches)
    {
        _sales              = sales;
        _expenses           = expenses;
        _purchases          = purchases;
        _customerReturns    = customerReturns;
        _supplierReturns    = supplierReturns;
        _rebateAccruals     = rebateAccruals;
        _rebateRealizations = rebateRealizations;
        _commissionAccruals = commissionAccruals;
        _notes              = notes;
        _ledger             = ledger;
        _branches           = branches;
    }

    public async Task<ProfitAndLossDto> GetProfitAndLossAsync(DateTime from, DateTime to)
    {
        // Normalise to a half-open [from, to) range over whole days, so a caller
        // passing the same date for both still gets that entire day rather than
        // an empty window.
        var start = from.Date;
        var end   = to.Date.AddDays(1);

        var totals          = await _sales.GetPeriodTotalsAsync(start, end);
        var expenseTotals   = await _expenses.GetCategoryTotalsAsync(start, end);
        var purchaseTotals  = await _purchases.GetPeriodTotalsAsync(start, end);
        var salesReturns    = await _customerReturns.GetPeriodTotalsAsync(start, end);
        var purchaseReturns = await _supplierReturns.GetPeriodTotalsAsync(start, end);
        var rebateAccrued   = await _rebateAccruals.GetPeriodTotalsAsync(start, end);
        var rebateRealized  = await _rebateRealizations.GetPeriodTotalsAsync(start, end);
        var commission      = await _commissionAccruals.GetPeriodTotalsAsync(start, end);

        // Rebate is recognised when it accrues, consistent with revenue being recognised
        // at invoice rather than at collection. LuckyDraw is the one exception: it accrues
        // zero because the amount is genuinely unknowable until the draw happens, so the
        // settlement is the first moment it can be measured — and the only slice of the
        // realization figures that may be added without double-counting rebate already
        // recognised on the accrual side.
        var rebateIncome = rebateAccrued.CashAccrued + rebateRealized.LuckyDrawGross;

        // A purchase return releases inventory at moving-average cost but is credited by
        // the supplier at the price they agree to. The two are not equal, and the gap is a
        // real gain or loss — dropping it would leave inventory and payables unable to
        // reconcile. NetAmount is ex-PPN, matching StockValue, which is a cost.
        var purchaseReturnVariance = purchaseReturns.NetAmount - purchaseReturns.StockValue;

        return new ProfitAndLossDto {
            From         = start,
            To           = end.AddDays(-1),
            InvoiceCount = totals.InvoiceCount,

            Revenue          = totals.Revenue,
            SalesReturns     = salesReturns.NetAmount,
            SalesReturnCount = salesReturns.ReturnCount,

            Cogs            = totals.Cogs,
            SalesReturnCogs = salesReturns.StockValue,

            ExpenseLines = expenseTotals.Select(t => new ProfitAndLossExpenseLineDto {
                CategoryName    = t.CategoryName,
                IsTaxDeductible = t.IsTaxDeductible,
                EntryCount      = t.EntryCount,
                Amount          = t.Amount
            }).ToList(),

            CommissionExpense      = commission.Amount,
            CommissionAccrualCount = commission.AccrualCount,

            RebateIncome            = rebateIncome,
            RebateAccrued           = rebateAccrued.CashAccrued,
            RebateLuckyDrawRealized = rebateRealized.LuckyDrawGross,
            RebateAccrualCount      = rebateAccrued.AccrualCount,
            RebateInKindCount       = rebateAccrued.InKindCount,

            PurchaseReturnVariance   = purchaseReturnVariance,
            PurchaseReturnCredit     = purchaseReturns.NetAmount,
            PurchaseReturnStockValue = purchaseReturns.StockValue,
            PurchaseReturnCount      = purchaseReturns.ReturnCount,

            TaxCollected         = totals.TaxCollected,
            TaxOnSalesReturns    = salesReturns.TaxReversed,
            TaxPaid              = purchaseTotals.TaxPaid,
            TaxOnPurchaseReturns = purchaseReturns.TaxReversed,
            GrossSales           = totals.GrossSales

            // PendingSections is deliberately left empty from Step 9 onward: rebate income,
            // commission and returns are all wired in above, so the report now runs to a
            // real Laba Bersih. The property stays on the DTO rather than being deleted —
            // it's the honest mechanism for admitting a gap, and the next section that
            // outgrows this report (a true Neraca) should use it rather than quietly
            // showing a number nobody can stand behind.
        };
    }

    public async Task<PositionSummaryDto> GetPositionSummaryAsync()
    {
        var receivables = await _sales.GetReceivablesTotalAsync();
        var payables    = await _purchases.GetPayablesTotalAsync();
        var openCredits = await _notes.GetOpenTotalAsync(CreditDebitType.Credit);
        var openDebits  = await _notes.GetOpenTotalAsync(CreditDebitType.Debit);
        var rebate      = await _rebateAccruals.GetOutstandingTotalAsync();
        var commission  = await _commissionAccruals.GetUnpaidTotalAsync();

        // Inventory is valued per branch. Single-branch today, so the default branch is
        // the whole position; when the multi-branch UI lands (Step 10) this becomes a
        // sum across branches rather than a single lookup.
        var branch    = await _branches.GetDefaultAsync();
        var valuation = branch == null
            ? new InventoryValuation(0, 0m)
            : await _ledger.GetValuationAsync(branch.Id);

        return new PositionSummaryDto {
            AsOf = DateTime.UtcNow,

            InventoryValue  = valuation.TotalValue,
            ProductsInStock = valuation.ProductsInStock,

            ReceivablesGross   = receivables.Total,
            OpenInvoices       = receivables.OpenInvoices,
            ReceivablesOverdue = receivables.Overdue,
            OpenCreditNotes    = openCredits,

            UnclaimedRebate            = rebate.CashAccrued,
            UnclaimedRebateCount       = rebate.AccrualCount,
            RebateInKindOutstanding    = rebate.InKindCount,
            RebateLuckyDrawOutstanding = rebate.LuckyDrawCount,

            PayablesGross   = payables.Total,
            OpenPurchases   = payables.OpenPurchases,
            PayablesOverdue = payables.Overdue,
            OpenDebitNotes  = openDebits,

            CommissionPayable      = commission.Amount,
            CommissionPayableCount = commission.AccrualCount
        };
    }
}
