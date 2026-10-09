using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Domain.Enums;

namespace SimpleERP.Web.Services;

/// <summary>
/// What a role may see on pages Admin and Staff share (HC, 2026-10-08): Staff work the sales
/// side only and never see harga beli / average cost / HPP / stock value, anything
/// supplier-side, or commission. Whole Admin-only pages are gated by the conventions in
/// Program.cs; these are for the shared pages, whose page models zero or skip the fields
/// before rendering — so a view that forgets a check shows 0, never the real figure.
///
/// Named for intent rather than "IsAdmin" so that a third role later (a purchasing clerk,
/// say) changes one line here instead of every page.
/// </summary>
public static class AccessExtensions
{
    public static bool SeesCost(this ClaimsPrincipal user)       => user.IsInRole(nameof(UserRole.Admin));
    public static bool SeesCommission(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));
    /// <summary>Supplier-side documents and master data (purchases, debit notes, suppliers).</summary>
    public static bool SeesSupplierSide(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));
    /// <summary>
    /// The business's totals: dashboard figures (revenue, cash in, outstanding), receivables,
    /// overdue alerts and End of Day (HC, 2026-10-09).
    /// </summary>
    public static bool SeesBusinessTotals(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));
    /// <summary>Creating or changing master data that is Admin's (products, salespeople).</summary>
    public static bool MaintainsMasterData(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));

    /// <summary>
    /// Refuse a handler on a shared page. Razor Pages ignore [Authorize] on handler methods,
    /// so a POST handler on a page Staff may open needs this explicit check. Sends the user to
    /// /Account/Denied like a gated page does, and logs one warning (AppLogs) with the reason.
    /// A redirect rather than Forbid(), which would log a second, reason-less line through
    /// OnRedirectToAccessDenied.
    /// </summary>
    public static IActionResult Refuse(this PageModel page, string reason)
    {
        page.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SimpleERP.Web.Access")
            .LogWarning("Access denied: {User} ({Role}) {Method} {Path}{Query}: {Reason}",
                page.User.Identity?.Name, page.User.FindFirst(ClaimTypes.Role)?.Value,
                page.Request.Method, page.Request.Path, page.Request.QueryString, reason);
        return new RedirectResult("/Account/Denied");
    }
}
