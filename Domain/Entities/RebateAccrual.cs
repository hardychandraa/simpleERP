using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// A rebate that has been *earned* but not yet *settled* — the accrual half of the
/// accrual/realization pattern. Created when a purchase or payment trips a rule;
/// closed out later when the supplier actually settles.
///
/// <see cref="RebateRealizationId"/> is null while outstanding and set once claimed —
/// deliberately not a status enum, so one lump settlement can close many accruals and
/// a LuckyDraw accrual can exist with <see cref="Amount"/> = 0 until its value is known.
/// </summary>
public class RebateAccrual
{
    public Guid Id           { get; set; }
    public Guid RebateRuleId { get; set; }
    public Guid SupplierId   { get; set; }

    /// <summary>The purchase that triggered this. Null only for manually recorded accruals.</summary>
    public Guid? PurchaseId     { get; set; }
    /// <summary>The specific line, when the rule is product-scoped.</summary>
    public Guid? PurchaseItemId { get; set; }

    /// <summary>Snapshot of the reward shape at accrual time — the rule may change later.</summary>
    public RebateRewardType RewardType { get; set; }
    /// <summary>Qty basis this accrued on (purchased units, or in-kind reward units).</summary>
    public decimal Qty { get; set; }
    /// <summary>
    /// Cash value earned. Zero for LuckyDraw (unknowable until settled) and for
    /// InKindGoods (valued as stock at realization, not as cash). The real-cost
    /// formula reads this: effective unit cost = PurchaseItem.UnitCost − Amount / Qty.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Null = outstanding. Set to the realization that settled it.</summary>
    public Guid? RebateRealizationId { get; set; }
    /// <summary>
    /// Voided when the triggering purchase is cancelled. Never deleted — a realization
    /// may already reference it, and a closed reporting period may already have counted
    /// it. Voided accruals are excluded from outstanding totals and effective cost.
    /// </summary>
    public bool IsVoided { get; set; }

    public DateTime AccrualDate { get; set; } = DateTime.UtcNow;

    public RebateRule?        Rule        { get; set; }
    public Supplier?          Supplier    { get; set; }
    public Purchase?          Purchase    { get; set; }
    public RebateRealization? Realization { get; set; }
}
