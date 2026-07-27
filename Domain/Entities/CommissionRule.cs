namespace SimpleERP.Domain.Entities;

/// <summary>
/// A rule that earns a salesperson commission, as a percentage of revenue. Configured
/// per salesperson from day one; the nullable ProductId/Category scoping columns let a
/// future product- or category-specific rate arrive without a schema change.
///
/// A null scoping column means "applies to all": SalesPersonId null = every
/// salesperson, ProductId null = every product, Category null = every category. When
/// several rules match a line, the highest Priority wins.
/// </summary>
public class CommissionRule
{
    public Guid    Id            { get; set; }
    public string  Name          { get; set; } = string.Empty;
    /// <summary>Null = applies to every salesperson.</summary>
    public Guid?   SalesPersonId { get; set; }
    /// <summary>Null = applies to every product.</summary>
    public Guid?   ProductId     { get; set; }
    /// <summary>Null = applies to every category. Matched against Product.Category.</summary>
    public string? Category      { get; set; }
    /// <summary>Commission rate as a percentage of revenue (2.5 = 2.5%).</summary>
    public decimal Rate          { get; set; }
    /// <summary>Higher wins when more than one rule matches a line.</summary>
    public int     Priority      { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo   { get; set; }
    public bool    IsActive      { get; set; } = true;

    public SalesPerson? SalesPerson { get; set; }
    public Product?     Product     { get; set; }
}
