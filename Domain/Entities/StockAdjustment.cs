namespace SimpleERP.Domain.Entities;

/// <summary>Records a manual inventory correction with reason.</summary>
public class StockAdjustment
{
    public Guid     Id             { get; set; }
    public Guid     ProductId      { get; set; }
    public Guid     BranchId       { get; set; }
    public DateTime AdjustmentDate { get; set; } = DateTime.UtcNow;
    public decimal  QtyBefore      { get; set; }
    public decimal  QtyAfter       { get; set; }
    public decimal  QtyDelta       => QtyAfter - QtyBefore;
    public string   Reason         { get; set; } = string.Empty;
    public string   CreatedBy      { get; set; } = "staff";

    // Navigation
    public Product? Product { get; set; }
}
