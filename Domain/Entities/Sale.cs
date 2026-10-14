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
    /// Credit term for this sale, and the only place the term is recorded. Null for Cash
    /// and for open credit with no agreed term.
    /// </summary>
    public Guid?       PaymentTermId { get; set; }
    /// <summary>
    /// Who gets credited with this sale. Null is legitimate — walk-in sales, and every
    /// sale posted before salespeople existed. Commission (Step 7) reads this.
    /// </summary>
    public Guid?       SalesPersonId { get; set; }
    /// <summary>
    /// Set when this invoice was created by revising another one: the original was
    /// cancelled in the same transaction that created this. Null for ordinary sales.
    /// </summary>
    public Guid?       ReplacesSaleId { get; set; }
    /// <summary>
    /// When payment falls due. Null for Cash, and for open credit with no agreed term.
    /// Formula: SaleDate + PaymentTerm.DueDays, computed once at posting time so a later
    /// edit to the term never moves an existing invoice's deadline.
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
    public string      CreatedBy     { get; set; } = string.Empty;
    public DateTime    CreatedAt     { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Perlu dicek (HC, 2026-10-14): a Staff sale below cost, over the discount limit, dated
    /// yesterday, or with a salesperson other than the customer's. It posts as normal; Admin
    /// checks it. <see cref="ReviewReasons"/> says why, for Admin only (it can name cost).
    /// </summary>
    public bool        NeedsReview   { get; set; }
    public string?     ReviewReasons { get; set; }
    public string?     ReviewedBy    { get; set; }
    public DateTime?   ReviewedAt    { get; set; }

    /// <summary>When, by whom and why the sale was cancelled (a revision cancels its original).</summary>
    public DateTime?   CancelledAt   { get; set; }
    public string?     CancelledBy   { get; set; }
    public string?     CancelReason  { get; set; }

    /// <summary>
    /// The New Sale form's one-time id (G9): a second submit of the same form finds this sale
    /// instead of posting a duplicate. Unique; null for sales from before 2026-10-14.
    /// </summary>
    public Guid?       SubmissionId  { get; set; }

    public Customer?                  Customer       { get; set; }
    public Branch?                    Branch         { get; set; }
    public PaymentTerm?               PaymentTerm    { get; set; }
    public SalesPerson?               SalesPerson    { get; set; }
    public ICollection<SaleItem>      SaleItems      { get; set; } = new List<SaleItem>();
    public ICollection<PaymentRecord> PaymentRecords { get; set; } = new List<PaymentRecord>();
}
