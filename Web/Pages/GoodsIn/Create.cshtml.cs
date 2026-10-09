using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.GoodsIn;

/// <summary>
/// Barang Masuk (HC, 2026-10-08): Staff record a supplier delivery without seeing any price —
/// often goods picked up at the supplier and taken straight to a customer. The server costs each
/// line at the product's master harga beli and flags the purchase Perlu dicek for Admin.
/// Nothing price-like is on this page or accepted from it. With ?edit={id} the same page corrects
/// a Barang Masuk entry; every Staff edit flags it Perlu dicek again (HC, 2026-10-09).
/// </summary>
public class CreateModel : PageModel
{
    private readonly IPurchaseService _purchases;
    private readonly ISupplierService _suppliers;
    private readonly IProductService  _products;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<CreateModel> _log;
    public CreateModel(IPurchaseService purchases, ISupplierService suppliers, IProductService products,
        IStringLocalizer<SharedResource> loc, ILogger<CreateModel> log)
    { _purchases = purchases; _suppliers = suppliers; _products = products; _loc = loc; _log = log; }

    [BindProperty] public Guid      SupplierId             { get; set; }
    [BindProperty] public string?   SupplierDocumentNumber { get; set; }
    [BindProperty] public DateTime? PurchaseDate           { get; set; }
    [BindProperty] public string?   Notes                  { get; set; }
    [BindProperty] public string    ItemsJson              { get; set; } = "[]";
    /// <summary>Edit mode: the Barang Masuk entry being corrected.</summary>
    [BindProperty(SupportsGet = true)] public Guid? Edit { get; set; }
    public string? EditingNumber { get; set; }
    public string? EditingSupplier { get; set; }

    /// <summary>Names only: never anything from the supplier master beyond what identifies it.</summary>
    public List<(Guid Id, string Name)> SupplierOptions { get; set; } = new();
    /// <summary>Id, name, SKU, stock. No cost of any kind.</summary>
    public List<(Guid Id, string Name, string Sku, decimal Stock)> ProductOptions { get; set; } = new();
    public string? Error { get; set; }
    public string  PrefillItemsJson { get; set; } = "[]";

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = "Goods In";
        PurchaseDate = DateTime.Now.Date;
        if (Edit.HasValue)
        {
            var p = await _purchases.GetQtyDetailAsync(Edit.Value);
            if (p == null) return RedirectToPage("/GoodsIn/Index");
            if (!p.EnteredWithoutPrice || p.Status == "Cancelled")
                return RedirectToPage("/GoodsIn/Detail", new { id = p.Id, msg = _loc["This purchase cannot be edited here."].Value, err = true });
            EditingNumber = p.PurchaseNumber; EditingSupplier = p.SupplierName;
            SupplierId = p.SupplierId; SupplierDocumentNumber = p.SupplierDocumentNumber;
            PurchaseDate = p.PurchaseDate; Notes = p.Notes;
            PrefillItemsJson = JsonSerializer.Serialize(p.Lines.Select(l => new UnpricedPurchaseLineDto {
                PurchaseItemId = l.PurchaseItemId, ProductId = l.ProductId, Qty = l.Qty, Notes = l.Notes }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Goods In";
        await LoadAsync();

        List<UnpricedPurchaseLineDto>? lines;
        try { lines = JsonSerializer.Deserialize<List<UnpricedPurchaseLineDto>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Goods-in lines failed to deserialize. Raw payload: {ItemsJson}", ItemsJson);
            Error = _loc["Invalid item data."]; return Page();
        }
        // Re-serialized from what was parsed (any posted price field is simply not part of the DTO).
        PrefillItemsJson = JsonSerializer.Serialize(lines ?? new(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (lines == null || lines.Count == 0) { Error = _loc["Add at least one item."]; return Page(); }
        if (lines.Count > 200) { Error = _loc["Too many items in one purchase."]; return Page(); }

        var dto = new CreateUnpricedPurchaseDto {
            SupplierId = SupplierId, SupplierDocumentNumber = SupplierDocumentNumber,
            PurchaseDate = PurchaseDate, Notes = Notes, Items = lines };

        if (Edit.HasValue)
        {
            var edited = await _purchases.EditUnpricedAsync(Edit.Value, dto, this.CurrentUserName());
            if (!edited.Success)
            {
                Error = edited.Error;
                var p = await _purchases.GetQtyDetailAsync(Edit.Value);
                EditingNumber = p?.PurchaseNumber; EditingSupplier = p?.SupplierName;
                return Page();
            }
            return RedirectToPage("/GoodsIn/Detail", new { id = Edit.Value, msg = _loc["Saved. It is marked for checking again."].Value });
        }

        var created = await _purchases.CreateUnpricedAsync(dto, this.CurrentUserName());
        if (!created.Success) { Error = created.Error; return Page(); }
        return RedirectToPage("/GoodsIn/Detail", new { id = created.Data, msg = _loc["Goods received. Stock is updated; an administrator will check the prices."].Value });
    }

    private async Task LoadAsync()
    {
        SupplierOptions = (await _suppliers.GetAllAsync(activeOnly: true)).Select(s => (s.Id, s.Name)).ToList();
        // Only id, name, SKU and stock leave this method: the DTO's cost fields are never copied.
        var products = Edit.HasValue ? await _products.GetAllAsync() : await _products.GetAllActiveAsync();
        ProductOptions = products.OrderBy(p => p.Name).Select(p => (p.Id, p.Name, p.SKU, p.CurrentStock)).ToList();
    }
}
