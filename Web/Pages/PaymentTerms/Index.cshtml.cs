using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.PaymentTerms;

/// <summary>
/// Single-page CRUD for the PaymentTerm master table. Kept on one page rather than
/// the Index/Create/Edit split used for Customers/Products: this table has four
/// fields and a handful of rows, so separate pages would be more navigation than
/// the data warrants.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IPaymentTermService _svc;
    public IndexModel(IPaymentTermService svc) => _svc = svc;

    public List<PaymentTermDto> Terms { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    [BindProperty] public PaymentTermDto Input { get; set; } = new();

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Payment Terms";
        Msg = msg; IsErr = err;
        Terms = await _svc.GetAllAsync();
    }

    private string User_ => User.Identity?.Name ?? "staff";

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await _svc.CreateAsync(Input, User_);
        return Redirect(result.Success
            ? $"/PaymentTerms?msg={Uri.EscapeDataString($"'{Input.Name}' added.")}"
            : $"/PaymentTerms?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        var result = await _svc.UpdateAsync(Input, User_);
        return Redirect(result.Success
            ? $"/PaymentTerms?msg={Uri.EscapeDataString($"'{Input.Name}' saved.")}"
            : $"/PaymentTerms?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteAsync(id, User_);
        return Redirect(result.Success
            ? "/PaymentTerms?msg=Payment+term+deleted."
            : $"/PaymentTerms?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
