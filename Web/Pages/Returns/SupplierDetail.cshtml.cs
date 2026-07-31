using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Returns;

public class SupplierDetailModel : PageModel
{
    private readonly IReturnService _returns;
    public SupplierDetailModel(IReturnService returns) => _returns = returns;

    public SupplierReturnDto Return { get; set; } = null!;
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        var ret = await _returns.GetSupplierReturnAsync(id);
        if (ret == null) return RedirectToPage("/Returns/Index");

        Return = ret;
        ViewData["Title"] = $"Purchase Return {ret.ReturnNumber}";
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await _returns.CancelSupplierReturnAsync(id, User.Identity?.Name ?? "staff");
        return RedirectToPage(new {
            id,
            msg = result.Success
                ? "Return cancelled. The goods are back in stock at the value they left at, and the debit note is voided."
                : result.Error,
            err = !result.Success
        });
    }
}
