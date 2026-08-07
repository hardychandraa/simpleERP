namespace SimpleERP.Domain.Entities;

/// <summary>
/// One credit/debit note applied against one specific invoice or purchase.
///
/// Before this existed a note only ever netted off AR/AP in aggregate — the whole
/// ledger's open notes subtracted from the whole ledger's outstanding balances, two
/// figures with no relationship to each other. That could drive a net receivable
/// negative (a credit note against a cash sale has no receivable to offset), and it
/// left staff with no answer to "which invoice did this note actually cover?".
///
/// A note can be split across several documents, so this is a link table with an
/// <see cref="Amount"/> rather than a nullable FK on the note itself. The sum of a
/// note's non-reversed applications can never exceed its face value, and the sum
/// applied to one document can never exceed that document's own remaining balance —
/// both capped at write time, which is what makes a negative net balance structurally
/// impossible rather than merely unlikely.
///
/// This is deliberately NOT how <see cref="PaymentBatch"/> nets notes. A batch settles
/// a note against the settlement as a whole, reducing the cash transferred and never
/// touching any one document — that path is unchanged. This one attributes the note to
/// a named document. Both can act on the same note: a batch nets whatever remains after
/// any per-document applications.
/// </summary>
public class CreditNoteApplication
{
    public Guid Id           { get; set; }
    public Guid CreditNoteId { get; set; }

    /// <summary>
    /// Set when applying a credit note. Exactly one of Sale/Purchase is populated, and
    /// which one is fixed by the note's own direction — a credit note can only ever
    /// reduce a receivable, a debit note only ever a payable.
    /// </summary>
    public Guid? SaleId     { get; set; }
    /// <summary>Set when applying a debit note. Exactly one of Sale/Purchase is populated.</summary>
    public Guid? PurchaseId { get; set; }

    /// <summary>
    /// How much of the note this application consumes. Tax-inclusive, like the note's
    /// own face value and like the document balance it reduces — a note offsetting a
    /// receivable is cash not collected, not turnover.
    /// </summary>
    public decimal  Amount          { get; set; }
    public DateTime ApplicationDate { get; set; } = DateTime.UtcNow;
    public string?  Notes           { get; set; }
    public string   CreatedBy       { get; set; } = string.Empty;
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Reversed rather than deleted, the same treatment <see cref="RebateAccrual.IsVoided"/>
    /// gets: who applied it, when, and who reversed it all stay queryable. Every applied
    /// total excludes reversed rows, so a reversal releases the amount back to both the
    /// note and the document at once.
    /// </summary>
    public bool      IsReversed { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string?   ReversedBy { get; set; }

    public CreditNote? CreditNote { get; set; }
    public Sale?       Sale       { get; set; }
    public Purchase?   Purchase   { get; set; }
}
