using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Payments;

/// <summary>
/// The by-invoice due list moved into Due Payments as its second view (S3, HC 2026-10-18).
/// This URL stays for bookmarks and old links; it is still in the Admin-only /Payments folder.
/// </summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() => Redirect("/Sales/Due?view=invoice");
}
