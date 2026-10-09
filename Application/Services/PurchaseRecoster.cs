using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>New receipt of one product on a revised purchase: total qty and its net unit cost ex-PPN.</summary>
public sealed record RecostReceipt(decimal Qty, decimal UnitCostExTax);

/// <summary>What a re-cost changed: sale lines whose HPP moved, and by how much in total.</summary>
public sealed record RecostSummary(int SaleLinesChanged, decimal HppDelta);

/// <summary>
/// Re-costs stock after a purchase is revised (HC, 2026-10-08): every figure posted after the
/// purchase is recomputed as if the purchase had been right from the start.
///
/// Why a replay: the moving average is "the UnitCost of the latest stock-in row" and every
/// later movement snapshotted it: SaleItem.CostAtSale (the P&amp;L's HPP), CustomerReturnItem.CostAtSale,
/// SupplierReturnItem.CostAtReturn and the ledger rows themselves. Changing one purchase's
/// price or quantity changes all of those for that product, so they are recomputed in posting
/// order (EnteredAt), exactly as they were first computed:
///   • stock-in rows blend: newAvg = S ≤ 0 ? c : (S·A + q·c)/(S + q), where c is the row's own
///     input cost (its purchase line, the sale cost a cancel or return restocks at, the current
///     average for an adjustment, 0 value-preserving for rebate goods). Where no document holds
///     the input (manual stock in, opening stock), it is derived from the row's original numbers.
///   • stock-out rows (sales, supplier returns, adjustments, cancels) take the current average.
/// Values are rounded to 4 dp at each row, the column scale, which is what the original
/// postings read back from the database.
///
/// The purchase's own rows for a product collapse into one row at the first row's place.
/// Refuses (nothing written; the caller rolls back) if stock would go negative anywhere in the
/// replay, or if a movement dated in a closed month would change. Does NOT SaveChanges.
/// </summary>
public sealed class PurchaseRecoster
{
    private readonly IInventoryLedgerRepository _ledger;
    private readonly ICostSnapshotRepository    _docs;
    private readonly PeriodLock                 _period;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<PurchaseRecoster> _log;

    public PurchaseRecoster(IInventoryLedgerRepository ledger, ICostSnapshotRepository docs, PeriodLock period,
        IStringLocalizer<SharedResource> loc, ILogger<PurchaseRecoster> log)
    { _ledger = ledger; _docs = docs; _period = period; _loc = loc; _log = log; }

    private static decimal R4(decimal x) => Math.Round(x, 4, MidpointRounding.AwayFromZero);
    private const decimal Eps = 0.00005m;

