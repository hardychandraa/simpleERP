using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Web.Services;
namespace SimpleERP.Web.Pages.Inventory;

/// <summary>
/// Stock card (kartu stok): every movement by document date with its document number,
/// customer/supplier and the running balance, for one product or all of them.
/// </summary>
public class LedgerModel : PageModel
{
    private readonly IInventoryService _svc;
    private readonly IProductService   _products;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> L;
    public LedgerModel(IInventoryService s, IProductService products, IAppSettingsService settings, IStringLocalizer<SharedResource> loc)
    { _svc = s; _products = products; _settings = settings; L = loc; }

    public StockCardDto       Card     { get; set; } = new();
    public List<ProductDto>   Products { get; set; } = new();
    public Guid?    ProductId { get; set; }
    public DateTime From      { get; set; }
    public DateTime To        { get; set; }

    public async Task OnGetAsync(Guid? productId, DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Ledger";
        await LoadAsync(productId, from, to);
    }

    private async Task LoadAsync(Guid? productId, DateTime? from, DateTime? to)
    {
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
        // Newest first (HC, 2026-10-07). The running balance is worked out oldest-first by the
        // service, so each row still shows the stock right after that movement.
        Card.Rows.Reverse();

        // Staff see the movements and balances but not what anything cost: on a sale row
        // the unit cost is the HPP (HC, 2026-10-08). Purchase and supplier-return rows keep
        // their number and supplier; purchases link to Goods In, supplier returns lose the link.
        if (!User.SeesCost())
        {
            foreach (var p in Products) { p.AvgCost = 0; p.PurchasePrice = 0; }
            foreach (var r in Card.Rows)
            {
                r.UnitCost = 0; r.TotalCost = 0;
                // A purchase opens as its quantity-only Goods In view (HC, 2026-10-09).
                if (r.Link != null && r.Link.StartsWith("/Purchases/"))
                    r.Link = "/GoodsIn/Detail/" + r.Link["/Purchases/".Length..];
                else if (r.Link != null && !r.Link.StartsWith("/Sales/") && !r.Link.StartsWith("/Returns/Customer/")
                         && !r.Link.StartsWith("/Returns/Exchange/"))
                    r.Link = null;
            }
        }
    }
    /// <summary>
    /// The stock card to Excel (HC, 2026-10-21), oldest first like a paper kartu stok, with the
    /// opening balance on top for one product. Admin only, like every Excel download: it carries
    /// cost, and files left in a shared PC's Downloads are the R8 risk.
    /// </summary>
    public async Task<IActionResult> OnGetExcelAsync(Guid? productId, DateTime? from, DateTime? to)
    {
        if (!User.SeesCost()) return this.Refuse("stock card Excel is Admin-only");
        await LoadAsync(productId, from, to);
        var one = ProductId.HasValue;
        var product = one ? Products.FirstOrDefault(p => p.Id == ProductId) : null;
        var cols = new List<XlColumn> { new(L["Date"], XlKind.Date), new(L["Document"]), new(L["Type"]), new(L["Customer / Supplier"]) };
        if (!one) cols.Add(new(L["Product"]));
        cols.AddRange(new XlColumn[] { new(L["Qty In"], XlKind.Number), new(L["Qty Out"], XlKind.Number), new(L["Balance"], XlKind.Number),
            new(L["Unit Cost"], XlKind.Number), new(L["Total"], XlKind.Number) });

        var rows = new List<object?[]>();
        if (one) rows.Add(new object?[] { From, null, L["Opening balance"].Value, null, null, null,
            Card.Opening.GetValueOrDefault(ProductId!.Value), null, null });
        foreach (var e in Enumerable.Reverse(Card.Rows))   // the page reversed them to newest first
        {
            var r = new List<object?> { e.TransactionDate.ToLocalTime().Date, e.DocumentNumber,
                L[EnumDisplay.Key("ReferenceType", e.ReferenceType)].Value, e.Party };
            if (!one) r.Add(e.ProductName);
            r.AddRange(new object?[] { e.QtyIn, e.QtyOut, e.Balance, e.UnitCost, e.TotalCost });
            rows.Add(r.ToArray());
        }
        var totals = new List<object?> { L["Total"].Value, null, null, null };
        if (!one) totals.Add(null);
        totals.AddRange(new object?[] { Card.Rows.Sum(e => e.QtyIn), Card.Rows.Sum(e => e.QtyOut), null, null, null });

        var title = one && product != null ? $"Kartu Stok {product.Name} ({product.SKU})" : "Kartu Stok";
        var stem = one && product != null ? $"Kartu-Stok_{product.SKU}" : "Kartu-Stok";
        return ExcelExport.File($"{stem}_{From:yyyy-MM-dd}_{To:yyyy-MM-dd}", (await _settings.GetAsync()).StoreName, title,
            L["Period {0} – {1} · printed {2}", From.ToString("dd MMM yyyy"), To.ToString("dd MMM yyyy"), DateTime.Now.ToString("dd MMM yyyy HH:mm")],
            new XlSheet("Kartu Stok", cols, rows, totals.ToArray()));
    }
}
