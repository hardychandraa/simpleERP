namespace SimpleERP.Domain.Entities;

/// <summary>Records a payment made against a Due sale.</summary>
public class PaymentRecord
{
    public Guid     Id          { get; set; }
    public Guid     SaleId      { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = string.Empty;
    /// <summary>
    /// Set when this payment was one line of a multi-invoice settlement. Null for an
    /// ordinary single-invoice payment, which is the unchanged default path.
    /// </summary>
    public Guid?    PaymentBatchId { get; set; }

    /// <summary>
    /// Set when Admin reversed this payment (HC, 2026-10-21): it was entered wrong (amount,
    /// invoice or date). The row is kept for the audit trail but no longer counts anywhere —
    /// not in the invoice's paid amount, End of Day or commission. Null = a live payment.
    /// </summary>
    public DateTime? ReversedAt    { get; set; }
    public string?   ReversedBy    { get; set; }
    public string?   ReverseReason { get; set; }
    public bool      IsReversed => ReversedAt != null;

    // Navigation
    public Sale? Sale { get; set; }
    public PaymentBatch? PaymentBatch { get; set; }
}
