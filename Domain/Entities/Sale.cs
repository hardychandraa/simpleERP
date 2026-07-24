using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

public class Sale
{
    public Guid        Id            { get; set; }
    public string      InvoiceNumber { get; set; } = string.Empty;
    public DateTime    SaleDate      { get; set; } = DateTime.UtcNow;
    public Guid        CustomerId    { get; set; }
    public Guid        BranchId      { get; set; }
    public PaymentType PaymentType   { get; set; }
    /// <summary>
    /// Populated only for TOP30/45/60/90 terms. Null for Cash/Due.
    /// Formula: SaleDate + X days where X is the term number.
    /// </summary>
    public DateTime?   DueDate       { get; set; }
    public decimal     SubTotal      { get; set; }
    public decimal     DiscountTotal { get; set; }
    /// <summary>PPN taxable base (DPP) for this sale — normally SubTotal − DiscountTotal.</summary>
    public decimal     TaxBase       { get; set; }
    /// <summary>
    /// PPN rate applied, stored as a fraction (0.10 = 10%). Snapshotted at sale time —
    /// AppSettings.VatRate may change later and must not retroactively alter posted sales.
    /// </summary>
    public decimal     TaxRate       { get; set; }
    public decimal     TaxAmount     { get; set; }
    /// <summary>
    /// True if the quoted line prices already included PPN (tax extracted backwards);
    /// false if PPN was added on top. Snapshotted per sale — the business quotes both ways.
    /// </summary>
    public bool        IsTaxInclusive{ get; set; }
    public decimal     GrandTotal    { get; set; }
    public decimal     AmountPaid    { get; set; }
    public SaleStatus  Status        { get; set; } = SaleStatus.Active;
    public string?     Notes         { get; set; }
    public string      CreatedBy     { get; set; } = "staff";
    public DateTime    CreatedAt     { get; set; } = DateTime.UtcNow;

    public Customer?                  Customer       { get; set; }
    public Branch?                    Branch         { get; set; }
    public ICollection<SaleItem>      SaleItems      { get; set; } = new List<SaleItem>();
    public ICollection<PaymentRecord> PaymentRecords { get; set; } = new List<PaymentRecord>();
}
