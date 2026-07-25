using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// The actual settlement of one or more accruals — the realization half of the
/// pattern. Created when the supplier reconciles and pays/credits the rebate.
///
/// Carries a real withholding split rather than a single amount: the supplier
/// reconciliation sheet shows every settlement netting through a withholding
/// (~15%, configurable) before it offsets the payable — Gross → −Withholding → Net,
/// evidenced as routine on every line, not an edge case.
/// </summary>
public class RebateRealization
{
    public Guid Id         { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime RealizationDate { get; set; } = DateTime.UtcNow;
    /// <summary>Snapshot of what kind of reward this settled.</summary>
    public RebateRewardType RewardType { get; set; }

    // ── Cash settlement (PercentDiscount / FixedCash / CreditNote / LuckyDraw) ──
    /// <summary>Gross settled amount before withholding. Null for a purely in-kind settlement.</summary>
    public decimal? GrossAmount     { get; set; }
    /// <summary>Withholding rate applied, as a fraction, snapshotted at settlement time.</summary>
    public decimal? WithholdingRate { get; set; }
    public decimal? WithholdingAmount { get; set; }
    /// <summary>What actually lands against the payable: Gross − Withholding.</summary>
    public decimal? NetAmount       { get; set; }

    // ── In-kind settlement (InKindGoods) ──
    public Guid?    InKindProductId { get; set; }
    public decimal? InKindQty       { get; set; }

    /// <summary>Free-text reference — the supplier's reconciliation doc number, etc.</summary>
    public string? ReferenceId { get; set; }
    public string? Notes       { get; set; }
    public string  CreatedBy   { get; set; } = "staff";

    public Supplier? Supplier      { get; set; }
    public Product?  InKindProduct { get; set; }
    public ICollection<RebateAccrual> Accruals { get; set; } = new List<RebateAccrual>();
}
