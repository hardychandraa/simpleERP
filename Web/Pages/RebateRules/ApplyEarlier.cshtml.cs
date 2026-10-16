using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.RebateRules;

/// <summary>
/// Applies a rebate rule to purchases posted before it existed: GET previews what it would
/// accrue (nothing written), POST writes exactly that and shows the result.
/// </summary>
public class ApplyEarlierModel : PageModel
{
    private readonly IRebateService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public ApplyEarlierModel(IRebateService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    public RebateBackfillDto? Result { get; set; }
    /// <summary>True after a successful apply: Result lists what was written.</summary>
    public bool Applied { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var r = await _svc.PreviewApplyToEarlierAsync(id);
        if (!r.Success)
            return Redirect($"/RebateRules?err=true&msg={Uri.EscapeDataString(r.Error!)}");
        Result = r.Data;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var r = await _svc.ApplyToEarlierAsync(id, this.CurrentUserName());
        if (!r.Success)
        {
            // Show the refusal over a fresh preview, so the page still says where things stand.
            Error = r.Error;
            var preview = await _svc.PreviewApplyToEarlierAsync(id);
            if (!preview.Success)
                return Redirect($"/RebateRules?err=true&msg={Uri.EscapeDataString(r.Error!)}");
            Result = preview.Data;
            return Page();
        }
        Result = r.Data;
        Applied = true;
        return Page();
    }
}
