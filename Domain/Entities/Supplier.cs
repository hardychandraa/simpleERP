namespace SimpleERP.Domain.Entities;

/// <summary>
/// A merchandise supplier. Mirrors Customer, plus the two things the AP and rebate
/// sides need that the sales side doesn't: an NPWP for the tax paperwork, and a
/// default credit term, since a supplier's terms are a standing arrangement rather
/// than something negotiated per document.
/// </summary>
public class Supplier {
    public Guid    Id       { get; set; }
    public string  Name     { get; set; } = string.Empty;
    public string? Phone    { get; set; }
    public string? Address  { get; set; }
    /// <summary>NPWP — the supplier's Indonesian tax ID, as printed on their invoices.</summary>
    public string? TaxId    { get; set; }
    /// <summary>
    /// Default credit term, pre-selected on a new purchase. Only a default: the term
    /// actually applied is snapshotted onto each Purchase, so changing it here never
    /// moves the due date of a document already posted.
    /// </summary>
    public Guid?   PaymentTermId { get; set; }
    public bool    IsActive { get; set; } = true;
    public string? Notes    { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PaymentTerm?             PaymentTerm { get; set; }
    public ICollection<Purchase>    Purchases   { get; set; } = new List<Purchase>();
}
