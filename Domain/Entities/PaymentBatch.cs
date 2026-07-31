using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// One settlement covering several documents at once — the shape a real statement of
/// account takes: the counterparty lists every open document over a period, and one
/// transfer settles the lot.
///
/// One entity carries both directions via <see cref="PaymentBatchDirection"/> rather than
/// two near-identical types, the same call CreditNote made — and for the same reason, the
/// PaymentRecord/PaymentCollection duplication this codebase already paid for once.
///
/// The individual <see cref="PaymentRecord"/> / <see cref="SupplierPayment"/> rows stay
/// exactly as they are for a single-document payment; this batch is the envelope around
/// them. A payment with no batch is an ordinary one-document payment, which is why the
/// back-references on those two entities are nullable.
///
/// Money is recorded Gross → deduction → Net, with net derived by <em>subtraction</em>
/// rather than rounded independently — the same reconciliation rule RebateRealization
/// uses for its withholding split.
/// </summary>
public class PaymentBatch
{
    public Guid     Id          { get; set; }
    /// <summary>Generated per direction: STL-R-yyyyMM-nnnn received, STL-P-yyyyMM-nnnn paid. Unique.</summary>
    public string   BatchNumber { get; set; } = string.Empty;
    public PaymentBatchDirection Direction { get; set; }
    public DateTime BatchDate   { get; set; } = DateTime.UtcNow;

    /// <summary>Set on a Received batch. Exactly one of Customer/Supplier is populated.</summary>
    public Guid?    CustomerId  { get; set; }
    /// <summary>Set on a Paid batch. Exactly one of Customer/Supplier is populated.</summary>
    public Guid?    SupplierId  { get; set; }

    /// <summary>Sum of the amounts applied to the documents in this settlement.</summary>
    public decimal  GrossAmount        { get; set; }
    /// <summary>
    /// Face value of the credit/debit notes settled as part of this batch. These reduce
    /// the cash that changes hands; they do not touch any individual document's AmountPaid,
    /// which stays exactly what was applied to it.
    /// </summary>
    public decimal  NotesAppliedAmount { get; set; }
    /// <summary>GrossAmount − NotesAppliedAmount: the cash actually transferred.</summary>
    public decimal  NetAmount          { get; set; }

    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "staff";
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;

    public Customer? Customer { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>The AR payments this batch covers. Empty on a Paid batch.</summary>
    public ICollection<PaymentRecord>   Payments         { get; set; } = new List<PaymentRecord>();
    /// <summary>The AP payments this batch covers. Empty on a Received batch.</summary>
    public ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
    /// <summary>The credit/debit notes settled as part of this batch.</summary>
    public ICollection<CreditNote>      AppliedNotes     { get; set; } = new List<CreditNote>();
}
