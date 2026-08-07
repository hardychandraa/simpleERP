using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Pages.Account;

/// <summary>
/// Self-service password change — the only account operation a Staff user can perform.
/// Resetting someone *else's* password is Admin-only and lives on /Settings/Users.
/// </summary>
public class ChangePasswordModel : PageModel
{
    private readonly IUserService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public ChangePasswordModel(IUserService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword     { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public void OnGet() => ViewData["Title"] = _loc["Change Password"].Value;

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = _loc["Change Password"].Value;

        if (NewPassword != ConfirmPassword)
        {
            Msg = _loc["The new passwords do not match."].Value;
            IsErr = true;
            return Page();
        }

        var result = await _svc.ChangeOwnPasswordAsync(User.Identity!.Name!, CurrentPassword, NewPassword);
        if (!result.Success)
        {
            Msg = result.Error;
            IsErr = true;
            return Page();
        }

        Msg = _loc["Password changed."].Value;
        return Page();
    }
}
