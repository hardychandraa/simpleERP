using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.RebateRules;

public class IndexModel : PageModel
{
    private readonly IRebateService _svc;
    public IndexModel(IRebateService svc) => _svc = svc;

    public List<RebateRuleDto> Rules { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Rebate Rules";
        Msg = msg; IsErr = err;
        Rules = await _svc.GetRulesAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteRuleAsync(id, User.Identity?.Name ?? "staff");
        return Redirect(result.Success
            ? "/RebateRules?msg=Rebate+rule+deleted."
            : $"/RebateRules?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
