namespace SimpleERP.Domain.Entities;

/// <summary>
/// One returned line, always tied back to the exact SaleItem it came from — that link
/// is what caps the returnable quantity and what makes the credit work out to the price
/// the customer was actually charged, discounts and all.
/// </summary>
public class CustomerReturnItem
{
    public Guid    Id               { get; set; }
    public Guid    CustomerReturnId { get; set; }
    /// <summary>The invoice line being returned against. Restrict-deleted: history stays resolvable.</summary>
    public Guid    SaleItemId       { get; set; }
    public Guid    ProductId        { get; set; }
    public decimal Qty              { get; set; }
    /// <summary>Unit price as sold, snapshotted for display. Not used for arithmetic.</summary>
    public decimal UnitPrice        { get; set; }
    /// <summary>
    /// This line's credit: the sale line's net revenue (after both its own discount and
    /// its allocated share of any invoice discount) prorated by the returned quantity.
    /// Derived from the net figure, never from UnitPrice × Qty — otherwise a discounted
    /// invoice would be refunded at the undiscounted price.
    /// </summary>
    public decimal CreditAmount     { get; set; }
    /// <summary>
    /// Copied from SaleItem.CostAtSale — the value the goods are put back into stock at.
    /// Restocking at the cost they left at is what makes the COGS reversal exact instead
    /// of drifting with whatever the moving average has since become.
    /// </summary>
    public decimal CostAtSale       { get; set; }
    public string? Notes            { get; set; }

    public CustomerReturn? Return   { get; set; }
    public SaleItem?       SaleItem { get; set; }
    public Product?        Product  { get; set; }
}
