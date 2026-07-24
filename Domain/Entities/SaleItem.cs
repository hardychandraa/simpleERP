namespace SimpleERP.Domain.Entities;

public class SaleItem
{
    public Guid      Id             { get; set; }
    public Guid      SaleId         { get; set; }
    public Guid      ProductId      { get; set; }
    public decimal   Qty            { get; set; }
    public decimal   UnitPrice      { get; set; }
    public decimal   DiscountAmount { get; set; }
    public decimal   LineTotal      { get; set; }
    public decimal   CostAtSale     { get; set; }
    public int?      WarrantyMonths { get; set; }
    public DateTime? WarrantyExpiry { get; set; }

    /// <summary>Free-text notes per line item (serial numbers, conditions, special terms).</summary>
    public string?   Notes          { get; set; }

    /// <summary>
    /// Required when UnitPrice differs from Product.UnitPrice at time of sale.
    /// Records the business reason for the price override. Shown in audit and on invoice.
    /// </summary>
    public string?   PriceReason    { get; set; }

    public Sale?     Sale    { get; set; }
    public Product?  Product { get; set; }
}
