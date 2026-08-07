using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.SalesPersons;

/// <summary>
/// Single-page CRUD for the SalesPerson master table, same shape as PaymentTerms:
/// three fields and a handful of rows don't justify an Index/Create/Edit split.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ISalesPersonService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public IndexModel(ISalesPersonService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    public List<SalesPersonDto> People { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    [BindProperty] public SalesPersonDto Input { get; set; } = new();

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Sales People";
        Msg = msg; IsErr = err;
        People = await _svc.GetAllAsync();
    }

    private string User_ => this.CurrentUserName();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await _svc.CreateAsync(Input, User_);
        return Redirect(result.Success
            ? $"/SalesPersons?msg={Uri.EscapeDataString(_loc["'{0}' added.", Input.Name].Value)}"
            : $"/SalesPersons?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteAsync(id, User_);
        return Redirect(result.Success
            ? $"/SalesPersons?msg={Uri.EscapeDataString(_loc["Sales person deleted."].Value)}"
            : $"/SalesPersons?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
