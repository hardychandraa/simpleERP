using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Returns;

public class CustomerDetailModel : PageModel
{
    private readonly IReturnService _returns;
    public CustomerDetailModel(IReturnService returns) => _returns = returns;

    public CustomerReturnDto Return { get; set; } = null!;
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, string? msg, bool err = false)
    {
        var ret = await _returns.GetCustomerReturnAsync(id);
        if (ret == null) return RedirectToPage("/Returns/Index");

        Return = ret;
        ViewData["Title"] = $"Sales Return {ret.ReturnNumber}";
        Msg = msg; IsErr = err;
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await _returns.CancelCustomerReturnAsync(id, User.Identity?.Name ?? "staff");
        return RedirectToPage(new {
            id,
            msg = result.Success
                ? "Return cancelled. The goods have been taken back out of stock and the credit note voided."
                : result.Error,
            err = !result.Success
        });
    }
}
