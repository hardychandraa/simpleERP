using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.Account;

/// <summary>
/// POST only. A logout reachable by GET can be fired by any image tag on any page, which
/// is a nuisance rather than a breach — but it costs nothing to close.
/// </summary>
public class LogoutModel : PageModel
{
    private readonly IAuthService _auth;
    public LogoutModel(IAuthService auth) => _auth = auth;

    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            await _auth.LogoutAsync(User.Identity.Name!);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/Account/Login");
    }
}
