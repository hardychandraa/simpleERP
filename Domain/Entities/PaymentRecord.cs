namespace SimpleERP.Domain.Entities;

/// <summary>Records a payment made against a Due sale.</summary>
public class PaymentRecord
{
    public Guid     Id          { get; set; }
    public Guid     SaleId      { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "staff";

    // Navigation
    public Sale? Sale { get; set; }
}
