using SimpleERP.Domain.Enums;

namespace SimpleERP.Application.DTOs;

// ── Daily stock sheet (Cek Stok Harian, HC 2026-10-14) ───────────────────────
/// <summary>Opening, movements and closing per product for one local day, from the stock ledger.</summary>
public class DailyStockSheetDto {
    public DateTime Day  { get; set; }
    public List<DailyStockRowDto> Rows { get; set; } = new();
}

public class DailyStockRowDto {
    public Guid    ProductId   { get; set; }
    public string  ProductName { get; set; } = "";
    public string  SKU         { get; set; } = "";
    public string? Category    { get; set; }
    public bool    IsActive    { get; set; }
    public decimal Opening     { get; set; }
    /// <summary>Purchases, manual stock in and rebate goods.</summary>
    public decimal InPurchase  { get; set; }
    public decimal InReturn    { get; set; }
    /// <summary>Cancellations and adjustments that added stock.</summary>
    public decimal InOther     { get; set; }
    public decimal OutSale     { get; set; }
    public decimal OutReturn   { get; set; }
    /// <summary>Cancellations and adjustments that took stock out.</summary>
    public decimal OutOther    { get; set; }
    public decimal In      => InPurchase + InReturn + InOther;
    public decimal Out     => OutSale + OutReturn + OutOther;
    public decimal Closing => Opening + In - Out;
    /// <summary>Any movement dated this day, or written this day (a backdated document).</summary>
    public bool    Moved       { get; set; }

    /// <summary>The day's last submitted count of this product, if any.</summary>
    public Guid?   CountId     { get; set; }
    public string? CountNumber { get; set; }
    public int?    CountedQty  { get; set; }
    public StockCountDecision? CountDecision { get; set; }
    /// <summary>
    /// Documents dated before that count but entered after it (backdated past a count, the G5
    /// risk): what the count saw is no longer what the sheet says for that moment.
    /// </summary>
    public List<string> EnteredAfterCount { get; set; } = new();
}

// ── Stock count (stock opname) ───────────────────────────────────────────────
public class StockCountListDto {
    public Guid    Id          { get; set; }
    public string? CountNumber { get; set; }
    public StockCountScope  Scope  { get; set; }
    public StockCountStatus Status { get; set; }
    public string  CreatedBy   { get; set; } = "";
    public DateTime  CreatedAt   { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string?   SubmittedBy { get; set; }
    public int     LineCount       { get; set; }
    public int     DifferenceCount { get; set; }
    public int     PendingCount    { get; set; }
}

public class StockCountDto : StockCountListDto {
    public string?   Notes    { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string?   ClosedBy { get; set; }
    public List<StockCountLineDto> Lines { get; set; } = new();
}

public class StockCountLineDto {
    public Guid    Id          { get; set; }
    public Guid    ProductId   { get; set; }
    public string  ProductName { get; set; } = "";
    public string  SKU         { get; set; } = "";
    public int?    CountedQty  { get; set; }
    /// <summary>System stock at the count moment. Never shown while counting (blind count).</summary>
    public decimal? SystemQty  { get; set; }
    public decimal? Difference => CountedQty.HasValue && SystemQty.HasValue ? CountedQty.Value - SystemQty.Value : null;
    public string? Note        { get; set; }
    public StockCountDecision Decision { get; set; }
    public string?   DecisionNote { get; set; }
    public string?   DecidedBy    { get; set; }
    public DateTime? DecidedAt    { get; set; }
    public decimal?  AdjustedQty  { get; set; }

    // Review only (Admin, pending lines): the difference against the stock at the count moment
    // as the ledger says it now, after documents dated before the count but entered after it.
    public decimal?  CurrentDifference { get; set; }
    public List<LateEntryDto> EnteredAfterCount { get; set; } = new();
}

public class LateEntryDto {
    public string   DocumentNumber { get; set; } = "";
    public string?  Link           { get; set; }
    public string   ReferenceType  { get; set; } = "";
    /// <summary>+ in, − out.</summary>
    public decimal  Qty            { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime EnteredAt      { get; set; }
}

/// <summary>A counted quantity (whole units) and note for one line, as entered on the form.</summary>
public class StockCountLineInput {
    public Guid    LineId { get; set; }
    public int?    Qty    { get; set; }
    public string? Note   { get; set; }
}

public class StockCountSubmitResult {
    public bool    Success { get; init; }
    public string? Error   { get; init; }
    /// <summary>Products that moved while the form was open: their counts were kept but must be checked.</summary>
    public List<Guid> MovedProductIds { get; init; } = new();
}
