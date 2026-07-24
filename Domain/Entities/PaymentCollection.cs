namespace SimpleERP.Domain.Entities;

/// <summary>
/// Records a payment received against a Due sale.
/// A sale can have multiple partial payments.
/// </summary>
public class PaymentCollection
{
    public Guid     Id          { get; set; }
    public Guid     SaleId      { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public Sale?    Sale        { get; set; }
}
