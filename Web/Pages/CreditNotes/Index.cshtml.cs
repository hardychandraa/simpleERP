using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.CreditNotes;

/// <summary>
/// The notes register: everything credited to customers or owed back by suppliers, whether
/// a return raised it or it was issued standalone. Settling is done here — a note stays
/// Open until someone records that it was applied to an invoice or refunded, and Open
/// notes are what AR/AP has to net off.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICreditNoteService _notes;
    private readonly ICustomerService   _customers;
    private readonly ISupplierService   _suppliers;

    public IndexModel(ICreditNoteService notes, ICustomerService customers, ISupplierService suppliers)
    { _notes = notes; _customers = customers; _suppliers = suppliers; }

    public List<CreditNoteDto> Notes           { get; set; } = new();
    public List<CustomerDto>   CustomerOptions { get; set; } = new();
    public List<SupplierDto>   SupplierOptions { get; set; } = new();

    public decimal OpenCredit { get; set; }
    public decimal OpenDebit  { get; set; }

    public CreditDebitType?  FilterType   { get; set; }
    public CreditNoteStatus? FilterStatus { get; set; }

    [BindProperty] public CreateCreditNoteDto NewNote { get; set; } = new();
    [BindProperty] public SettleCreditNoteDto Settle  { get; set; } = new();

    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(CreditDebitType? type, CreditNoteStatus? status,
                                 string? msg, bool err = false)
    {
        FilterType = type; FilterStatus = status;
        Msg = msg; IsErr = err;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        ViewData["Title"] = "Credit & Debit Notes";
        Notes           = await _notes.GetAllAsync(FilterType, FilterStatus);
        CustomerOptions = await _customers.GetAllActiveAsync();
        SupplierOptions = await _suppliers.GetAllAsync(activeOnly: true);
        OpenCredit      = await _notes.GetOpenTotalAsync(CreditDebitType.Credit);
        OpenDebit       = await _notes.GetOpenTotalAsync(CreditDebitType.Debit);
        NewNote.NoteDate ??= DateTime.Now.Date;
    }

    private string User_ => User.Identity?.Name ?? "staff";

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await _notes.CreateAsync(NewNote, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString($"{NewNote.Type} note created.")}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostSettleAsync()
    {
        var result = await _notes.SettleAsync(Settle, User_);
        return Redirect(result.Success
            ? "/CreditNotes?msg=Note+settled."
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        var result = await _notes.CancelAsync(id, User_);
        return Redirect(result.Success
            ? "/CreditNotes?msg=Note+cancelled."
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
