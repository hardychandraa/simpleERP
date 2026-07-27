using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.CommissionRules;

public class IndexModel : PageModel
{
    private readonly ICommissionService _svc;
    public IndexModel(ICommissionService svc) => _svc = svc;

    public List<CommissionRuleDto> Rules { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Commission Rules";
        Msg = msg; IsErr = err;
        Rules = await _svc.GetRulesAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteRuleAsync(id, User.Identity?.Name ?? "staff");
        return Redirect(result.Success
            ? "/CommissionRules?msg=Commission+rule+deleted."
            : $"/CommissionRules?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
