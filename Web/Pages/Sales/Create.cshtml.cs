using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;

namespace SimpleERP.Web.Pages.Sales;

public class CreateModel : PageModel
{
    private readonly ISaleService     _sales;
    private readonly ICustomerService _customers;
    private readonly IProductService  _products;

    public CreateModel(ISaleService sales, ICustomerService customers, IProductService products)
    { _sales=sales; _customers=customers; _products=products; }

    [BindProperty] public Guid        CustomerId  { get; set; }
    [BindProperty] public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    [BindProperty] public string?     Notes       { get; set; }
    [BindProperty] public string      ItemsJson   { get; set; } = "[]";
    /// <summary>True if the entered prices already include PPN. Default: PPN added on top.</summary>
    [BindProperty] public bool        IsTaxInclusive { get; set; }

    public List<SelectListItem> CustomerOptions   { get; set; } = new();
    public List<ProductDto>     AvailableProducts { get; set; } = new();
    public string? Error { get; set; }

    public async Task OnGetAsync() { ViewData["Title"]="New Sale"; await LoadAsync(); }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "New Sale";
        await LoadAsync();

        // Sanitize notes
        var notes = Notes?.Trim();
        if (notes?.Length > 500) notes = notes[..500];

        List<CreateSaleItemDto>? items;
        try { items = JsonSerializer.Deserialize<List<CreateSaleItemDto>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { Error = "Invalid item data."; return Page(); }

        if (items == null || items.Count == 0) { Error = "Add at least one item."; return Page(); }
        if (items.Count > 100) { Error = "Too many items in one sale."; return Page(); }

        // Determine user — in a 2-user system the role comes from a simple claim or falls back to "staff"
        var user = User.Identity?.Name ?? "staff";

        var result = await _sales.CreateAsync(new CreateSaleDto {
            CustomerId     = CustomerId,
            PaymentType    = PaymentType,
            Notes          = notes,
            IsTaxInclusive = IsTaxInclusive,
            Items          = items
        }, user);

        if (!result.Success) { Error = result.Error; return Page(); }
        return RedirectToPage("/Sales/Detail", new { id = result.Data!.Id });
    }

    private async Task LoadAsync()
    {
        var customers = await _customers.GetAllActiveAsync();
        CustomerOptions = customers.Select(c => new SelectListItem(
            $"{c.Name}{(string.IsNullOrEmpty(c.Phone) ? "" : $"  ({c.Phone})")}",
            c.Id.ToString())).ToList();
        AvailableProducts = await _products.GetAllActiveAsync();
    }
}
