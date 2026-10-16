using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Sales;

public class CreateModel : PageModel
{
    private readonly ISaleService        _sales;
    private readonly ICustomerService    _customers;
    private readonly IProductService     _products;
    private readonly IPaymentTermService _terms;
    private readonly ISalesPersonService _people;
    private readonly IAppSettingsService _settings;

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<CreateModel> _log;
    public CreateModel(ISaleService sales, ICustomerService customers,
                       IProductService products, IPaymentTermService terms,
                       ISalesPersonService people, IAppSettingsService settings, IStringLocalizer<SharedResource> loc,
                       ILogger<CreateModel> log)
    { _sales=sales; _customers=customers; _products=products; _terms=terms;
      _people=people; _settings=settings;  _loc = loc; _log = log; }

    [BindProperty] public Guid        CustomerId  { get; set; }
    /// <summary>Business date of the sale. Defaults to today; may be backdated for
    /// catch-up entry. Future dates are refused by the service.</summary>
    [BindProperty] public DateTime?   SaleDate    { get; set; }
    [BindProperty] public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    [BindProperty] public string?     Notes       { get; set; }
    [BindProperty] public string      ItemsJson   { get; set; } = "[]";
    /// <summary>True if the entered prices already include PPN. Default: included, which is how
    /// KJ quotes every price (HC, 2026-10-06). Revise copies the original's setting instead.</summary>
    [BindProperty] public bool        IsTaxInclusive { get; set; } = true;
    /// <summary>Selected credit term. Empty = open credit with no agreed due date.</summary>
    [BindProperty] public Guid?       PaymentTermId  { get; set; }
    /// <summary>Who to credit. Empty = unattributed; the service treats that as valid.</summary>
    [BindProperty] public Guid?       SalesPersonId  { get; set; }

    /// <summary>Flat whole-invoice discount, or a percent when InvoiceDiscountIsPercent.</summary>
    [BindProperty] public decimal InvoiceDiscountInput { get; set; }
    [BindProperty] public bool    InvoiceDiscountIsPercent { get; set; }

    /// <summary>
    /// Revise mode: the invoice being corrected. Saving cancels it and creates this one in
    /// a single transaction (SaleService.ReviseAsync); leaving the page changes nothing.
    /// </summary>
    [BindProperty(SupportsGet = true)] public Guid? Revise { get; set; }
    /// <summary>Why the invoice is being revised: required from Staff (G2, HC 2026-10-14).</summary>
    [BindProperty] public string? ChangeReason { get; set; }
    /// <summary>
    /// This form's one-time id (G9): set when the page opens, posted back, and stored on the
    /// sale, so a double click or a resubmit opens the sale already made instead of a second one.
    /// </summary>
    [BindProperty] public Guid? SubmissionId { get; set; }
    public string? RevisingInvoiceNumber { get; set; }
    /// <summary>Line items to load into the page on open, in the shape the page posts.</summary>
    public string PrefillItemsJson { get; set; } = "[]";

    public List<PaymentTermDto> TermOptions { get; set; } = new();
    public List<SalesPersonDto> SalesPersonOptions { get; set; } = new();
    /// <summary>PPN rate as a fraction, so the summary can preview tax the same way the server computes it.</summary>
    public decimal VatRate { get; set; }

