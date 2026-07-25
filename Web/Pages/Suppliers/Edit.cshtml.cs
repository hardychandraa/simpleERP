using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Suppliers;

/// <summary>
/// Create and edit in one page: id absent = new. Suppliers carry enough fields
/// (address, NPWP, default term, notes) that the inline-row form used for the
/// smaller master tables would be cramped.
/// </summary>
public class EditModel : PageModel
{
    private readonly ISupplierService    _svc;
    private readonly IPaymentTermService _terms;
    public EditModel(ISupplierService svc, IPaymentTermService terms)
    { _svc = svc; _terms = terms; }

    [BindProperty] public SupplierDto Input { get; set; } = new();
    public List<PaymentTermDto> TermOptions { get; set; } = new();
    public string? Error { get; set; }
    public bool    IsNew { get; set; }
    public bool    InUse { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        IsNew = id == null || id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Supplier" : "Edit Supplier";
        await LoadAsync();

        if (IsNew) { Input = new SupplierDto { IsActive = true }; return Page(); }

        var s = await _svc.GetByIdAsync(id!.Value);
        if (s == null) return Redirect("/Suppliers?err=true&msg=Supplier+not+found.");
        Input = s; InUse = s.InUse;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsNew = Input.Id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Supplier" : "Edit Supplier";

        var result = IsNew
            ? await _svc.CreateAsync(Input, User.Identity?.Name ?? "staff")
            : await _svc.UpdateAsync(Input, User.Identity?.Name ?? "staff");

        if (!result.Success)
        {
            Error = result.Error;
            await LoadAsync();
            if (!IsNew) InUse = (await _svc.GetByIdAsync(Input.Id))?.InUse ?? false;
            return Page();
        }
        return Redirect($"/Suppliers?msg={Uri.EscapeDataString($"'{Input.Name}' saved.")}");
    }

    private async Task LoadAsync() => TermOptions = await _terms.GetAllAsync(activeOnly: true);
}
