namespace SimpleERP.Application.Services;

/// <summary>
/// Ceilings on what one line or one document may carry. Money columns are decimal(18,4), so
/// anything near 10^14 overflows in the database: Goods In accepted 1,000,000,000 units and
/// 10^15 units ended in a 500 (security review R8, 2026-10-09). These are sanity limits far
/// above any real trade, not business rules: 200 lines × 100 billion stays under 10^14.
/// </summary>
public static class Limits
{
    public const decimal MaxQty        = 100_000m;           // units on one line
    public const decimal MaxUnitAmount = 1_000_000_000m;     // a price or cost per unit
    public const decimal MaxLineAmount = 100_000_000_000m;   // qty × price, and any single amount (a note, an expense)
    public const int     MaxLines      = 200;                // lines on one document
}