    public List<SelectListItem> CustomerOptions   { get; set; } = new();
    /// <summary>Each customer's sale defaults (salesperson, term, item discount %), by id.</summary>
    public List<CustomerDto>    Customers         { get; set; } = new();
    public List<ProductDto>     AvailableProducts { get; set; } = new();
    /// <summary>
    /// Each customer's plafon, open balance and overdue invoices, for Admin's credit panel
    /// (HC, 2026-10-15). Null for Staff: receivables never reach their page, not even hidden.
    /// </summary>
    public Dictionary<Guid, CustomerCreditDto>? Credit { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Revise is Admin's (R3, HC 2026-10-16): Staff send a request from the invoice instead.
        if (Revise.HasValue && !User.ChecksStaffWork()) return this.Refuse("revising a sale is Admin-only; Staff send a request");
        ViewData["Title"] = "New Sale";
        SaleDate = DateTime.Now.Date;   // business-local today
        SubmissionId = Guid.NewGuid();

        if (Revise.HasValue)
        {
            var original = await _sales.GetByIdAsync(Revise.Value);
            if (original == null || original.Status != "Active")
                return RedirectToPage("/Sales/Detail", new { id = Revise.Value });

            RevisingInvoiceNumber = original.InvoiceNumber;
            CustomerId     = original.CustomerId;
            SaleDate       = original.SaleDate.ToLocalTime().Date;
            PaymentType    = Enum.Parse<PaymentType>(original.PaymentType);
            PaymentTermId  = original.PaymentTermId;
            SalesPersonId  = original.SalesPersonId;
            IsTaxInclusive = original.IsTaxInclusive;
            Notes          = original.Notes;
            InvoiceDiscountIsPercent = original.InvoiceDiscountPercent is > 0;
            InvoiceDiscountInput     = original.InvoiceDiscountPercent is > 0
                                     ? original.InvoiceDiscountPercent.Value : original.InvoiceDiscountAmount;
            PrefillItemsJson = JsonSerializer.Serialize(original.Items.Select(i => new CreateSaleItemDto {
                ProductId = i.ProductId, Qty = i.Qty, UnitPrice = i.UnitPrice,
                DiscountAmount = i.DiscountAmount, DiscountPercent = i.DiscountPercent,
                WarrantyMonths = i.WarrantyMonths, Notes = i.Notes, PriceReason = i.PriceReason
            }), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Revise.HasValue && !User.ChecksStaffWork()) return this.Refuse("revising a sale is Admin-only; Staff send a request");
        ViewData["Title"] = "New Sale";
        await LoadAsync();

        // Sanitize notes
        var notes = Notes?.Trim();
        if (notes?.Length > 500) notes = notes[..500];

        List<CreateSaleItemDto>? items;
        try { items = JsonSerializer.Deserialize<List<CreateSaleItemDto>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException ex)
        {
            // The line items are built client-side and posted as JSON, so this firing means
            // the page's own JavaScript produced something malformed — a real defect, not
            // user error, and it costs the operator a whole invoice's worth of typing.
            // It used to be discarded entirely, leaving nothing to diagnose. The payload is
            // logged because the fault is almost always in one specific line's shape, and
            // it is the customer's own order data, not credentials.
            _log.LogError(ex, "Sale line items failed to deserialize. Raw payload: {ItemsJson}", ItemsJson);
            Error = _loc["Invalid item data."]; return Page();
        }

        // If this post is refused, the page re-opens with the lines that were typed rather
        // than an empty table. Re-serialized from the parsed items, never echoed raw: it is
        // written into a <script>, and the serializer escapes < > & and quotes.
        PrefillItemsJson = JsonSerializer.Serialize(items ?? new(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        if (items == null || items.Count == 0) { Error = _loc["Add at least one item."]; return Page(); }
        if (items.Count > 100) { Error = _loc["Too many items in one sale."]; return Page(); }

        var user = this.CurrentUserName();

        var dto = new CreateSaleDto {
            SubmissionId   = SubmissionId,
            CustomerId     = CustomerId,
            SaleDate       = SaleDate,
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
        };
        var admin  = User.ChecksStaffWork();
        var result = Revise.HasValue
            ? await _sales.ReviseAsync(Revise.Value, dto, user, admin, ChangeReason)
            : await _sales.CreateAsync(dto, user, admin);

        if (!result.Success) { Error = result.Error; return Page(); }
        return RedirectToPage("/Sales/Detail", new { id = result.Data!.Id });
    }

    private async Task LoadAsync()
    {
        var customers = await _customers.GetAllActiveAsync();
        Customers = customers;
        CustomerOptions = customers.Select(c => new SelectListItem(
            $"{c.Name}{(string.IsNullOrEmpty(c.Phone) ? "" : $"  ({c.Phone})")}",
            c.Id.ToString())).ToList();
        AvailableProducts = await _products.GetAllActiveAsync();
        if (Revise.HasValue && await _sales.GetByIdAsync(Revise.Value) is { Status: "Active" } original)
        {
            RevisingInvoiceNumber = original.InvoiceNumber;
            // Saving returns the original's goods to stock before the replacement takes
            // them, so the page's stock check must count them as available — otherwise
            // an unchanged line would be refused.
            foreach (var p in AvailableProducts)
                p.CurrentStock += original.Items.Where(i => i.ProductId == p.Id).Sum(i => i.Qty);
        }
        if (User.SeesBusinessTotals()) Credit = await _sales.GetCreditStatusesAsync(excludeSaleId: Revise);
        TermOptions = await _terms.GetAllAsync(activeOnly: true);
        SalesPersonOptions = await _people.GetAllAsync(activeOnly: true);
        VatRate     = (await _settings.GetAsync()).VatRatePercent / 100m;
    }
}
