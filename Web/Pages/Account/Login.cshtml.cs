using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Pages.Account;

/// <summary>
/// The one page reachable without a login. Everything else is denied by default through
/// the folder conventions in Program.cs.
/// </summary>
public class LoginModel : PageModel
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly IStringLocalizer<SharedResource> _loc;
    public LoginModel(IAuthService auth, IUserService users, IStringLocalizer<SharedResource> loc)
    { _auth = auth; _users = users; _loc = loc; }

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = _loc["Sign in"].Value;
        // Already signed in — no reason to show the form again.
        if (User.Identity?.IsAuthenticated == true) return SafeRedirect();
        // Nobody has set the app up yet: a login form here could never be satisfied.
        if (!await _users.AnyUserExistsAsync()) return Redirect("/Account/Setup");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = _loc["Sign in"].Value;

        var result = await _auth.LoginAsync(Username, Password);
        if (!result.Success)
        {
            Error = result.Error;
            return Page();
        }

        var account = result.Data!;
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            // ClaimTypes.Name is what User.Identity?.Name returns — and therefore what
            // every CreatedBy and AuditLog row across the app is stamped with.
            new(ClaimTypes.Name,           account.Username),
            new(ClaimTypes.Role,           account.Role.ToString()),
            new("DisplayName",             account.DisplayName)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        return SafeRedirect();
    }

    /// <summary>
    /// LocalRedirect only — ReturnUrl arrives from the query string, so an absolute URL
    /// here would turn the login page into an open redirect. Same guard as /set-language.
    /// </summary>
    private IActionResult SafeRedirect() =>
        Url.IsLocalUrl(ReturnUrl) ? Redirect(ReturnUrl!) : Redirect("/");
}
