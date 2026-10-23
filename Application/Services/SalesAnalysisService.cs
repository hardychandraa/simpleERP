using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Admin's analysis reports (HC, 2026-10-15): margin by product, sales by customer, the PPN
/// summary for the tax consultant, and the open receivables / payables lists behind the Excel
/// downloads. Read-only. Sales and HPP are on the P&amp;L's basis, so each report adds up to the
/// P&amp;L for the same period to the rupiah.
/// </summary>
public class SalesAnalysisService : ISalesAnalysisService
{
    private readonly IReportRepository   _reports;
    private readonly IProductRepository  _products;
    private readonly ICustomerRepository _customers;
    private readonly ISaleRepository     _sales;
    private readonly IPurchaseRepository _purchases;
    private readonly ICreditNoteApplicationRepository _applications;
    private readonly ISaleService        _saleService;
    private readonly IInventoryService   _inventory;

    public SalesAnalysisService(IReportRepository reports, IProductRepository products, ICustomerRepository customers,
        ISaleRepository sales, IPurchaseRepository purchases, ICreditNoteApplicationRepository applications,
        ISaleService saleService, IInventoryService inventory)
    { _reports = reports; _products = products; _customers = customers; _sales = sales; _purchases = purchases;
      _applications = applications; _saleService = saleService; _inventory = inventory; }

    public async Task<SalesAnalysisDto> GetByProductAsync(DateTime from, DateTime to)
    {
        var (start, end, lines, returns) = await LoadAsync(from, to);
        var products = (await _products.GetAllAsync()).ToDictionary(p => p.Id);
        var rows = Build(lines, returns, l => l.ProductId, r => r.ProductId, id =>
        {
            products.TryGetValue(id, out var p);
            return new SalesAnalysisRowDto { Id = id, Name = p?.Name ?? "?", Code = p?.SKU, Group = p?.Category, Unit = p?.Unit };
        });
        return Finish(start, end, lines, rows.OrderByDescending(r => r.NetSales).ThenBy(r => r.Name).ToList());
    }

    public async Task<SalesAnalysisDto> GetByCustomerAsync(DateTime from, DateTime to)
    {
        var (start, end, lines, returns) = await LoadAsync(from, to);
        var customers = (await _customers.GetAllAsync()).ToDictionary(c => c.Id);
        // What each customer owes now: the Due page's figures, so the two pages agree.
        var due = (await _saleService.GetDueSummaryAsync()).ToDictionary(d => d.CustomerId);
        var rows = Build(lines, returns, l => l.CustomerId, r => r.CustomerId, id =>
        {
            customers.TryGetValue(id, out var c);
            var row = new SalesAnalysisRowDto { Id = id, Name = c?.Name ?? "?", Code = c?.Phone };
            if (due.TryGetValue(id, out var d))
            {
                row.OwedNow = d.TotalDue;
                row.Overdue = d.Aging.Days1To30 + d.Aging.Days31To60 + d.Aging.Days61To90 + d.Aging.Days90Plus;
            }
            return row;
        });
        return Finish(start, end, lines, rows.OrderByDescending(r => r.NetSales).ThenBy(r => r.Name).ToList());
    }

    public async Task<PpnSummaryDto> GetPpnSummaryAsync(DateTime from, DateTime to)
    {
        var (start, end, startUtc, endUtc) = Period(from, to);
        static TaxDocDto Map(TaxDocFact f, bool utc) => new() {
            Id = f.Id, Date = utc ? f.Date.ToLocalTime().Date : f.Date.Date, Number = f.Number, OtherNumber = f.OtherNumber,
            Party = f.Party, TaxId = f.TaxId, NationalId = f.NationalId, TaxName = f.TaxName, TaxAddress = f.TaxAddress,
            TaxBase = f.TaxBase, TaxAmount = f.TaxAmount, GrandTotal = f.GrandTotal, Unchecked = f.Unchecked };
        return new PpnSummaryDto {
            From = start, To = end.AddDays(-1),
            Sales           = (await _reports.GetSalesForTaxAsync(startUtc, endUtc)).Select(f => Map(f, true)).ToList(),
            SalesReturns    = (await _reports.GetSalesReturnsForTaxAsync(start, end)).Select(f => Map(f, false)).ToList(),
            Purchases       = (await _reports.GetPurchasesForTaxAsync(start, end)).Select(f => Map(f, false)).ToList(),
            PurchaseReturns = (await _reports.GetPurchaseReturnsForTaxAsync(start, end)).Select(f => Map(f, false)).ToList()
        };
    }

