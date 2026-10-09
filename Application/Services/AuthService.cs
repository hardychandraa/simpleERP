using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Credential checking, and the failed-attempt lockout that goes with it.
///
/// The user is told the same thing for every kind of failure — unknown username, wrong
/// password, deactivated account, active lockout — so the form cannot be used to work out
/// which usernames exist or which are disabled. The diagnostic log records the real
/// reason, because that is where the answer belongs when someone reports being unable to
/// get in.
/// </summary>
public class AuthService : IAuthService
{
    /// <summary>Consecutive failures before the account locks.</summary>
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUserRepository     _users;
    private readonly IPasswordHasher     _hasher;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork         _uow;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<AuthService> _log;

    public AuthService(IUserRepository users, IPasswordHasher hasher, IAuditLogRepository audit,
        IUnitOfWork uow, IStringLocalizer<SharedResource> loc, ILogger<AuthService> log)
    { _users = users; _hasher = hasher; _audit = audit; _uow = uow; _loc = loc; _log = log; }

    public async Task<ServiceResult<AuthenticatedUserDto>> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Deny("empty username or password");

        var user = await _users.GetByUsernameAsync(username.Trim());
        if (user == null)          return Deny($"no such user '{username.Trim()}'");
        if (!user.IsActive)        return Deny($"'{user.Username}' is deactivated");

        if (user.LockedUntil != null && user.LockedUntil > DateTime.UtcNow)
            return Deny($"'{user.Username}' is locked out until {user.LockedUntil:u}");

        // The attempt is counted BEFORE the password is checked, in one SQL statement.
        // Counting after the check, on the loaded entity, let 20 guesses sent at once all
        // read 0, all be checked and all write 1: the account never locked (security review
        // R3, 2026-10-09). Now at most LockoutThreshold checks can be in flight per window.
        var attempt = await _users.ReserveLoginAttemptAsync(user.Id, LockoutThreshold, DateTime.UtcNow);
        if (attempt == null)
            return Deny($"'{user.Username}' is locked out (or {LockoutThreshold} attempts already in progress)");

        if (!_hasher.Verify(user.PasswordHash, password))
        {
            var locked = attempt >= LockoutThreshold;
            if (locked)
            {
                var until = DateTime.UtcNow.Add(LockoutDuration);
                await _users.LockOutAsync(user.Id, until);
                // Audited, not just logged: repeated failures against a real account are a
                // business-relevant event, not only a diagnostic one.
                await _audit.LogAsync(user.Username, "Auth.Lockout",
                    $"Locked until {until:u} after {LockoutThreshold} failed attempts");
                await _uow.SaveChangesAsync();
            }
            return Deny(locked
                ? $"'{user.Username}' wrong password, now locked out"
                : $"'{user.Username}' wrong password, attempt {attempt}");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil      = null;
        user.LastLoginAt      = DateTime.UtcNow;
        _users.Update(user);
        await _audit.LogAsync(user.Username, "Auth.Login", user.DisplayName);
        await _uow.SaveChangesAsync();

        return ServiceResult<AuthenticatedUserDto>.Ok(new AuthenticatedUserDto {
            Id = user.Id, Username = user.Username,
            DisplayName = user.DisplayName, Role = user.Role, SecurityStamp = user.SecurityStamp
        });
    }

    public async Task LogoutAsync(string user)
    {
        // A new stamp ends every session of this user, a copy of the cookie included:
        // signing out only told this browser to drop it (security review R4).
        await _users.RotateSecurityStampAsync(user);
        await _audit.LogAsync(user, "Auth.Logout");
        await _uow.SaveChangesAsync();
    }

    /// <summary>
    /// Logs why the attempt really failed, returns the one message every failure shares.
    /// Not <c>Refuse</c>: that deliberately shows the user the same sentence it logs, and
    /// here the two must differ.
    /// </summary>
    private ServiceResult<AuthenticatedUserDto> Deny(string realReason)
    {
        _log.LogWarning("Login refused: {Reason}", realReason);
        return ServiceResult<AuthenticatedUserDto>.Fail(_loc["Invalid username or password."].Value);
    }
}
