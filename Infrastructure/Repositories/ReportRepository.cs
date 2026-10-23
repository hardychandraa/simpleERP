using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;
using SimpleERP.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace SimpleERP.Infrastructure.Repositories;

/// <summary>
/// The analysis reports' queries (2026-10-15). Same filters as the P&amp;L's period totals
/// (active documents only; sale dates as UTC instants, return and purchase dates as calendar
/// days), so the reports add up to the P&amp;L. Projections only: nothing is tracked or written.
/// </summary>
public class ReportRepository : IReportRepository
{
    private readonly AppDbContext _db;
    public ReportRepository(AppDbContext db) => _db = db;

    public Task<List<SaleLineFact>> GetSaleLinesAsync(DateTime fromUtc, DateTime toUtc) =>
        _db.SaleItems.AsNoTracking()
           .Where(i => i.Sale!.SaleDate >= fromUtc && i.Sale.SaleDate < toUtc && i.Sale.Status == SaleStatus.Active)
           .Select(i => new SaleLineFact(i.SaleId, i.Sale!.CustomerId, i.ProductId, i.Qty,
                i.LineTotal - i.AllocatedInvoiceDiscount, i.CostAtSale * i.Qty, i.Sale.TaxBase))
           .ToListAsync();

    public Task<List<ReturnLineFact>> GetReturnLinesAsync(DateTime from, DateTime to) =>
        _db.CustomerReturnItems.AsNoTracking()
           .Where(i => i.Return!.ReturnDate >= from && i.Return.ReturnDate < to && i.Return.Status == ReturnStatus.Active)
           .Select(i => new ReturnLineFact(i.CustomerReturnId, i.Return!.Sale!.CustomerId, i.ProductId, i.Qty,
                i.CreditAmount, i.CostAtSale * i.Qty, i.Return.TaxBase))
           .ToListAsync();

    public Task<List<TaxDocFact>> GetSalesForTaxAsync(DateTime fromUtc, DateTime toUtc) =>
        _db.Sales.AsNoTracking()
           .Where(s => s.SaleDate >= fromUtc && s.SaleDate < toUtc && s.Status == SaleStatus.Active)
           .OrderBy(s => s.SaleDate).ThenBy(s => s.InvoiceNumber)
           .Select(s => new TaxDocFact(s.Id, s.SaleDate, s.InvoiceNumber, null, s.Customer!.Name,
                s.Customer.TaxId, s.Customer.NationalId, s.TaxBase, s.TaxAmount, s.GrandTotal, false,
                s.Customer.TaxName ?? s.Customer.Name, s.Customer.TaxAddress ?? s.Customer.Address))
           .ToListAsync();

    public Task<List<TaxDocFact>> GetSalesReturnsForTaxAsync(DateTime from, DateTime to) =>
        _db.CustomerReturns.AsNoTracking()
           .Where(r => r.ReturnDate >= from && r.ReturnDate < to && r.Status == ReturnStatus.Active)
           .OrderBy(r => r.ReturnDate).ThenBy(r => r.ReturnNumber)
           .Select(r => new TaxDocFact(r.Id, r.ReturnDate, r.ReturnNumber, r.Sale!.InvoiceNumber, r.Sale.Customer!.Name,
                r.Sale.Customer.TaxId, r.Sale.Customer.NationalId, r.TaxBase, r.TaxAmount, r.GrandTotal, false,
                r.Sale.Customer.TaxName ?? r.Sale.Customer.Name, r.Sale.Customer.TaxAddress ?? r.Sale.Customer.Address))
           .ToListAsync();

    public Task<List<TaxDocFact>> GetPurchasesForTaxAsync(DateTime from, DateTime to) =>
        _db.Purchases.AsNoTracking()
           .Where(p => p.PurchaseDate >= from && p.PurchaseDate < to && p.Status == PurchaseStatus.Active)
           .OrderBy(p => p.PurchaseDate).ThenBy(p => p.PurchaseNumber)
           .Select(p => new TaxDocFact(p.Id, p.PurchaseDate, p.PurchaseNumber, p.SupplierDocumentNumber, p.Supplier!.Name,
                p.Supplier.TaxId, null, p.TaxBase, p.TaxAmount, p.GrandTotal, p.NeedsReview))
           .ToListAsync();

    public Task<List<TaxDocFact>> GetPurchaseReturnsForTaxAsync(DateTime from, DateTime to) =>
        _db.SupplierReturns.AsNoTracking()
           .Where(r => r.ReturnDate >= from && r.ReturnDate < to && r.Status == ReturnStatus.Active)
           .OrderBy(r => r.ReturnDate).ThenBy(r => r.ReturnNumber)
           .Select(r => new TaxDocFact(r.Id, r.ReturnDate, r.ReturnNumber,
                r.Purchase!.SupplierDocumentNumber ?? r.Purchase.PurchaseNumber, r.Purchase.Supplier!.Name,
                r.Purchase.Supplier.TaxId, null, r.TaxBase, r.TaxAmount, r.GrandTotal, false))
           .ToListAsync();

    public Task<Dictionary<Guid, DateTime>> GetLastSaleDatesAsync() =>
        _db.SaleItems.AsNoTracking()
           .Where(i => i.Sale!.Status == SaleStatus.Active)
           .GroupBy(i => i.ProductId)
           .Select(g => new { g.Key, Last = g.Max(i => i.Sale!.SaleDate) })
           .ToDictionaryAsync(x => x.Key, x => x.Last);

    public Task<Dictionary<Guid, DateTime>> GetLastPurchaseDatesAsync() =>
        _db.PurchaseItems.AsNoTracking()
           .Where(i => i.Purchase!.Status == PurchaseStatus.Active)
           .GroupBy(i => i.ProductId)
           .Select(g => new { g.Key, Last = g.Max(i => i.Purchase!.PurchaseDate) })
           .ToDictionaryAsync(x => x.Key, x => x.Last);
}
