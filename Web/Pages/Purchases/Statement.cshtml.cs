using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// A supplier's statement of account, and the form that settles it. Doubles as the
/// printable document — the same page the business owner reconciles against the
/// supplier's own list before paying.
/// </summary>
public class StatementModel : PageModel
{
    private readonly IPurchaseService _purchases;
    public StatementModel(IPurchaseService purchases) => _purchases = purchases;

    public PaymentStatementDto Statement { get; set; } = null!;

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To   { get; set; }

    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public List<StatementLineInput> Lines { get; set; } = new();
    /// <summary>Ids of the debit notes ticked to net off this settlement.</summary>
    [BindProperty] public List<Guid> ApplyNoteIds { get; set; } = new();

    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid supplierId)
    {
        var statement = await _purchases.GetSupplierStatementAsync(supplierId, From, To);
        if (statement == null) return RedirectToPage("/Purchases/Due");

        Statement = statement;
        ViewData["Title"] = $"Statement — {statement.CounterpartyName}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid supplierId)
    {
        var statement = await _purchases.GetSupplierStatementAsync(supplierId, From, To);
        if (statement == null) return RedirectToPage("/Purchases/Due");

        Statement = statement;
        ViewData["Title"] = $"Statement — {statement.CounterpartyName}";

        // Rows left blank simply aren't part of this settlement.
        var lines = Lines
            .Where(l => l.Amount > 0)
            .Select(l => new SupplierBatchPaymentLineDto { PurchaseId = l.DocumentId, Amount = l.Amount })
            .ToList();

        if (lines.Count == 0 && ApplyNoteIds.Count == 0)
        {
            Error = "Enter an amount on at least one purchase, or tick a debit note.";
            return Page();
        }

        var result = await _purchases.RecordBatchPaymentAsync(new RecordSupplierBatchPaymentDto {
            SupplierId         = supplierId,
            Notes              = Notes,
            Lines              = lines,
            ApplyCreditNoteIds = ApplyNoteIds
        }, User.Identity?.Name ?? "staff");

        if (!result.Success) { Error = result.Error; return Page(); }

        var b = result.Data!;
        var msg = $"Settlement {b.BatchNumber} recorded — {b.NetAmount:N0} paid across " +
                  $"{b.DocumentCount} purchase(s)" +
                  (b.NoteCount > 0 ? $", after netting {b.NotesAppliedAmount:N0} in debit note(s)." : ".");
        return Redirect($"/Purchases/Due?msg={Uri.EscapeDataString(msg)}");
    }
}
