namespace SimpleERP.Application.Interfaces;

/// <summary>
/// Turns a password into a stored hash, and checks one against it.
///
/// An interface rather than a direct call because Application is deliberately
/// abstractions-only (see the notes in its .csproj) — and because this is the single
/// point that would change if the app ever moved to full ASP.NET Core Identity.
/// </summary>
public interface IPasswordHasher {
    string Hash(string password);
    /// <summary>False for a wrong password, and for any stored value that cannot be parsed.</summary>
    bool Verify(string hash, string password);
}
