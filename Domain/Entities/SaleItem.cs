namespace SimpleERP.Domain.Entities;

public class SaleItem
{
    public Guid      Id             { get; set; }
    public Guid      SaleId         { get; set; }
    public Guid      ProductId      { get; set; }
    public decimal   Qty            { get; set; }
    public decimal   UnitPrice      { get; set; }
    /// <summary>
    /// Resolved discount per unit. All totals work off this, including when the
    /// discount was entered as a percentage — so no calculation ever depends on
    /// re-deriving an amount from a rate.
    /// </summary>
    public decimal   DiscountAmount { get; set; }
    /// <summary>
    /// The percentage as staff typed it, when the discount was given that way;
    /// null when a flat amount was entered. Kept so the invoice can show "10%"
    /// as the customer was quoted, and so the original intent survives in audit.
    /// Never used for arithmetic.
    /// </summary>
    public decimal?  DiscountPercent{ get; set; }
    public decimal   LineTotal      { get; set; }
    /// <summary>
    /// This line's pro-rata share of the invoice-level discount, by line total.
    /// The shares always sum to Sale.InvoiceDiscountAmount exactly. Net revenue for
    /// this line is LineTotal − AllocatedInvoiceDiscount; use that, not LineTotal,
    /// for margin, commission and refund calculations.
    /// </summary>
    public decimal   AllocatedInvoiceDiscount { get; set; }
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
