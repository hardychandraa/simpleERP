using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SimpleERP.Web.Pages.CommissionRules;

public class EditModel : PageModel
{
    private readonly ICommissionService _svc;
    private readonly ISalesPersonService _people;
    private readonly IProductService _products;
    public EditModel(ICommissionService svc, ISalesPersonService people, IProductService products)
    { _svc = svc; _people = people; _products = products; }

    [BindProperty] public CommissionRuleDto Input { get; set; } = new();
    public List<SelectListItem> SalesPersonOptions { get; set; } = new();
    public List<SelectListItem> ProductOptions     { get; set; } = new();
    public List<string>         CategoryOptions    { get; set; } = new();
    public string? Error { get; set; }
    public bool    IsNew { get; set; }
    public bool    InUse { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        IsNew = id == null || id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Commission Rule" : "Edit Commission Rule";
        await LoadAsync();

        if (IsNew) { Input = new CommissionRuleDto { IsActive = true, Priority = 0 }; return Page(); }

        var r = await _svc.GetRuleAsync(id!.Value);
        if (r == null) return Redirect("/CommissionRules?err=true&msg=Commission+rule+not+found.");
        Input = r; InUse = r.InUse;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        IsNew = Input.Id == Guid.Empty;
        ViewData["Title"] = IsNew ? "New Commission Rule" : "Edit Commission Rule";

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
        return Redirect($"/CommissionRules?msg={Uri.EscapeDataString($"'{Input.Name}' saved.")}");
    }

    private async Task LoadAsync()
    {
        var people = await _people.GetAllAsync(activeOnly: true);
        SalesPersonOptions = people.Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToList();
        var products = await _products.GetAllActiveAsync();
        ProductOptions = products.Select(p => new SelectListItem($"{p.Name} ({p.SKU})", p.Id.ToString())).ToList();
        CategoryOptions = products.Where(p => !string.IsNullOrWhiteSpace(p.Category))
                                  .Select(p => p.Category!).Distinct().OrderBy(c => c).ToList();
    }
}
