using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// Goods a customer brought back, as one multi-line document against one invoice —
/// the same document shape as Sale and Purchase, not one row per returned line. A
/// customer handing back three items from one invoice is a single physical event, so
/// it gets a single return number and generates a single credit note.
///
/// The original sale is never edited. What the customer is owed is carried by the
/// CreditNote this return generates, which is what AR reporting nets off.
/// </summary>
public class CustomerReturn
{
    public Guid     Id           { get; set; }
    /// <summary>Our own generated number, CRN-yyyyMM-nnnn, unique.</summary>
    public string   ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate   { get; set; } = DateTime.UtcNow;
    public Guid     SaleId       { get; set; }
    public Guid     BranchId     { get; set; }

    /// <summary>
    /// Sum of the line credits, on the same tax basis the sale used — so it already
    /// includes PPN when the sale was tax-inclusive, and excludes it when it wasn't.
    /// The split below turns it into base + tax either way.
    /// </summary>
    public decimal  SubTotal     { get; set; }
    /// <summary>PPN base being reversed (DPP).</summary>
    public decimal  TaxBase      { get; set; }
    /// <summary>
    /// Rate taken from the original sale, not from AppSettings — a return has to reverse
    /// the tax that was actually charged, whatever the current rate happens to be.
    /// </summary>
    public decimal  TaxRate      { get; set; }
    public decimal  TaxAmount    { get; set; }
    /// <summary>True if the original sale's prices included PPN. Snapshotted from it.</summary>
    public bool     IsTaxInclusive { get; set; }
    /// <summary>Total credited to the customer, including PPN.</summary>
    public decimal  GrandTotal   { get; set; }

    /// <summary>Why the goods came back. Required — a return without a reason is unauditable.</summary>
    public string   Reason       { get; set; } = string.Empty;
    public string?  Notes        { get; set; }
    public ReturnStatus Status   { get; set; } = ReturnStatus.Active;
    public string   CreatedBy    { get; set; } = "staff";
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;

    public Sale?   Sale   { get; set; }
    public Branch? Branch { get; set; }
    public ICollection<CustomerReturnItem> Items       { get; set; } = new List<CustomerReturnItem>();
    /// <summary>The credit note this return generated. A collection only because it's the FK's other end.</summary>
    public ICollection<CreditNote>         CreditNotes { get; set; } = new List<CreditNote>();
}