    public async Task<List<OpenDocumentDto>> GetOpenReceivablesAsync()
    {
        var due = await _sales.GetDueSalesAsync();
        var applied = await _applications.GetAppliedTotalsForSalesAsync(due.Select(s => s.Id));
        var today = DateTime.Now.Date;
        return due.Select(s => new OpenDocumentDto {
            Party = s.Customer?.Name ?? "", Phone = s.Customer?.Phone, Number = s.InvoiceNumber,
            Date = s.SaleDate.ToLocalTime().Date, DueDate = s.DueDate?.Date, DaysLate = DaysLate(s.DueDate, today),
            GrandTotal = s.GrandTotal, Paid = s.AmountPaid, NotesApplied = applied.GetValueOrDefault(s.Id)
        }).ToList();
    }

    public async Task<List<OpenDocumentDto>> GetOpenPayablesAsync()
    {
        var due = await _purchases.GetDuePurchasesAsync();
        var applied = await _applications.GetAppliedTotalsForPurchasesAsync(due.Select(p => p.Id));
        var today = DateTime.Now.Date;
        return due.Select(p => new OpenDocumentDto {
            Party = p.Supplier?.Name ?? "", Phone = p.Supplier?.Phone, Number = p.PurchaseNumber,
            OtherNumber = p.SupplierDocumentNumber, Date = p.PurchaseDate.Date, DueDate = p.DueDate?.Date,
            DaysLate = DaysLate(p.DueDate, today),
            GrandTotal = p.GrandTotal, Paid = p.AmountPaid, NotesApplied = applied.GetValueOrDefault(p.Id)
        }).ToList();
    }

    public async Task<StockListDto> GetStockListAsync()
    {
        var today = DateTime.Now.Date;
        // The Products page's own query, so stock, cost and value agree with it to the rupiah.
        var levels = await _inventory.GetAllStockLevelsAsync();
        var products = (await _products.GetAllAsync()).ToDictionary(p => p.Id);
        var lastSale = await _reports.GetLastSaleDatesAsync();
        var lastPurchase = await _reports.GetLastPurchaseDatesAsync();
        var sold30 = await NetSoldAsync(today.AddDays(-29), today);
        var sold90 = await NetSoldAsync(today.AddDays(-89), today);

        var rows = levels.Select(l =>
        {
            products.TryGetValue(l.ProductId, out var p);
            DateTime? sale = lastSale.TryGetValue(l.ProductId, out var s) ? s.ToLocalTime().Date : null;
            DateTime? bought = lastPurchase.TryGetValue(l.ProductId, out var b) ? b.Date : null;
            var since = sale ?? bought;
            return new StockListRowDto {
                ProductId = l.ProductId, Name = l.ProductName, SKU = l.SKU, Category = p?.Category, Unit = p?.Unit ?? "",
                UnitPrice = p?.UnitPrice ?? 0, IsActive = l.IsActive,
                Stock = l.CurrentStock, AvgCost = l.AvgCost, StockValue = l.StockValue,
                LastSale = sale, LastPurchase = bought,
                IdleDays = since is { } d ? Math.Max(0, (today - d).Days) : null,
                Sold30 = sold30.GetValueOrDefault(l.ProductId), Sold90 = sold90.GetValueOrDefault(l.ProductId)
            };
        });
        return new StockListDto {
            AsOf = DateTime.Now,
            Rows = rows.OrderByDescending(r => r.StockValue).ThenBy(r => r.Name).ToList(),
            TotalValue = levels.Sum(l => l.StockValue)
        };
    }

