using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

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

    private readonly IStringLocalizer<SharedResource> _loc;
    public IndexModel(ICreditNoteService notes, ICustomerService customers, ISupplierService suppliers, IStringLocalizer<SharedResource> loc)
    { _notes = notes; _customers = customers; _suppliers = suppliers;  _loc = loc; }

    public List<CreditNoteDto> Notes           { get; set; } = new();
    public List<CustomerDto>   CustomerOptions { get; set; } = new();
    public List<SupplierDto>   SupplierOptions { get; set; } = new();

    public decimal OpenCredit { get; set; }
    public decimal OpenDebit  { get; set; }

    public CreditDebitType?  FilterType   { get; set; }
    public CreditNoteStatus? FilterStatus { get; set; }

    [BindProperty] public CreateCreditNoteDto NewNote { get; set; } = new();
    [BindProperty] public SettleCreditNoteDto Settle  { get; set; } = new();
    [BindProperty] public ApplyCreditNoteDto  Apply   { get; set; } = new();

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
        // Debit notes are supplier-side: their amounts, the open purchases they apply to and
        // the supplier list are Admin's (HC, 2026-10-08). Staff get credit notes only.
        var supplierSide = User.SeesSupplierSide();
        if (!supplierSide) FilterType = CreditDebitType.Credit;
        Notes           = await _notes.GetAllAsync(FilterType, FilterStatus);
        CustomerOptions = await _customers.GetAllActiveAsync();
        OpenCredit      = await _notes.GetOpenTotalAsync(CreditDebitType.Credit);
        if (supplierSide)
        {
            SupplierOptions = await _suppliers.GetAllAsync(activeOnly: true);
            OpenDebit       = await _notes.GetOpenTotalAsync(CreditDebitType.Debit);
        }
        NewNote.NoteDate ??= DateTime.Now.Date;
    }

    /// <summary>
    /// Staff may act on credit notes only. Every handler checks the note itself, not just
    /// what the form offered, since a direct post can name any note id.
    /// </summary>
    private async Task<bool> IsCreditNoteAsync(Guid noteId)
        => (await _notes.GetByIdAsync(noteId))?.IsCredit == true;

    private string User_ => this.CurrentUserName();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!User.SeesSupplierSide() && (NewNote.Type != CreditDebitType.Credit || NewNote.SupplierId != null))
            return this.Refuse("debit notes are Admin-only");
        var result = await _notes.CreateAsync(NewNote, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString(_loc["{0} note created.", _loc["CreditDebitType_" + NewNote.Type].Value].Value)}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostSettleAsync()
    {
        if (!User.SeesSupplierSide() && !await IsCreditNoteAsync(Settle.Id))
            return this.Refuse("debit notes are Admin-only");
        var result = await _notes.SettleAsync(Settle, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString(_loc["Note settled."].Value)}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        if (!User.SeesSupplierSide() && !await IsCreditNoteAsync(id))
            return this.Refuse("debit notes are Admin-only");
        var result = await _notes.CancelAsync(id, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString(_loc["Note cancelled."].Value)}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        if (!User.SeesSupplierSide() && (Apply.PurchaseId != null || !await IsCreditNoteAsync(Apply.CreditNoteId)))
            return this.Refuse("debit notes are Admin-only");
        var result = await _notes.ApplyAsync(Apply, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString(_loc["Note applied."].Value)}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostReverseApplicationAsync(Guid applicationId)
    {
        if (!User.SeesSupplierSide() && (await _notes.GetApplicationAsync(applicationId))?.IsCredit != true)
            return this.Refuse("debit notes are Admin-only");
        var result = await _notes.ReverseApplicationAsync(applicationId, User_);
        return Redirect(result.Success
            ? $"/CreditNotes?msg={Uri.EscapeDataString(_loc["Application reversed."].Value)}"
            : $"/CreditNotes?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
