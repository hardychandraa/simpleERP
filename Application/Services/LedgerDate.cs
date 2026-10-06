namespace SimpleERP.Application.Services;

/// <summary>
/// The stock-ledger date for a document dated <paramref name="localDay"/> (the business's
/// local calendar day, as purchases and returns store it).
///
/// Today keeps the real clock time, so same-day order and End of Day are unchanged; a
/// backdated document is anchored to that day's local midnight, as a UTC instant. Same
/// rule SaleService already applies to SaleDate, so a sale and its ledger rows agree.
/// Order within a backdated day is the posting order (InventoryLedger.EnteredAt).
/// </summary>
internal static class LedgerDate
{
    public static DateTime FromLocalDay(DateTime localDay) =>
        localDay.Date == DateTime.Now.Date
            ? DateTime.UtcNow
            : DateTime.SpecifyKind(localDay.Date, DateTimeKind.Local).ToUniversalTime();
}
