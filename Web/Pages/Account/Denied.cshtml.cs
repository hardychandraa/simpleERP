using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Account;

/// <summary>
/// Where the cookie handler sends a signed-in user who lacks the role for a page —
/// a Staff account opening an Admin-only link, in practice. Anonymous rather than
/// authenticated so it can also render if the cookie expires mid-navigation.
/// </summary>
public class DeniedModel : PageModel
{
}
