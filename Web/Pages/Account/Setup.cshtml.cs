using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Pages.Account;

/// <summary>
/// Creates the first administrator, and only ever that one.
///
/// Without this the app is unusable on a fresh database: every page requires a login, and
/// creating a login requires an Admin. The alternatives were a seeded default password
/// (which survives forever if nobody changes it) or a documented manual SQL insert (which
/// needs database credentials to install the app). This page closes itself permanently the
/// moment an account exists — the check is in the service too, so posting to the handler
/// directly does not get around it.
/// </summary>
public class SetupModel : PageModel
{
    private readonly IUserService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public SetupModel(IUserService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    [BindProperty] public string Username        { get; set; } = "";
    [BindProperty] public string DisplayName     { get; set; } = "";
    [BindProperty] public string Password        { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = _loc["First-time setup"].Value;
        if (await _svc.AnyUserExistsAsync()) return Redirect("/Account/Login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = _loc["First-time setup"].Value;
        if (await _svc.AnyUserExistsAsync()) return Redirect("/Account/Login");

        if (Password != ConfirmPassword)
        {
            Error = _loc["The new passwords do not match."].Value;
            return Page();
        }

        var result = await _svc.CreateFirstAdminAsync(Username, DisplayName, Password);
        if (!result.Success)
        {
            Error = result.Error;
            return Page();
        }

        return Redirect("/Account/Login");
    }
}
