namespace SimpleERP.Domain.Entities;

/// <summary>
/// One line sent back to a supplier, tied to the exact PurchaseItem it was received on —
/// that link caps the returnable quantity and makes the debit match what was billed.
/// </summary>
public class SupplierReturnItem
{
    public Guid    Id               { get; set; }
    public Guid    SupplierReturnId { get; set; }
    /// <summary>The purchase line being returned against.</summary>
    public Guid    PurchaseItemId   { get; set; }
    public Guid    ProductId        { get; set; }
    public decimal Qty              { get; set; }
    /// <summary>Unit cost as billed, snapshotted for display. Not used for arithmetic.</summary>
    public decimal UnitCost         { get; set; }
    /// <summary>
    /// This line's debit: the purchase line's net cost (after its own discount and its
    /// allocated share of any document discount) prorated by the returned quantity.
    /// Derived from the net figure so a discounted invoice isn't credited back at gross.
    /// </summary>
    public decimal DebitAmount      { get; set; }
    /// <summary>
    /// The moving-average cost the goods actually left stock at — which is not the same
    /// as UnitCost once other batches have moved the average. Stored so cancelling this
    /// return puts them back at exactly the value they left at, rather than at whatever
    /// the average has drifted to since.
    /// </summary>
    public decimal CostAtReturn     { get; set; }
    public string? Notes            { get; set; }

    public SupplierReturn? Return       { get; set; }
    public PurchaseItem?   PurchaseItem { get; set; }
    public Product?        Product      { get; set; }
}
