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
    private readonly ISaleService        _sales;
    private readonly ICustomerService    _customers;
    private readonly IProductService     _products;
    private readonly IPaymentTermService _terms;
    private readonly ISalesPersonService _people;
    private readonly IAppSettingsService _settings;

    public CreateModel(ISaleService sales, ICustomerService customers,
                       IProductService products, IPaymentTermService terms,
                       ISalesPersonService people, IAppSettingsService settings)
    { _sales=sales; _customers=customers; _products=products; _terms=terms;
      _people=people; _settings=settings; }

    [BindProperty] public Guid        CustomerId  { get; set; }
    [BindProperty] public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    [BindProperty] public string?     Notes       { get; set; }
    [BindProperty] public string      ItemsJson   { get; set; } = "[]";
    /// <summary>True if the entered prices already include PPN. Default: PPN added on top.</summary>
    [BindProperty] public bool        IsTaxInclusive { get; set; }
    /// <summary>Selected credit term. Empty = open credit with no agreed due date.</summary>
    [BindProperty] public Guid?       PaymentTermId  { get; set; }
    /// <summary>Who to credit. Empty = unattributed; the service treats that as valid.</summary>
    [BindProperty] public Guid?       SalesPersonId  { get; set; }

    /// <summary>Flat whole-invoice discount, or a percent when InvoiceDiscountIsPercent.</summary>
    [BindProperty] public decimal InvoiceDiscountInput { get; set; }
    [BindProperty] public bool    InvoiceDiscountIsPercent { get; set; }

    public List<PaymentTermDto> TermOptions { get; set; } = new();
    public List<SalesPersonDto> SalesPersonOptions { get; set; } = new();
    /// <summary>PPN rate as a fraction, so the summary can preview tax the same way the server computes it.</summary>
    public decimal VatRate { get; set; }

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
            PaymentTermId  = PaymentTermId,
            SalesPersonId  = SalesPersonId,
            Notes          = notes,
            IsTaxInclusive = IsTaxInclusive,
            // Only one of the two is sent; the service resolves a percent into an
            // amount itself rather than trusting anything computed on the client.
            InvoiceDiscountAmount  = InvoiceDiscountIsPercent ? 0m : InvoiceDiscountInput,
            InvoiceDiscountPercent = InvoiceDiscountIsPercent ? InvoiceDiscountInput : null,
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
        TermOptions = await _terms.GetAllAsync(activeOnly: true);
        SalesPersonOptions = await _people.GetAllAsync(activeOnly: true);
        VatRate     = (await _settings.GetAsync()).VatRatePercent / 100m;
    }
}
