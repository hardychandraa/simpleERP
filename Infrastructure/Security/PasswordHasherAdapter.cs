using Microsoft.AspNetCore.Identity;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;

namespace SimpleERP.Infrastructure.Security;

/// <summary>
/// ASP.NET Core Identity's own password hasher, used on its own — PBKDF2-HMAC-SHA512,
/// 100k iterations, per-password salt, constant-time comparison, all handled by the
/// framework rather than hand-rolled here.
///
/// Only this class touches the Identity package. Nothing else in the app knows the hash
/// format, so adopting full Identity later would keep every stored hash valid.
///
/// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> counts as success: it
/// means the hash is valid but was produced by an older iteration count. Silently
/// upgrading it would need a write on the login path, which is not worth the complexity
/// for a handful of accounts — every hash here is written by the current version anyway.
/// </summary>
public class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly PasswordHasher<User> Hasher = new();

    public string Hash(string password) => Hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password)
    {
        try {
            var result = Hasher.VerifyHashedPassword(null!, hash, password);
            return result is PasswordVerificationResult.Success
                          or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
