using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;
using SimpleERP.Web.Services;

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

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<CreateModel> _log;
    public CreateModel(IPurchaseService purchases, ISupplierService suppliers,
                       IProductService products, IPaymentTermService terms,
                       IAppSettingsService settings, IStringLocalizer<SharedResource> loc,
                       ILogger<CreateModel> log)
    { _purchases=purchases; _suppliers=suppliers; _products=products;
      _terms=terms; _settings=settings;  _loc = loc; _log = log; }

    [BindProperty] public Guid        SupplierId  { get; set; }
    [BindProperty] public string?     SupplierDocumentNumber { get; set; }
    [BindProperty] public DateTime?   PurchaseDate { get; set; }
    [BindProperty] public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    [BindProperty] public Guid?       PaymentTermId { get; set; }
    [BindProperty] public string?     Notes       { get; set; }
    /// <summary>Supplier prices include PPN by default (HC, 2026-10-06); untick when they don't.</summary>
    [BindProperty] public bool        IsTaxInclusive { get; set; } = true;
    [BindProperty] public string      ItemsJson   { get; set; } = "[]";

    [BindProperty] public decimal InvoiceDiscountInput { get; set; }
    [BindProperty] public bool    InvoiceDiscountIsPercent { get; set; }

    /// <summary>
    /// Revise mode (HC, 2026-10-08): the purchase being corrected in place, same number. Saving
    /// re-costs everything posted after it (PurchaseService.ReviseAsync); leaving changes nothing.
    /// </summary>
    [BindProperty(SupportsGet = true)] public Guid? Revise { get; set; }
    /// <summary>Revise mode: also clear "Perlu dicek". On by default, since a revision is the check.</summary>
    [BindProperty] public bool MarkReviewed { get; set; } = true;
    public string? RevisingNumber { get; set; }
    public bool    RevisingNeedsReview { get; set; }

    public List<SupplierDto>    SupplierOptions   { get; set; } = new();
    public List<PaymentTermDto> TermOptions       { get; set; } = new();
    public List<ProductDto>     AvailableProducts { get; set; } = new();
    /// <summary>PPN rate as a fraction, so the summary previews tax exactly as the server computes it.</summary>
    public decimal VatRate { get; set; }
    public string? Error { get; set; }
    /// <summary>The posted lines, put back into the table when a save is refused (as Sales/Create does).</summary>
    public string PrefillItemsJson { get; set; } = "[]";

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = "New Purchase";
        PurchaseDate = DateTime.Now.Date;
        if (Revise.HasValue)
        {
            var p = await _purchases.GetByIdAsync(Revise.Value);
            if (p == null) return RedirectToPage("/Purchases/Index");
            if (p.Status == "Cancelled") return RedirectToPage("/Purchases/Detail", new { id = p.Id });
            RevisingNumber = p.PurchaseNumber; RevisingNeedsReview = p.NeedsReview;
            SupplierId = p.SupplierId; SupplierDocumentNumber = p.SupplierDocumentNumber;
            PurchaseDate = p.PurchaseDate; PaymentType = Enum.Parse<PaymentType>(p.PaymentType);
            PaymentTermId = p.PaymentTermId; Notes = p.Notes; IsTaxInclusive = p.IsTaxInclusive;
            InvoiceDiscountIsPercent = p.InvoiceDiscountPercent.HasValue;
            InvoiceDiscountInput = p.InvoiceDiscountPercent ?? p.InvoiceDiscountAmount;
            PrefillItemsJson = JsonSerializer.Serialize(p.Items.Select(i => new CreatePurchaseItemDto {
                PurchaseItemId = i.Id, ProductId = i.ProductId, Qty = i.Qty, UnitCost = i.UnitCost,
                DiscountAmount = i.DiscountAmount, DiscountPercent = i.DiscountPercent, Notes = i.Notes }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "New Purchase";
        await LoadAsync();
        if (Revise.HasValue) await LoadRevisingAsync();

        List<CreatePurchaseItemDto>? items;
        try { items = JsonSerializer.Deserialize<List<CreatePurchaseItemDto>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException ex)
        {
            // Same reasoning as Sales/Create: this is a client-side defect that costs the
            // operator a whole supplier invoice's worth of entry, and it used to leave no
            // trace at all.
            _log.LogError(ex, "Purchase line items failed to deserialize. Raw payload: {ItemsJson}", ItemsJson);
            Error = _loc["Invalid item data."]; return Page();
        }

        // If this post is refused, the page re-opens with the lines that were typed rather than
        // an empty table (it used to lose them all: a whole supplier invoice to retype, found
        // 2026-10-07). Re-serialized from the parsed items, never echoed raw: it is written
        // into a <script>, and the serializer escapes < > & and quotes.
        PrefillItemsJson = JsonSerializer.Serialize(items ?? new(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (items == null || items.Count == 0) { Error = _loc["Add at least one item."]; return Page(); }
        if (items.Count > 200) { Error = _loc["Too many items in one purchase."]; return Page(); }

        var dto = new CreatePurchaseDto {
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
        };

        if (Revise.HasValue)
        {
            var revised = await _purchases.ReviseAsync(Revise.Value, dto, MarkReviewed, this.CurrentUserName());
            if (!revised.Success) { Error = revised.Error; await LoadRevisingAsync(); return Page(); }
            return RedirectToPage("/Purchases/Detail", new { id = Revise.Value, msg = _loc["Purchase revised."].Value });
        }

        var result = await _purchases.CreateAsync(dto, this.CurrentUserName());
        if (!result.Success) { Error = result.Error; return Page(); }
        return RedirectToPage("/Purchases/Detail", new { id = result.Data!.Id });
    }

    private async Task LoadRevisingAsync()
    {
        var p = await _purchases.GetByIdAsync(Revise!.Value);
        RevisingNumber = p?.PurchaseNumber; RevisingNeedsReview = p?.NeedsReview ?? false;
        // The supplier and payment type are fixed on a revision; the posted values are ignored.
        if (p != null) { SupplierId = p.SupplierId; PaymentType = Enum.Parse<PaymentType>(p.PaymentType); }
    }

    private async Task LoadAsync()
    {
        // Revise mode lists every supplier and product: the one on the purchase may since have
        // been deactivated, and it still has to show.
        SupplierOptions   = await _suppliers.GetAllAsync(activeOnly: !Revise.HasValue);
        TermOptions       = await _terms.GetAllAsync(activeOnly: true);
        AvailableProducts = Revise.HasValue ? await _products.GetAllAsync() : await _products.GetAllActiveAsync();
        VatRate           = (await _settings.GetAsync()).VatRatePercent / 100m;
    }
}
