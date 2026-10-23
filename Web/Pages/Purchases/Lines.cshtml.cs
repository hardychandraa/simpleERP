using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// Rincian baris (HC, 2026-10-22: "I need to click each PO to check the details"): every purchase in
/// the filter with its lines and totals, laid out like a supplier invoice, so a pile of invoices is
/// checked in one scroll; "Sudah dicek" on each flagged one; the same to Excel. Admin only (the
/// /Purchases folder). Same filters as the list, plus a supplier.
/// </summary>
public class LinesModel : PageModel
{
    private readonly IPurchaseService _svc;
    private readonly ISupplierService _suppliers;
    private readonly IAppSettingsService _settings;
    private readonly IStringLocalizer<SharedResource> L;
    public LinesModel(IPurchaseService svc, ISupplierService suppliers, IAppSettingsService settings, IStringLocalizer<SharedResource> loc)
    { _svc = svc; _suppliers = suppliers; _settings = settings; L = loc; }

    /// <summary>At most this many purchases on one page: a month of one supplier fits easily.</summary>
    public const int Max = 150;

    public List<PurchaseDto> Purchases { get; set; } = new();
    public List<SupplierDto> Suppliers { get; set; } = new();
    public int Total { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? SupplierId { get; set; }
    [BindProperty(SupportsGet = true)] public bool Pending { get; set; }
    public string? Msg { get; set; }
    public bool IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Purchase lines";
        Msg = msg; IsErr = err;
        Suppliers = (await _suppliers.GetAllAsync()).OrderBy(s => s.Name).ToList();
        Purchases = await LoadAsync();
    }

    public async Task<IActionResult> OnPostReviewAsync(Guid id)
    {
        var r = await _svc.MarkReviewedAsync(id, this.CurrentUserName());
        return Redirect(Query("/Purchases/Lines", r.Success ? null : r.Error, !r.Success) + $"#p{id}");
    }

    public async Task<IActionResult> OnGetExcelAsync()
    {
        var list = await LoadAsync();
        var cols = new List<XlColumn> {
            new(L["Date"], XlKind.Date), new(L["PO Number"]), new(L["Supplier Doc"]), new(L["Supplier"]), new(L["Product"]), new("SKU"),
            new(L["Qty"], XlKind.Number), new(L["Unit Cost"], XlKind.Number), new(L["Discount"], XlKind.Number), new(L["Line Total"], XlKind.Number),
            new("DPP", XlKind.Number), new("PPN", XlKind.Number), new(L["Total"], XlKind.Number), new(L["Needs checking"]) };
        // One row per line; the purchase's DPP / PPN / total on its first line only, so the columns add up.
        var rows = list.SelectMany(p => p.Items.Select((i, n) => new object?[] {
            p.PurchaseDate, p.PurchaseNumber, p.SupplierDocumentNumber, p.SupplierName, i.ProductName, i.SKU,
            i.Qty, i.UnitCost, i.DiscountAmount, i.LineTotal,
            n == 0 ? p.TaxBase : null, n == 0 ? p.TaxAmount : null, n == 0 ? p.GrandTotal : null,
            n == 0 && p.NeedsReview ? L["Yes"].Value : null }));
        var totals = new object?[] { L["Total"].Value, null, null, null, null, null, list.Sum(p => p.Items.Sum(i => i.Qty)), null, null,
            list.Sum(p => p.Items.Sum(i => i.LineTotal)), list.Sum(p => p.TaxBase), list.Sum(p => p.TaxAmount), list.Sum(p => p.GrandTotal), null };
        var sub = $"{(From.HasValue ? From.Value.ToString("dd MMM yyyy") : "…")} – {(To.HasValue ? To.Value.ToString("dd MMM yyyy") : "…")}";
        return ExcelExport.File($"Rincian-Pembelian_{DateTime.Today:yyyy-MM-dd}", (await _settings.GetAsync()).StoreName,
            L["Purchase lines"], sub, new XlSheet(L["Purchase lines"], cols, rows, totals));
    }

    /// <summary>The list's own filters (Perlu dicek only = any date), then the supplier; newest first.</summary>
    private async Task<List<PurchaseDto>> LoadAsync()
    {
        var list = Pending ? await _svc.GetNeedingReviewAsync()
                           : await _svc.GetAllAsync(From?.Date, To?.Date.AddDays(1).AddTicks(-1), Search);
        if (SupplierId is { } s) list = list.Where(p => p.SupplierId == s).ToList();
        Total = list.Count;
        return await _svc.GetDetailsAsync(list.Take(Max).Select(p => p.Id));
    }

    public string ExcelUrl => Query("/Purchases/Lines", first: "handler=Excel");
    public string ReviewUrl(Guid id) => Query("/Purchases/Lines", first: $"handler=Review&id={id}");

    /// <summary>This page's filters as a query string on <paramref name="path"/>.</summary>
    public string Query(string path, string? msg = null, bool err = false, string? first = null)
    {
        var q = new List<string>();
        if (first != null) q.Add(first);
        if (From.HasValue) q.Add($"from={From:yyyy-MM-dd}");
        if (To.HasValue) q.Add($"to={To:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        if (SupplierId.HasValue) q.Add($"supplierId={SupplierId}");
        if (Pending) q.Add("pending=true");
        if (msg != null) q.Add("msg=" + Uri.EscapeDataString(msg));
        if (err) q.Add("err=true");
        return path + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
