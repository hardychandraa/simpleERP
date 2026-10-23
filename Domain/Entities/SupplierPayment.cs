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
    public string   CreatedBy   { get; set; } = string.Empty;
    /// <summary>
    /// Set when this payment was one line of a multi-purchase settlement. Null for an
    /// ordinary single-purchase payment, which is the unchanged default path.
    /// </summary>
    public Guid?    PaymentBatchId { get; set; }

    /// <summary>
    /// Set when Admin reversed this payment (HC, 2026-10-21; the mirror of PaymentRecord): it
    /// was entered wrong (amount, purchase or date). The row is kept for the audit trail but no
    /// longer counts in the purchase's paid amount or its settlement. Null = a live payment.
    /// </summary>
    public DateTime? ReversedAt    { get; set; }
    public string?   ReversedBy    { get; set; }
    public string?   ReverseReason { get; set; }
    public bool      IsReversed => ReversedAt != null;

    public Purchase? Purchase { get; set; }
    public PaymentBatch? PaymentBatch { get; set; }
}
