using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// A credit note (we owe a customer) or a debit note (a supplier owes us) — one entity
/// with a Type discriminator rather than two near-identical ones.
///
/// Returns generate one automatically, but a note is a standalone document in its own
/// right: price disputes, billing errors and rebate settlements all produce one with no
/// goods movement behind them, which is why every source link is nullable.
///
/// This is the document that carries the money. A posted sale or purchase is never
/// edited to reflect a return, so AR and AP reporting must net off the notes still
/// sitting at <see cref="CreditNoteStatus.Open"/>.
/// </summary>
public class CreditNote
{
    public Guid            Id             { get; set; }
    /// <summary>Generated per direction: CN-yyyyMM-nnnn for credit, DN-yyyyMM-nnnn for debit. Unique.</summary>
    public string          DocumentNumber { get; set; } = string.Empty;
    public CreditDebitType Type           { get; set; }
    public CreditNoteCategory Category    { get; set; } = CreditNoteCategory.Other;
    public DateTime        NoteDate       { get; set; } = DateTime.UtcNow;

    /// <summary>Set on a credit note. Exactly one of Customer/Supplier is populated.</summary>
    public Guid?           CustomerId     { get; set; }
    /// <summary>Set on a debit note. Exactly one of Customer/Supplier is populated.</summary>
    public Guid?           SupplierId     { get; set; }

    /// <summary>Taxable base of the adjustment (DPP).</summary>
    public decimal         TaxBase        { get; set; }
    /// <summary>Rate as a fraction. Copied from the source document when there is one.</summary>
    public decimal         TaxRate        { get; set; }
    public decimal         TaxAmount      { get; set; }
    /// <summary>Face value of the note, including PPN. TaxBase + TaxAmount.</summary>
    public decimal         Amount         { get; set; }

    public Guid?           SourceSaleId           { get; set; }
    public Guid?           SourcePurchaseId       { get; set; }
    public Guid?           SourceCustomerReturnId { get; set; }
    public Guid?           SourceSupplierReturnId { get; set; }

    public CreditNoteStatus Status        { get; set; } = CreditNoteStatus.Open;
    public DateTime?       SettledDate    { get; set; }
    /// <summary>How it was settled — applied to which invoice, or refunded in cash.</summary>
    public string?         SettlementNotes{ get; set; }
    /// <summary>
    /// Set when this note was netted off inside a multi-document settlement, so the
    /// batch's NotesAppliedAmount can be drilled into rather than being a bare number.
    /// Null for a note settled on its own from the notes register.
    /// </summary>
    public Guid?           SettledByPaymentBatchId { get; set; }

    /// <summary>Why the note exists. Required.</summary>
    public string          Reason         { get; set; } = string.Empty;
    public string?         Notes          { get; set; }
    public string          CreatedBy      { get; set; } = "staff";
    public DateTime        CreatedAt      { get; set; } = DateTime.UtcNow;

    public Customer?       Customer       { get; set; }
    public Supplier?       Supplier       { get; set; }
    public Sale?           SourceSale     { get; set; }
    public Purchase?       SourcePurchase { get; set; }
    public CustomerReturn? SourceCustomerReturn { get; set; }
    public SupplierReturn? SourceSupplierReturn { get; set; }
    public PaymentBatch?   SettledByPaymentBatch { get; set; }
}
