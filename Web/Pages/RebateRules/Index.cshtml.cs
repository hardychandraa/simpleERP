using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.RebateRules;

public class IndexModel : PageModel
{
    private readonly IRebateService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public IndexModel(IRebateService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

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
            ? $"/RebateRules?msg={Uri.EscapeDataString(_loc["Rebate rule deleted."].Value)}"
            : $"/RebateRules?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
