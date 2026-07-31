using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Returns;

/// <summary>
/// Sends goods back to a supplier against one purchase. Capped per line at what's still
/// returnable, and refused outright if the units are no longer in stock — they may already
/// have been sold, and stock can't go negative.
/// </summary>
public class CreateSupplierModel : PageModel
{
    private readonly IReturnService _returns;
    public CreateSupplierModel(IReturnService returns) => _returns = returns;

    public ReturnFormDto Form { get; set; } = null!;

    [BindProperty] public DateTime? ReturnDate { get; set; }
    [BindProperty] public string    Reason     { get; set; } = "";
    [BindProperty] public string?   Notes      { get; set; }
    [BindProperty] public List<ReturnLineInput> Lines { get; set; } = new();

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid purchaseId)
    {
        var form = await _returns.GetSupplierReturnFormAsync(purchaseId);
        if (form == null) return RedirectToPage("/Purchases/Index");

        Form = form;
        ViewData["Title"] = $"Return against {form.DocumentNumber}";
        ReturnDate = DateTime.Now.Date;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid purchaseId)
    {
        var form = await _returns.GetSupplierReturnFormAsync(purchaseId);
        if (form == null) return RedirectToPage("/Purchases/Index");

        Form = form;
        ViewData["Title"] = $"Return against {form.DocumentNumber}";

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

        var result = await _returns.CreateSupplierReturnAsync(new CreateSupplierReturnDto {
            PurchaseId = purchaseId,
            ReturnDate = ReturnDate,
            Reason     = Reason,
            Notes      = Notes,
            Items      = items
        }, User.Identity?.Name ?? "staff");

        if (!result.Success) { Error = result.Error; return Page(); }

        var msg = $"Return {result.Data!.ReturnNumber} posted. Goods issued and debit note " +
                  $"{result.Data.DebitNoteNumber} raised for {result.Data.GrandTotal:N0}.";
        return Redirect($"/Returns/Supplier/{result.Data.Id}?msg={Uri.EscapeDataString(msg)}");
    }
}
