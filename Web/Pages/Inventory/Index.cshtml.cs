using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Inventory;
/// <summary>
/// Stock Levels was a subset of Products (stock, avg cost) plus stock value; it is now part of
/// the Products page (HC, 2026-10-09). The URL stays so old links and bookmarks still land.
/// </summary>
public class IndexModel:PageModel{
    public IActionResult OnGet() => Redirect("/Products?showEmpty=true");
}
