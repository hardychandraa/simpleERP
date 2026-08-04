using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Suppliers;

public class IndexModel : PageModel
{
    private readonly ISupplierService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public IndexModel(ISupplierService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    public List<SupplierDto> Suppliers { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Suppliers";
        Msg = msg; IsErr = err;
        Suppliers = await _svc.GetAllAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteAsync(id, User.Identity?.Name ?? "staff");
        return Redirect(result.Success
            ? $"/Suppliers?msg={Uri.EscapeDataString(_loc["Supplier deleted."].Value)}"
            : $"/Suppliers?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
