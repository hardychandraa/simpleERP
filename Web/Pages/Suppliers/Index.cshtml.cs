using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Suppliers;

public class IndexModel : PageModel
{
    private readonly ISupplierService _svc;
    public IndexModel(ISupplierService svc) => _svc = svc;

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
            ? "/Suppliers?msg=Supplier+deleted."
            : $"/Suppliers?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