    /// <summary>Quantity sold less returned per product, local days from..to inclusive.</summary>
    private async Task<Dictionary<Guid, decimal>> NetSoldAsync(DateTime from, DateTime to)
    {
        var (_, _, lines, returns) = await LoadAsync(from, to);
        var net = lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
        foreach (var r in returns) net[r.ProductId] = net.GetValueOrDefault(r.ProductId) - r.Qty;
        return net;
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private static int DaysLate(DateTime? due, DateTime today) =>
        due is { } d && d.Date < today ? (today - d.Date).Days : 0;

    /// <summary>Whole local days from..to inclusive; sale dates compare as UTC instants (as the P&amp;L does).</summary>
    private static (DateTime start, DateTime end, DateTime startUtc, DateTime endUtc) Period(DateTime from, DateTime to)
    {
        var start = from.Date; var end = to.Date.AddDays(1);
        return (start, end, DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime(),
                            DateTime.SpecifyKind(end,   DateTimeKind.Local).ToUniversalTime());
    }

    private async Task<(DateTime, DateTime, List<SaleLineFact>, List<ReturnLineFact>)> LoadAsync(DateTime from, DateTime to)
    {
        var (start, end, startUtc, endUtc) = Period(from, to);
        return (start, end, await _reports.GetSaleLinesAsync(startUtc, endUtc), await _reports.GetReturnLinesAsync(start, end));
    }

    private static SalesAnalysisDto Finish(DateTime start, DateTime end, List<SaleLineFact> lines, List<SalesAnalysisRowDto> rows)
    {
        var net = rows.Sum(r => r.NetSales);
        foreach (var r in rows)
            r.SharePercent = net != 0 ? Math.Round(r.NetSales / net * 100m, 1, MidpointRounding.AwayFromZero) : 0m;
        return new SalesAnalysisDto {
            From = start, To = end.AddDays(-1), Rows = rows,
            InvoiceCount = lines.Select(l => l.SaleId).Distinct().Count()
        };
    }

    /// <summary>
    /// Groups the period's sale and return lines by <paramref name="saleKey"/> / <paramref name="returnKey"/>.
    /// Each invoice's stored DPP is split over its lines in proportion to their net amount (after
    /// line and invoice discount), each return's over its lines by credit; the last line of a
    /// document takes the remainder, so the shares add up to the stored DPP exactly. Then each
    /// money column is rounded to whole rupiah so the rows add up to the rounded total.
    /// </summary>
    private static List<SalesAnalysisRowDto> Build(List<SaleLineFact> lines, List<ReturnLineFact> returns,
        Func<SaleLineFact, Guid> saleKey, Func<ReturnLineFact, Guid> returnKey, Func<Guid, SalesAnalysisRowDto> newRow)
    {
        var sales = Split(lines.GroupBy(l => l.SaleId), l => l.LineNet, l => l.SaleTaxBase);
        var rets  = Split(returns.GroupBy(r => r.ReturnId), r => r.Credit != 0 ? r.Credit : r.Qty, r => r.ReturnTaxBase);

        var rows = new Dictionary<Guid, (SalesAnalysisRowDto Row, decimal Sales, decimal Returns, decimal Cogs, decimal ReturnCogs, HashSet<Guid> Invoices)>();
        (SalesAnalysisRowDto Row, decimal Sales, decimal Returns, decimal Cogs, decimal ReturnCogs, HashSet<Guid> Invoices) Get(Guid id) =>
            rows.TryGetValue(id, out var x) ? x : (newRow(id), 0m, 0m, 0m, 0m, new HashSet<Guid>());
        foreach (var (l, dpp) in sales)
        {
            var x = Get(saleKey(l)); x.Row.QtySold += l.Qty; x.Invoices.Add(l.SaleId);
            rows[saleKey(l)] = (x.Row, x.Sales + dpp, x.Returns, x.Cogs + l.Cost, x.ReturnCogs, x.Invoices);
        }
        foreach (var (r, dpp) in rets)
        {
            var x = Get(returnKey(r)); x.Row.QtyReturned += r.Qty;
            rows[returnKey(r)] = (x.Row, x.Sales, x.Returns + dpp, x.Cogs, x.ReturnCogs + r.Cost, x.Invoices);
        }

        var list = rows.Values.ToList();
        var s = RoundToTotal(list.Select(x => x.Sales).ToList());
        var rt = RoundToTotal(list.Select(x => x.Returns).ToList());
        var c = RoundToTotal(list.Select(x => x.Cogs).ToList());
        var rc = RoundToTotal(list.Select(x => x.ReturnCogs).ToList());
        for (var i = 0; i < list.Count; i++)
        {
            var row = list[i].Row;
            row.Sales = s[i]; row.Returns = rt[i]; row.Cogs = c[i]; row.ReturnCogs = rc[i];
            row.InvoiceCount = list[i].Invoices.Count;
        }
        return list.Select(x => x.Row).ToList();
    }

    /// <summary>Splits each document's stored DPP over its lines by weight; the last line takes the remainder.</summary>
    private static List<(T Line, decimal Dpp)> Split<T>(IEnumerable<IGrouping<Guid, T>> docs, Func<T, decimal> weight, Func<T, decimal> docDpp)
    {
        var result = new List<(T, decimal)>();
        foreach (var doc in docs)
        {
            var items = doc.ToList();
            var dpp = docDpp(items[0]);
            var total = items.Sum(weight);
            decimal given = 0m;
            for (var i = 0; i < items.Count; i++)
            {
                var share = i == items.Count - 1 ? dpp - given
                          : total != 0 ? dpp * weight(items[i]) / total : 0m;
                given += share;
                result.Add((items[i], share));
            }
        }
        return result;
    }

    /// <summary>
    /// Rounds each value to whole rupiah so the rounded values add up to the rounded total
    /// (largest remainder): rounding each on its own can leave the column a rupiah or two off
    /// its total, and the totals here must equal the P&amp;L's.
    /// </summary>
    internal static decimal[] RoundToTotal(IReadOnlyList<decimal> values)
    {
        var total = Math.Round(values.Sum(), 0, MidpointRounding.AwayFromZero);
        var result = values.Select(Math.Floor).ToArray();
        var extra = (int)(total - result.Sum());
        foreach (var i in Enumerable.Range(0, values.Count).OrderByDescending(i => values[i] - result[i]).ThenBy(i => i).Take(extra))
            result[i] += 1m;
        return result;
    }
}
