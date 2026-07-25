using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SimpleERP.Web.Pages.RebateRules;

public class EditModel : PageModel
{
    private readonly IRebateService   _svc;
    private readonly ISupplierService _suppliers;
    private readonly IProductService  _products;
    public EditModel(IRebateService svc, ISupplierService suppliers, IProductService products)
    { _svc = svc; _suppliers = suppliers; _products = products; }

    [BindProperty] public RebateRuleDto Input { get; set; } = new();
    public List<SelectListItem> SupplierOptions { get; set; } = new();
    public List<SelectListItem> ProductOptions  { get; set; } = new();
    public string? Error { get; set; }
    public bool    IsNew { get; set; }
    public bool    InUse { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        IsNew = id == null || id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Rebate Rule" : "Edit Rebate Rule";
        await LoadAsync();

        if (IsNew) { Input = new RebateRuleDto { IsActive = true }; return Page(); }

        var r = await _svc.GetRuleAsync(id!.Value);
        if (r == null) return Redirect("/RebateRules?err=true&msg=Rebate+rule+not+found.");
        Input = r; InUse = r.InUse;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsNew = Input.Id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Rebate Rule" : "Edit Rebate Rule";

        var result = IsNew
            ? await _svc.CreateRuleAsync(Input, User.Identity?.Name ?? "staff")
            : await _svc.UpdateRuleAsync(Input, User.Identity?.Name ?? "staff");

        if (!result.Success)
        {
            Error = result.Error;
            await LoadAsync();
            if (!IsNew) InUse = (await _svc.GetRuleAsync(Input.Id))?.InUse ?? false;
            return Page();
        }
        return Redirect($"/RebateRules?msg={Uri.EscapeDataString($"'{Input.Name}' saved.")}");
    }

    private async Task LoadAsync()
    {
        var suppliers = await _suppliers.GetAllAsync(activeOnly: true);
        SupplierOptions = suppliers.Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
        var products = await _products.GetAllActiveAsync();
        ProductOptions = products.Select(p => new SelectListItem($"{p.Name} ({p.SKU})", p.Id.ToString())).ToList();
    }
}
