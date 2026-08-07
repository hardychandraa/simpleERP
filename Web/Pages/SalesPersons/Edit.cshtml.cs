using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.SalesPersons;

public class EditModel : PageModel
{
    private readonly ISalesPersonService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public EditModel(ISalesPersonService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    [BindProperty] public SalesPersonDto Input { get; set; } = new();
    public string? Error { get; set; }
    public bool    InUse { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ViewData["Title"] = "Edit Sales Person";
        var p = await _svc.GetByIdAsync(id);
        if (p == null) return Redirect($"/SalesPersons?err=true&msg={Uri.EscapeDataString(_loc["Sales person not found."].Value)}");
        Input = p; InUse = p.InUse;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Edit Sales Person";
        var result = await _svc.UpdateAsync(Input, this.CurrentUserName());
        if (!result.Success)
        {
            Error = result.Error;
            InUse = (await _svc.GetByIdAsync(Input.Id))?.InUse ?? false;
            return Page();
        }
        return Redirect($"/SalesPersons?msg={Uri.EscapeDataString(_loc["'{0}' saved.", Input.Name].Value)}");
    }
}
