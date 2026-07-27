namespace SimpleERP.Domain.Entities;

/// <summary>
/// Commission earned by a salesperson on one sale line, as revenue is collected — the
/// accrual half of the pattern (mirrors RebateAccrual).
///
/// Deliberately per SaleItem, not per Sale: it costs nothing now and lets a future
/// product- or category-specific commission rule work without a redesign. Earned on
/// collection, not at invoice time — a credit sale accrues in slices as each payment
/// comes in, so commission is never paid on debt that's never collected.
/// </summary>
public class CommissionAccrual
{
    public Guid Id            { get; set; }
    public Guid SaleId        { get; set; }
    public Guid SaleItemId    { get; set; }
    /// <summary>
    /// The payment this slice was earned against. Null for a cash sale, where collection
    /// happens at creation and no PaymentRecord row exists.
    /// </summary>
    public Guid? PaymentRecordId { get; set; }
    public Guid SalesPersonId    { get; set; }
    public Guid CommissionRuleId { get; set; }

    /// <summary>The revenue slice this commission was figured on (ex-PPN).</summary>
    public decimal BaseAmount { get; set; }
    /// <summary>Rate snapshot at accrual time — the rule may change later.</summary>
    public decimal Rate       { get; set; }
    /// <summary>The commission itself: round(Rate% × BaseAmount).</summary>
    public decimal Amount     { get; set; }

    /// <summary>Null = unpaid. Set to the payout that settled it.</summary>
    public Guid? CommissionPayoutId { get; set; }
    /// <summary>Voided when the sale is cancelled. Never deleted.</summary>
    public bool  IsVoided     { get; set; }
    public DateTime AccrualDate { get; set; } = DateTime.UtcNow;

    public Sale?             Sale        { get; set; }
    public SaleItem?         SaleItem    { get; set; }
    public SalesPerson?      SalesPerson { get; set; }
    public CommissionRule?   Rule        { get; set; }
    public CommissionPayout? Payout      { get; set; }
}
