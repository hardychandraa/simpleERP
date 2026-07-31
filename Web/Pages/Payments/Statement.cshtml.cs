using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Payments;

/// <summary>
/// A customer's statement of account, and the form that collects against it. Doubles as
/// the printable document handed to the customer.
/// </summary>
public class StatementModel : PageModel
{
    private readonly ISaleService _sales;
    public StatementModel(ISaleService sales) => _sales = sales;

    public PaymentStatementDto Statement { get; set; } = null!;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To   { get; set; }

    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public List<StatementLineInput> Lines { get; set; } = new();
    /// <summary>Ids of the credit notes ticked to net off this settlement.</summary>
    [BindProperty] public List<Guid> ApplyNoteIds { get; set; } = new();

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid customerId)
    {
        var statement = await _sales.GetCustomerStatementAsync(customerId, From, To);
        if (statement == null) return RedirectToPage("/Sales/Due");

        Statement = statement;
        ViewData["Title"] = $"Statement — {statement.CounterpartyName}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid customerId)
    {
        var statement = await _sales.GetCustomerStatementAsync(customerId, From, To);
        if (statement == null) return RedirectToPage("/Sales/Due");

        Statement = statement;
        ViewData["Title"] = $"Statement — {statement.CounterpartyName}";

        var lines = Lines
            .Where(l => l.Amount > 0)
            .Select(l => new SaleBatchPaymentLineDto { SaleId = l.DocumentId, Amount = l.Amount })
            .ToList();

        if (lines.Count == 0 && ApplyNoteIds.Count == 0)
        {
            Error = "Enter an amount on at least one invoice, or tick a credit note.";
            return Page();
        }

        var result = await _sales.RecordBatchPaymentAsync(new RecordSaleBatchPaymentDto {
            CustomerId         = customerId,
            Notes              = Notes,
            Lines              = lines,
            ApplyCreditNoteIds = ApplyNoteIds
        }, User.Identity?.Name ?? "staff");

        if (!result.Success) { Error = result.Error; return Page(); }

        var b = result.Data!;
        var msg = $"Settlement {b.BatchNumber} recorded — {b.NetAmount:N0} collected across " +
                  $"{b.DocumentCount} invoice(s)" +
                  (b.NoteCount > 0 ? $", after netting {b.NotesAppliedAmount:N0} in credit note(s)." : ".");
        return Redirect($"/Sales/Due?msg={Uri.EscapeDataString(msg)}");
    }
}

/// <summary>
/// One settleable row on a customer statement. Mirrors the AP-side input; kept per
/// namespace rather than shared, matching how the two sides' DTOs are deliberately twins
/// rather than one shared type.
/// </summary>
public class StatementLineInput
{
    public Guid    DocumentId { get; set; }
    public decimal Amount     { get; set; }
}
