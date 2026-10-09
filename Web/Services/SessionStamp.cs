namespace SimpleERP.Web.Services;

/// <summary>
/// The login cookie carries the user's security stamp under this claim; the per-request
/// check in Program.cs refuses a cookie whose stamp no longer matches the user row.
/// </summary>
public static class SessionStamp
{
    public const string ClaimType = "SecurityStamp";
}
