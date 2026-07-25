namespace SimpleERP.Domain.Entities;

/// <summary>
/// One line of a supplier invoice. Mirrors SaleItem, minus the sales-only fields
/// (CostAtSale — a purchase line *is* the cost — and warranty).
/// </summary>
public class PurchaseItem
{
    public Guid    Id             { get; set; }
    public Guid    PurchaseId     { get; set; }
    public Guid    ProductId      { get; set; }
    public decimal Qty            { get; set; }
    /// <summary>
    /// Cost per unit as billed by the supplier, before discounts. This is the figure
    /// the rebate engine subtracts against to get real cost — deliberately the raw
    /// billed number, not something already net of anything.
    /// </summary>
    public decimal UnitCost       { get; set; }
    /// <summary>Flat discount per unit, as resolved server-side.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Percent as entered, when given that way. Null = flat amount. Display only.</summary>
    public decimal? DiscountPercent { get; set; }
    /// <summary>This line's pro-rata share of the document-level discount.</summary>
    public decimal AllocatedInvoiceDiscount { get; set; }
    /// <summary>(UnitCost − DiscountAmount) × Qty. Before the document-level discount.</summary>
    public decimal LineTotal      { get; set; }
    public string? Notes          { get; set; }

    public Purchase? Purchase { get; set; }
    public Product?  Product  { get; set; }
}
