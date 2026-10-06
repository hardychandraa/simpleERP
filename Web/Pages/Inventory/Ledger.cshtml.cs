using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Inventory;

/// <summary>
/// Stock card (kartu stok): every movement by document date with its document number,
/// customer/supplier and the running balance, for one product or all of them.
/// </summary>
public class LedgerModel : PageModel
{
    private readonly IInventoryService _svc;
    private readonly IProductService   _products;
    public LedgerModel(IInventoryService s, IProductService products) { _svc = s; _products = products; }

    public StockCardDto       Card     { get; set; } = new();
    public List<ProductDto>   Products { get; set; } = new();
    public Guid?    ProductId { get; set; }
    public DateTime From      { get; set; }
    public DateTime To        { get; set; }

    public async Task OnGetAsync(Guid? productId, DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Ledger";
        // Default: this month so far, the period a stock check usually covers.
        var today = DateTime.Today;
        From = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        To   = (to   ?? today).Date;
        ProductId = productId;
        Products  = (await _products.GetAllAsync()).OrderBy(p => p.Name).ToList();
        // The dates are local calendar days; the ledger stores UTC. To is inclusive.
        Card = await _svc.GetStockCardAsync(productId,
            DateTime.SpecifyKind(From, DateTimeKind.Local).ToUniversalTime(),
            DateTime.SpecifyKind(To.AddDays(1), DateTimeKind.Local).ToUniversalTime());
    }
}
