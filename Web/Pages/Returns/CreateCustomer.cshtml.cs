using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Returns;

/// <summary>
/// Takes goods back against one invoice. The lines are whatever that invoice sold, each
/// capped at what's still returnable, so the form can't be used to return something that
/// was never bought or to return the same units twice.
/// </summary>
public class CreateCustomerModel : PageModel
{
    private readonly IReturnService _returns;
    public CreateCustomerModel(IReturnService returns) => _returns = returns;

    public ReturnFormDto Form { get; set; } = null!;

    [BindProperty] public DateTime? ReturnDate { get; set; }
    [BindProperty] public string    Reason     { get; set; } = "";
    [BindProperty] public string?   Notes      { get; set; }
    [BindProperty] public List<ReturnLineInput> Lines { get; set; } = new();

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid saleId)
    {
        var form = await _returns.GetCustomerReturnFormAsync(saleId);
        if (form == null) return RedirectToPage("/Sales/Index");

        Form = form;
        ViewData["Title"] = $"Return against {form.DocumentNumber}";
        ReturnDate = DateTime.Now.Date;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid saleId)
    {
        var form = await _returns.GetCustomerReturnFormAsync(saleId);
        if (form == null) return RedirectToPage("/Sales/Index");

        Form = form;
        ViewData["Title"] = $"Return against {form.DocumentNumber}";

        // Rows left blank simply aren't part of this return.
        var items = Lines
            .Where(l => l.Qty > 0)
            .Select(l => new CreateReturnItemDto {
                SourceItemId = l.SourceItemId, Qty = l.Qty, Notes = l.Notes })
            .ToList();

        if (items.Count == 0)
        {
            Error = "Enter a quantity on at least one line.";
            return Page();
        }

        var result = await _returns.CreateCustomerReturnAsync(new CreateCustomerReturnDto {
            SaleId     = saleId,
            ReturnDate = ReturnDate,
            Reason     = Reason,
            Notes      = Notes,
            Items      = items
        }, User.Identity?.Name ?? "staff");

        if (!result.Success) { Error = result.Error; return Page(); }

        var msg = $"Return {result.Data!.ReturnNumber} posted. Goods restocked and credit note " +
                  $"{result.Data.CreditNoteNumber} raised for {result.Data.GrandTotal:N0}.";
        return Redirect($"/Returns/Customer/{result.Data.Id}?msg={Uri.EscapeDataString(msg)}");
    }
}
