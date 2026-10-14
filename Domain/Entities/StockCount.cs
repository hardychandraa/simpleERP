using SimpleERP.Domain.Enums;
namespace SimpleERP.Domain.Entities;

/// <summary>
/// A physical stock count (stock opname, HC 2026-10-14). Staff count blind (the system figure is
/// not shown while counting); on submit the system stock of every line is recorded at that
/// instant, and any difference waits for Admin, who posts it as an adjustment or explains it.
/// Stock only changes through Admin's decision.
/// </summary>
public class StockCount
{
    public Guid     Id          { get; set; }
    /// <summary>SO-yyMM-0001, given on submit (a draft has none, so abandoned drafts leave no gaps).</summary>
    public string?  CountNumber { get; set; }
    public Guid     BranchId    { get; set; }
    public StockCountScope  Scope  { get; set; }
    public StockCountStatus Status { get; set; } = StockCountStatus.Draft;
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = string.Empty;
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// The instant the counts were submitted (UTC): the moment the count describes. Each line's
    /// SystemQty is the stock at this instant, and an adjustment Admin posts later is dated here.
    /// </summary>
    public DateTime? SubmittedAt { get; set; }
    public string?   SubmittedBy { get; set; }
    /// <summary>When the last line was decided, or the count was cancelled.</summary>
    public DateTime? ClosedAt    { get; set; }
    public string?   ClosedBy    { get; set; }

    public List<StockCountLine> Lines { get; set; } = new();
}

public class StockCountLine
{
    public Guid    Id           { get; set; }
    public Guid    StockCountId { get; set; }
    public Guid    ProductId    { get; set; }
    /// <summary>Whole units (HC, 2026-10-14). Null while the draft line is not counted yet.</summary>
    public int?    CountedQty   { get; set; }
    /// <summary>System stock at <see cref="StockCount.SubmittedAt"/>; null until submitted.</summary>
    public decimal? SystemQty   { get; set; }
    public string? Note         { get; set; }
    public StockCountDecision Decision { get; set; } = StockCountDecision.Pending;
    public string?   DecisionNote { get; set; }
    public string?   DecidedBy    { get; set; }
    public DateTime? DecidedAt    { get; set; }
    /// <summary>The StockAdjustment (and ledger ReferenceId) Admin posted for this line.</summary>
    public Guid?     StockAdjustmentId { get; set; }
    /// <summary>The quantity Admin actually posted (+ in, − out), which can differ from the
    /// difference at submit when a document dated before the count was entered after it.</summary>
    public decimal?  AdjustedQty  { get; set; }

    public StockCount? StockCount { get; set; }
    public Product?    Product    { get; set; }
}
