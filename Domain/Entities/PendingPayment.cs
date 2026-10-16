namespace SimpleERP.Domain.Entities;

public enum PendingPaymentStatus { Pending = 1, Verified = 2, Rejected = 3 }

/// <summary>
/// A customer payment Staff recorded, waiting for Admin to confirm the money really arrived
/// (R1, HC 2026-10-16). Kept apart from <see cref="PaymentRecord"/> on purpose: until Admin
/// verifies it, it changes nothing (not the invoice balance, not commission, not End of Day's
/// cash, not any report), so no existing total can count it by mistake. Verifying it records
/// an ordinary PaymentRecord dated when Staff received it; rejecting it records why.
/// </summary>
public class PendingPayment
{
    public Guid     Id         { get; set; }
    public Guid     SaleId     { get; set; }
    public decimal  Amount     { get; set; }
    public string?  Notes      { get; set; }
    /// <summary>When Staff recorded receiving it (UTC). Becomes the payment's date when verified.</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string   CreatedBy  { get; set; } = string.Empty;
    public PendingPaymentStatus Status { get; set; } = PendingPaymentStatus.Pending;
    public string?   DecidedBy    { get; set; }
    public DateTime? DecidedAt    { get; set; }
    public string?   RejectReason { get; set; }
    /// <summary>The payment it became, once verified.</summary>
    public Guid?     PaymentRecordId { get; set; }

    public Sale? Sale { get; set; }
}