    /// <param name="ledgerDate">The purchase's ledger date (new rows, and moved rows when <paramref name="dateChanged"/>).</param>
    /// <param name="productNames">For refusal messages.</param>
    public async Task<ServiceResult<RecostSummary>> RecostAsync(
        Guid purchaseId, Guid branchId, DateTime ledgerDate, bool dateChanged,
        IReadOnlyDictionary<Guid, RecostReceipt> newByProduct, IEnumerable<Guid> oldProductIds,
        IReadOnlyDictionary<Guid, string> productNames)
    {
        var changedSaleLines = 0;
        var hppDelta = 0m;

        foreach (var productId in oldProductIds.Concat(newByProduct.Keys).Distinct())
        {
            var hist = await _ledger.GetProductHistoryAsync(productId, branchId);
            var ownRows = hist.Where(r => r.ReferenceType == ReferenceType.PurchaseOrder && r.ReferenceId == purchaseId).ToList();
            newByProduct.TryGetValue(productId, out var receipt);

            if (ownRows.Count == 0)
            {
                if (receipt == null) continue;
                // A product added by the revision: received now, like any backdated receipt.
                // Nothing was posted against it from this purchase, so nothing after it moves.
                var s0 = hist.Sum(r => r.QtyIn - r.QtyOut);
                var a0 = hist.LastOrDefault(r => r.QtyIn > 0)?.UnitCost ?? 0m;
                var raw0 = s0 <= 0 ? receipt.UnitCostExTax : (s0 * a0 + receipt.Qty * receipt.UnitCostExTax) / (s0 + receipt.Qty);
                await _ledger.AddAsync(new InventoryLedger {
                    Id = Guid.NewGuid(), TransactionDate = ledgerDate, BranchId = branchId, ProductId = productId,
                    ReferenceType = ReferenceType.PurchaseOrder, ReferenceId = purchaseId,
                    QtyIn = receipt.Qty, QtyOut = 0, UnitCost = R4(raw0), TotalCost = R4(receipt.Qty * raw0) });
                continue;
            }

            var start = hist.IndexOf(ownRows[0]);
            decimal S = 0, A = 0;
            for (var i = 0; i < start; i++)
            {
                S += hist[i].QtyIn - hist[i].QtyOut;
                if (hist[i].QtyIn > 0) A = hist[i].UnitCost;
            }
            decimal origS = S, origA = A;   // the original sequence, for deriving input costs

            // Documents referenced by the rows being replayed, tracked: updating them updates the DB.
            var later = hist.Skip(start).ToList();
            var refIds = later.Select(r => r.ReferenceId).Distinct().ToList();
            var saleItems = (await _docs.GetSaleItemsAsync(refIds, productId)).ToLookup(i => i.SaleId);
            var custItems = (await _docs.GetCustomerReturnItemsAsync(refIds, productId)).ToLookup(i => i.CustomerReturnId);
            var suppItems = (await _docs.GetSupplierReturnItemsAsync(refIds, productId)).ToLookup(i => i.SupplierReturnId);
            var purItems  = (await _docs.GetPurchaseItemsAsync(refIds.Where(id => id != purchaseId), productId)).ToLookup(i => i.PurchaseId);
            var newSaleItemCost = new Dictionary<Guid, decimal>();

            foreach (var r in later)
            {
                var origIn = r.QtyIn; var origOut = r.QtyOut; var origUnit = r.UnitCost;

                if (r.ReferenceType == ReferenceType.PurchaseOrder && r.ReferenceId == purchaseId)
                {
                    if (r != ownRows[0] || receipt == null)
                        _ledger.Remove(r);   // collapsed into the first row, or the product left the purchase
                    else
                    {
                        var raw = S <= 0 ? receipt.UnitCostExTax : (S * A + receipt.Qty * receipt.UnitCostExTax) / (S + receipt.Qty);
                        r.QtyIn = receipt.Qty; r.UnitCost = R4(raw); r.TotalCost = R4(receipt.Qty * raw);
                        // Only a changed purchase date moves the row: keeping its original time
                        // keeps its place among the day's movements on the stock card.
                        if (dateChanged) r.TransactionDate = ledgerDate;
                        S += receipt.Qty; A = r.UnitCost;
                    }
                    origS += origIn; origA = origUnit;
                    continue;
                }

                if (r.QtyIn > 0)
                {
                    var q = r.QtyIn;
                    decimal raw;
                    if (r.ReferenceType == ReferenceType.RebateInKind)
                        raw = S + q <= 0 ? 0m : S * A / (S + q);   // free goods: value unchanged
                    else
                    {
                        var c = InputCost(r, q, origS, origA, A, saleItems, custItems, suppItems, purItems, newSaleItemCost);
                        raw = S <= 0 ? c : (S * A + q * c) / (S + q);
                    }
                    if (await ChangesClosedRow(r, R4(raw)) is { } closed) return Refuse(closed);
                    r.UnitCost = R4(raw); r.TotalCost = R4(q * raw);
                    S += q; A = r.UnitCost;
                }
                else
                {
                    var q = r.QtyOut;
                    if (S < q - Eps)
                        return Refuse(_loc["{0}: this change would leave too little stock for a later movement ({1}, {2} units). Those units have already been sold or issued; correct with a supplier return instead.",
                            productNames.GetValueOrDefault(productId, ""), DescribeRow(r), q.ToString("N0")]);
                    if (await ChangesClosedRow(r, A) is { } closed) return Refuse(closed);
                    r.UnitCost = A; r.TotalCost = R4(A * q);

                    if (r.ReferenceType == ReferenceType.Sale)
                        foreach (var item in saleItems[r.ReferenceId])
                        {
                            if (item.CostAtSale != A)
                            {
                                hppDelta += (A - item.CostAtSale) * item.Qty;
                                changedSaleLines++;
                                item.CostAtSale = A;
                            }
                            newSaleItemCost[item.Id] = A;
                        }
                    else if (r.ReferenceType == ReferenceType.SupplierReturn)
                        foreach (var item in suppItems[r.ReferenceId]) item.CostAtReturn = A;
                    S -= q;
                }

                origS += origIn - origOut;
                if (origIn > 0) origA = origUnit;
            }
        }

        return ServiceResult<RecostSummary>.Ok(new RecostSummary(changedSaleLines, R4(hppDelta)));
    }

