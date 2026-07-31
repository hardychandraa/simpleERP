namespace SimpleERP.Domain.Entities;

/// <summary>
/// A payment made to a supplier against one purchase — the AP mirror of
/// PaymentRecord. Separate rows rather than just moving Purchase.AmountPaid, so the
/// payment date is preserved: the OnTimePayment rebate condition (Step 6) is decided
/// by comparing that date against the purchase's due date.
/// </summary>
public class SupplierPayment
{
    public Guid     Id          { get; set; }
    public Guid     PurchaseId  { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "staff";
    /// <summary>
    /// Set when this payment was one line of a multi-purchase settlement. Null for an
    /// ordinary single-purchase payment, which is the unchanged default path.
    /// </summary>
    public Guid?    PaymentBatchId { get; set; }

    public Purchase? Purchase { get; set; }
    public PaymentBatch? PaymentBatch { get; set; }
}
