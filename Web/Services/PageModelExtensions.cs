using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Services;

public static class PageModelExtensions
{
    /// <summary>
    /// The signed-in username, for the <c>string user</c> parameter every write-side
    /// service method takes — it lands in the entity's <c>CreatedBy</c> and in the
    /// matching <c>AuditLog</c> row.
    ///
    /// This replaced <c>User.Identity?.Name ?? "staff"</c>, which appeared at 46 call
    /// sites and, with no authentication anywhere in the app, always evaluated to the
    /// literal <c>"staff"</c> — so every invoice, payment and cancellation in the system
    /// was attributed to the same fictional person.
    ///
    /// Not null-tolerant, deliberately. Every page except /Account/Login is behind
    /// <c>AuthorizeFolder("/")</c>, so an unauthenticated caller cannot reach a handler
    /// that calls this. If that ever stops being true, a crash naming this method is a
    /// far better outcome than silently resuming anonymous attribution.
    /// </summary>
    public static string CurrentUserName(this PageModel page) =>
        page.User.Identity?.Name
        ?? throw new InvalidOperationException(
            "No authenticated user on a page that writes business data. Every page except " +
            "/Account/Login is covered by AuthorizeFolder(\"/\") in Program.cs — check that " +
            "the page has not been made anonymous.");
}
