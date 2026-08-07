using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// CRUD over the login accounts. Accounts are deactivated, never deleted — every Sale,
/// Purchase, payment and AuditLog row stores the username as a plain string, so removing
/// the account would leave that history pointing at a name nobody can look up.
///
/// The last active Admin cannot be demoted or deactivated. Without that guard a single
/// mis-click locks everyone out of Settings, user management and the reports permanently,
/// with no recovery path short of editing the database by hand.
/// </summary>
public class UserService : IUserService
{
    private const int MinPasswordLength = 8;

    private readonly IUserRepository     _users;
    private readonly IPasswordHasher     _hasher;
    private readonly IAuditLogRepository _audit;
    private readonly IUnitOfWork         _uow;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<UserService> _log;

    public UserService(IUserRepository users, IPasswordHasher hasher, IAuditLogRepository audit,
        IUnitOfWork uow, IStringLocalizer<SharedResource> loc, ILogger<UserService> log)
    { _users = users; _hasher = hasher; _audit = audit; _uow = uow; _loc = loc; _log = log; }

    public Task<bool> AnyUserExistsAsync() => _users.AnyAsync();

    public async Task<ServiceResult> CreateFirstAdminAsync(string username, string displayName, string password)
    {
        // Re-checked here, not only on the page: this is the one account-creation path
        // reachable without being signed in, so the guard has to live where it cannot be
        // bypassed by posting straight to the handler.
        if (await _users.AnyAsync())
            return _log.Refuse(_loc["Setup has already been completed."]);

        return await CreateAsync(
            new UserDto { Username = username, DisplayName = displayName,
                          Role = UserRole.Admin, IsActive = true },
            password, "setup");
    }

    public async Task<List<UserDto>> GetAllAsync(bool activeOnly = false)
    {
        var list = await _users.GetAllAsync(activeOnly);
        var dtos = new List<UserDto>(list.Count);
        foreach (var u in list)
            dtos.Add(Map(u, await _users.IsLastActiveAdminAsync(u.Id)));
        return dtos;
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var u = await _users.GetByIdAsync(id);
        return u == null ? null : Map(u, await _users.IsLastActiveAdminAsync(u.Id));
    }

    public async Task<ServiceResult> CreateAsync(UserDto dto, string password, string user)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            return _log.Refuse(_loc["Username is required."]);
        if (dto.Username.Trim().Length > 100)
            return _log.Refuse(_loc["Username cannot exceed 100 characters."]);
        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            return _log.Refuse(_loc["Display name is required."]);
        if (dto.DisplayName.Trim().Length > 200)
            return _log.Refuse(_loc["Display name cannot exceed 200 characters."]);
        if (await _users.UsernameExistsAsync(dto.Username.Trim()))
            return _log.Refuse(_loc["A user named '{0}' already exists.", dto.Username.Trim()]);

        var badPassword = ValidatePassword(password);
        if (badPassword != null) return badPassword;

        var account = new User {
            Id           = Guid.NewGuid(),
            Username     = dto.Username.Trim(),
            DisplayName  = dto.DisplayName.Trim(),
            PasswordHash = _hasher.Hash(password),
            Role         = dto.Role,
            IsActive     = dto.IsActive,
            CreatedAt    = DateTime.UtcNow
        };
        await _users.AddAsync(account);
        await _audit.LogAsync(user, "User.Create", $"{account.Username} ({account.Role})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UserDto dto, string user)
    {
        var account = await _users.GetByIdAsync(dto.Id);
        if (account == null) return _log.Refuse(_loc["User not found."]);

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            return _log.Refuse(_loc["Display name is required."]);
        if (dto.DisplayName.Trim().Length > 200)
            return _log.Refuse(_loc["Display name cannot exceed 200 characters."]);

        var losingAdmin = account.Role == UserRole.Admin
                       && (dto.Role != UserRole.Admin || !dto.IsActive);
        if (losingAdmin && await _users.IsLastActiveAdminAsync(account.Id))
            return _log.Refuse(_loc["'{0}' is the only active administrator. Give another user the Admin role first.", account.Username]);

        var before = $"{account.DisplayName} ({account.Role}, active={account.IsActive})";
        account.DisplayName = dto.DisplayName.Trim();
        account.Role        = dto.Role;
        account.IsActive    = dto.IsActive;
        // Reactivating clears a lockout — an admin turning the account back on is a
        // deliberate act, and leaving the old timer running would silently refuse them.
        if (account.IsActive) {
            account.LockedUntil      = null;
            account.FailedLoginCount = 0;
        }

        _users.Update(account);
        await _audit.LogAsync(user, "User.Update",
            $"{account.Username}: {before} -> {account.DisplayName} ({account.Role}, active={account.IsActive})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ResetPasswordAsync(Guid id, string newPassword, string user)
    {
        var account = await _users.GetByIdAsync(id);
        if (account == null) return _log.Refuse(_loc["User not found."]);

        var bad = ValidatePassword(newPassword);
        if (bad != null) return bad;

        account.PasswordHash     = _hasher.Hash(newPassword);
        account.LockedUntil      = null;
        account.FailedLoginCount = 0;
        _users.Update(account);
        // The password itself is never written to the audit trail, only that it changed.
        await _audit.LogAsync(user, "User.ResetPassword", account.Username);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ChangeOwnPasswordAsync(string username, string currentPassword, string newPassword)
    {
        var account = await _users.GetByUsernameAsync(username);
        if (account == null) return _log.Refuse(_loc["User not found."]);

        if (!_hasher.Verify(account.PasswordHash, currentPassword))
            return _log.Refuse(_loc["Current password is incorrect."]);

        var bad = ValidatePassword(newPassword);
        if (bad != null) return bad;

        if (_hasher.Verify(account.PasswordHash, newPassword))
            return _log.Refuse(_loc["The new password must be different from the current one."]);

        account.PasswordHash = _hasher.Hash(newPassword);
        _users.Update(account);
        await _audit.LogAsync(account.Username, "User.ChangePassword", account.Username);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private ServiceResult? ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return _log.Refuse(_loc["Password is required."]);
        if (password.Length < MinPasswordLength)
            return _log.Refuse(_loc["Password must be at least {0} characters.", MinPasswordLength]);
        return null;
    }

    private static UserDto Map(User u, bool isLastAdmin) => new() {
        Id          = u.Id,
        Username    = u.Username,
        DisplayName = u.DisplayName,
        Role        = u.Role,
        IsActive    = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        IsLockedOut = u.LockedUntil != null && u.LockedUntil > DateTime.UtcNow,
        IsLastAdmin = isLastAdmin
    };
}
