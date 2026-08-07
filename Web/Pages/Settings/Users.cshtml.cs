using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Pages.Settings;

/// <summary>
/// Single-page CRUD for login accounts, same shape as SalesPersons and PaymentTerms.
/// Admin-only via the folder convention on /Settings in Program.cs.
///
/// Accounts are deactivated rather than deleted, so there is no Delete handler at all:
/// every Sale, Purchase and AuditLog row stores the username as a plain string, and
/// removing the account would strand that history.
/// </summary>
public class UsersModel : PageModel
{
    private readonly IUserService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public UsersModel(IUserService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    public List<UserDto> Users { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    [BindProperty] public UserDto Input       { get; set; } = new();
    [BindProperty] public string  NewPassword { get; set; } = "";

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = _loc["Users"].Value;
        Msg = msg; IsErr = err;
        Users = await _svc.GetAllAsync();
    }

    private string User_ => User.Identity!.Name!;

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await _svc.CreateAsync(Input, NewPassword, User_);
        return Back(result.Success, result.Error,
            _loc["User '{0}' added.", Input.Username].Value);
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        var result = await _svc.UpdateAsync(Input, User_);
        return Back(result.Success, result.Error, _loc["User updated."].Value);
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(Guid id)
    {
        var result = await _svc.ResetPasswordAsync(id, NewPassword, User_);
        return Back(result.Success, result.Error, _loc["Password reset."].Value);
    }

    private IActionResult Back(bool success, string? error, string okMessage) =>
        Redirect(success
            ? $"/Settings/Users?msg={Uri.EscapeDataString(okMessage)}"
            : $"/Settings/Users?err=true&msg={Uri.EscapeDataString(error!)}");
}