    /// <summary>The cost a stock-in row brings in, by what posted it.</summary>
    private static decimal InputCost(InventoryLedger r, decimal q, decimal origS, decimal origA, decimal curA,
        ILookup<Guid, SaleItem> saleItems, ILookup<Guid, CustomerReturnItem> custItems,
        ILookup<Guid, SupplierReturnItem> suppItems, ILookup<Guid, PurchaseItem> purItems,
        Dictionary<Guid, decimal> newSaleItemCost)
    {
        switch (r.ReferenceType)
        {
            case ReferenceType.Adjustment:
                return curA;   // a count correction comes in at the current average

            case ReferenceType.PurchaseOrder:
                var lines = purItems[r.ReferenceId].ToList();
                if (lines.Count == 1 && lines[0].Purchase is { } p)
                    return PurchaseService.NetUnitCostExTax(lines[0], p.IsTaxInclusive, p.TaxRate);
                break;   // two lines of one product on that purchase: derive below

            case ReferenceType.Cancel:
                // A cancelled sale restocks at its own CostAtSale; a cancelled supplier return
                // takes the goods back at the CostAtReturn they left at. Both are tracked entities,
                // so a value re-costed earlier in this replay is already the new one.
                if (saleItems[r.ReferenceId].FirstOrDefault() is { } si) return si.CostAtSale;
                if (suppItems[r.ReferenceId].FirstOrDefault() is { } sr) return sr.CostAtReturn;
                break;

            case ReferenceType.CustomerReturn:
                var ret = custItems[r.ReferenceId].ToList();
                if (ret.Count > 0)
                {
                    foreach (var item in ret)
                        if (newSaleItemCost.TryGetValue(item.SaleItemId, out var c)) item.CostAtSale = c;
                    return ret[0].CostAtSale;
                }
                break;
        }
        // Manual stock in, opening stock, or anything without a document holding its input:
        // recover the input from the row's original before/after averages.
        return origS <= 0 ? r.UnitCost : (r.UnitCost * (origS + q) - origS * origA) / q;
    }

    private async Task<LocalizedString?> ChangesClosedRow(InventoryLedger r, decimal newUnit)
    {
        if (Math.Abs(newUnit - r.UnitCost) <= Eps) return null;
        return await _period.ExistingAsync(DescribeRow(r), PeriodLock.LocalDay(r.TransactionDate));
    }

    private string DescribeRow(InventoryLedger r) =>
        $"{_loc[EnumKey(r.ReferenceType)].Value} {PeriodLock.LocalDay(r.TransactionDate):dd MMM yyyy}";

    private static string EnumKey(ReferenceType t) => "ReferenceType_" + t;

    private ServiceResult<RecostSummary> Refuse(LocalizedString why) => _log.Refuse<RecostSummary>(why);
}
