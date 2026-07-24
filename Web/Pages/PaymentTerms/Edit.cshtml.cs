using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.PaymentTerms;

public class EditModel : PageModel
{
    private readonly IPaymentTermService _svc;
    public EditModel(IPaymentTermService svc) => _svc = svc;

    [BindProperty] public PaymentTermDto Input { get; set; } = new();
    public string? Error { get; set; }
    public bool    InUse { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ViewData["Title"] = "Edit Payment Term";
        var t = await _svc.GetByIdAsync(id);
        if (t == null) return Redirect("/PaymentTerms?err=true&msg=Payment+term+not+found.");
        Input = t; InUse = t.InUse;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Edit Payment Term";
        var result = await _svc.UpdateAsync(Input, User.Identity?.Name ?? "staff");
        if (!result.Success)
        {
            Error = result.Error;
            InUse = (await _svc.GetByIdAsync(Input.Id))?.InUse ?? false;
            return Page();
        }
        return Redirect($"/PaymentTerms?msg={Uri.EscapeDataString($"'{Input.Name}' saved.")}");
    }
}
