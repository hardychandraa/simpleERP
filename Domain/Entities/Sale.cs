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
    /// Credit term for this sale. Null for Cash, and for legacy rows that used the
    /// PaymentType.TOP30–TOP90 enum members before terms became master data.
    /// </summary>
    public Guid?       PaymentTermId { get; set; }
    /// <summary>
    /// Who gets credited with this sale. Null is legitimate — walk-in sales, and every
    /// sale posted before salespeople existed. Commission (Step 7) reads this.
    /// </summary>
    public Guid?       SalesPersonId { get; set; }
    /// <summary>
    /// When payment falls due. Null for Cash, and for open credit with no agreed term.
    /// Formula: SaleDate + PaymentTerm.DueDays (legacy rows: SaleDate + the enum's days).
    /// </summary>
    public DateTime?   DueDate       { get; set; }
    public decimal     SubTotal      { get; set; }
    /// <summary>Sum of line-level discounts only. Invoice-level sits separately below.</summary>
    public decimal     DiscountTotal { get; set; }
    /// <summary>
    /// Whole-invoice discount as negotiated (e.g. "take 500,000 off the total").
    /// Allocated pro-rata onto SaleItem.AllocatedInvoiceDiscount so that per-line
    /// margin, commission and returns all keep working from line-level numbers —
    /// a header-only discount would silently overstate every one of them.
    /// </summary>
    public decimal     InvoiceDiscountAmount  { get; set; }
    /// <summary>Percent as entered, when given that way. Null = flat amount. Never used for arithmetic.</summary>
    public decimal?    InvoiceDiscountPercent { get; set; }
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
    public PaymentTerm?               PaymentTerm    { get; set; }
    public SalesPerson?               SalesPerson    { get; set; }
    public ICollection<SaleItem>      SaleItems      { get; set; } = new List<SaleItem>();
    public ICollection<PaymentRecord> PaymentRecords { get; set; } = new List<PaymentRecord>();
}
