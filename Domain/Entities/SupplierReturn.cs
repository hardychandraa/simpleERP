using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// Goods sent back to a supplier, as one multi-line document against one supplier
/// invoice — the mirror of CustomerReturn.
///
/// The original purchase is never edited. What the supplier owes back is carried by the
/// debit note this return generates, which is what AP reporting nets off. Stock leaves
/// at the current moving-average cost while the supplier credits us at the cost they
/// billed; those two figures legitimately differ, and both are recorded per line.
/// </summary>
public class SupplierReturn
{
    public Guid     Id           { get; set; }
    /// <summary>Our own generated number, SRN-yyyyMM-nnnn, unique.</summary>
    public string   ReturnNumber { get; set; } = string.Empty;
    public DateTime ReturnDate   { get; set; } = DateTime.UtcNow;
    public Guid     PurchaseId   { get; set; }
    public Guid     BranchId     { get; set; }

    /// <summary>
    /// Sum of the line debits, on the same tax basis the purchase used — inclusive of
    /// PPN Masukan when the supplier billed that way, exclusive when they didn't.
    /// </summary>
    public decimal  SubTotal     { get; set; }
    /// <summary>PPN Masukan base being reversed (DPP).</summary>
    public decimal  TaxBase      { get; set; }
    /// <summary>Rate taken from the original purchase, so the reversal matches what was actually claimed.</summary>
    public decimal  TaxRate      { get; set; }
    public decimal  TaxAmount    { get; set; }
    /// <summary>True if the original purchase's costs included PPN. Snapshotted from it.</summary>
    public bool     IsTaxInclusive { get; set; }
    /// <summary>Total the supplier owes back, including PPN.</summary>
    public decimal  GrandTotal   { get; set; }

    /// <summary>Why the goods went back. Required.</summary>
    public string   Reason       { get; set; } = string.Empty;
    public string?  Notes        { get; set; }
    public ReturnStatus Status   { get; set; } = ReturnStatus.Active;
    public string   CreatedBy    { get; set; } = "staff";
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;

    public Purchase? Purchase { get; set; }
    public Branch?   Branch   { get; set; }
    public ICollection<SupplierReturnItem> Items       { get; set; } = new List<SupplierReturnItem>();
    /// <summary>The debit note this return generated. A collection only because it's the FK's other end.</summary>
    public ICollection<CreditNote>         CreditNotes { get; set; } = new List<CreditNote>();
}
