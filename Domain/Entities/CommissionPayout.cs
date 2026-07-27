namespace SimpleERP.Domain.Entities;

/// <summary>
/// A payment of accrued commission to a salesperson — the settlement half of the
/// pattern (mirrors RebateRealization, but simpler: commission carries no withholding
/// here). One payout closes out many outstanding accruals.
/// </summary>
public class CommissionPayout
{
    public Guid     Id            { get; set; }
    public Guid     SalesPersonId { get; set; }
    public DateTime PayoutDate    { get; set; } = DateTime.UtcNow;
    public decimal  Amount        { get; set; }
    public string?  Notes         { get; set; }
    public string   CreatedBy     { get; set; } = "staff";

    public SalesPerson? SalesPerson { get; set; }
    public ICollection<CommissionAccrual> Accruals { get; set; } = new List<CommissionAccrual>();
}
