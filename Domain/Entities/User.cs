using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// A login account. Distinct from <see cref="SalesPerson"/> on purpose: that table
/// records who *closed* a deal, this records who *typed* it — the two are frequently
/// different people, which is exactly why SalesPerson was kept decoupled from auth.
///
/// Deactivated accounts are never deleted. Every Sale, Purchase, payment and AuditLog
/// row carries the username as a plain string snapshot, so removing the row would leave
/// that history pointing at nobody.
/// </summary>
public class User {
    public Guid    Id           { get; set; }
    /// <summary>Login name, unique case-insensitively. Stored as typed.</summary>
    public string  Username     { get; set; } = string.Empty;
    /// <summary>PBKDF2 via PasswordHasher&lt;User&gt;. Never a plaintext or reversible value.</summary>
    public string  PasswordHash { get; set; } = string.Empty;
    /// <summary>Shown in the top bar; the username is what gets written to audit rows.</summary>
    public string  DisplayName  { get; set; } = string.Empty;
    public UserRole Role        { get; set; } = UserRole.Staff;
    public bool    IsActive     { get; set; } = true;

    /// <summary>Consecutive failures since the last success. Reset to 0 on any successful login.</summary>
    public int       FailedLoginCount { get; set; }
    /// <summary>Set once the failure threshold is hit; login is refused until it passes.</summary>
    public DateTime? LockedUntil      { get; set; }
    public DateTime? LastLoginAt      { get; set; }
    public DateTime  CreatedAt        { get; set; } = DateTime.UtcNow;
}
