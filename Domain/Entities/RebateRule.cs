using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// A standing agreement with a supplier that earns a rebate. One wide, nullable-field
/// row per rule rather than a generic EAV/metadata model — matching the existing
/// wide-nullable idiom (SaleItem.WarrantyMonths etc.); new scheme shapes arrive as
/// appended enum values, not new tables.
///
/// The condition decides *whether* a purchase (or payment) qualifies; the reward
/// decides *how much* accrues. The two are orthogonal, so which of the *Threshold /
/// ReferenceCost / Reward* fields matter depends on ConditionType and RewardType.
/// </summary>
public class RebateRule
{
    public Guid   Id         { get; set; }
    public string Name       { get; set; } = string.Empty;
    public Guid   SupplierId { get; set; }
    /// <summary>Null = applies to every product from this supplier.</summary>
    public Guid?  ProductId  { get; set; }

    public RebateConditionType ConditionType { get; set; }
    /// <summary>Volume: minimum cumulative qty in the period to qualify. Null = any qty qualifies.</summary>
    public decimal? ThresholdQty   { get; set; }
    /// <summary>Volume: alternative minimum cumulative purchase *value* in the period. Null = not used.</summary>
    public decimal? ThresholdValue { get; set; }
    /// <summary>PriceDrop: the reference cost; a line billed below this earns the difference.</summary>
    public decimal? ReferenceCost  { get; set; }
    /// <summary>OnTimePayment: pay within this many days of the due date to qualify. Null = on/before due.</summary>
    public int?     OnTimePaymentDays { get; set; }

    /// <summary>Rule active window. Volume thresholds accumulate within it. Null = open-ended.</summary>
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd   { get; set; }

    public RebateRewardType RewardType { get; set; }
    /// <summary>PercentDiscount: percent of qualifying value (10 = 10%).</summary>
    public decimal? RewardRate     { get; set; }
    /// <summary>FixedCash/CreditNote: flat amount accrued per qualifying purchase.</summary>
    public decimal? RewardAmount   { get; set; }
    /// <summary>InKindGoods: which product is given free, and how many units.</summary>
    public Guid?    RewardProductId { get; set; }
    public decimal? RewardQty       { get; set; }

    public bool IsActive { get; set; } = true;

    public Supplier? Supplier      { get; set; }
    public Product?  Product       { get; set; }
    public Product?  RewardProduct { get; set; }
}
