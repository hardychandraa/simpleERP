using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// Builds one supplier invoice as a whole document — add every line, then post once —
/// mirroring Sales/Create. Deliberately not the shape of the old Stock In page, which
/// received one product per submission and recorded the supplier as a free-text note.
/// </summary>
public class CreateModel : PageModel
{
    private readonly IPurchaseService    _purchases;
    private readonly ISupplierService    _suppliers;
    private readonly IProductService     _products;
    private readonly IPaymentTermService _terms;
    private readonly IAppSettingsService _settings;

    public CreateModel(IPurchaseService purchases, ISupplierService suppliers,
                       IProductService products, IPaymentTermService terms,
                       IAppSettingsService settings)
    { _purchases=purchases; _suppliers=suppliers; _products=products;
      _terms=terms; _settings=settings; }

    [BindProperty] public Guid        SupplierId  { get; set; }
    [BindProperty] public string?     SupplierDocumentNumber { get; set; }
    [BindProperty] public DateTime?   PurchaseDate { get; set; }
    [BindProperty] public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    [BindProperty] public Guid?       PaymentTermId { get; set; }
    [BindProperty] public string?     Notes       { get; set; }
    [BindProperty] public bool        IsTaxInclusive { get; set; }
    [BindProperty] public string      ItemsJson   { get; set; } = "[]";

    [BindProperty] public decimal InvoiceDiscountInput { get; set; }
    [BindProperty] public bool    InvoiceDiscountIsPercent { get; set; }

    public List<SupplierDto>    SupplierOptions   { get; set; } = new();
    public List<PaymentTermDto> TermOptions       { get; set; } = new();
    public List<ProductDto>     AvailableProducts { get; set; } = new();
    /// <summary>PPN rate as a fraction, so the summary previews tax exactly as the server computes it.</summary>
    public decimal VatRate { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "New Purchase";
        PurchaseDate = DateTime.Now.Date;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "New Purchase";
        await LoadAsync();

        List<CreatePurchaseItemDto>? items;
        try { items = JsonSerializer.Deserialize<List<CreatePurchaseItemDto>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { Error = "Invalid item data."; return Page(); }

        if (items == null || items.Count == 0) { Error = "Add at least one item."; return Page(); }
        if (items.Count > 200) { Error = "Too many items in one purchase."; return Page(); }

        var result = await _purchases.CreateAsync(new CreatePurchaseDto {
            SupplierId             = SupplierId,
            SupplierDocumentNumber = SupplierDocumentNumber,
            PurchaseDate           = PurchaseDate,
            PaymentType            = PaymentType,
            PaymentTermId          = PaymentTermId,
            Notes                  = Notes,
            IsTaxInclusive         = IsTaxInclusive,
            // Only one of the two is sent; the service resolves a percent itself
            // rather than trusting anything the client computed.
            InvoiceDiscountAmount  = InvoiceDiscountIsPercent ? 0m : InvoiceDiscountInput,
            InvoiceDiscountPercent = InvoiceDiscountIsPercent ? InvoiceDiscountInput : null,
            Items                  = items
        }, User.Identity?.Name ?? "staff");

        if (!result.Success) { Error = result.Error; return Page(); }
        return RedirectToPage("/Purchases/Detail", new { id = result.Data!.Id });
    }

    private async Task LoadAsync()
    {
        SupplierOptions   = await _suppliers.GetAllAsync(activeOnly: true);
        TermOptions       = await _terms.GetAllAsync(activeOnly: true);
        AvailableProducts = await _products.GetAllActiveAsync();
        VatRate           = (await _settings.GetAsync()).VatRatePercent / 100m;
    }
}
