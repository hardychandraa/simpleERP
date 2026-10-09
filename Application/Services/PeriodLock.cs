using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Month lock (tutup buku, HC 2026-10-08). Once the books are closed through a month-end, no
/// document dated on or before it may be created, changed, cancelled or deleted, so a P&amp;L or
/// PPN figure that has been reported can't move afterwards.
///
/// New events dated in the open period stay allowed even when they touch an old document: a
/// payment today on a September invoice, a credit note applied today, a return dated today of a
/// September sale. Collecting old receivables is normal business, not a change to September.
///
/// Only months that have fully ended can be closed (see AppSettingsService), so anything dated
/// "now" — payments, stock in, adjustments, payouts — is always in the open period and needs no
/// check of its own.
///
/// Returns the refusal text rather than a ServiceResult so the caller's own _log.Refuse records
/// the operation name. Scoped: the lock date is read once per request.
/// </summary>
public sealed class PeriodLock
{
    private readonly IAppSettingsRepository _settings;
    private readonly IStringLocalizer<SharedResource> _loc;
    private DateTime? _closedThrough;
    private bool _loaded;

    public PeriodLock(IAppSettingsRepository settings, IStringLocalizer<SharedResource> loc)
    { _settings = settings; _loc = loc; }

    /// <summary>Last closed day (a local date), or null when nothing is closed.</summary>
    public async Task<DateTime?> ClosedThroughAsync()
    {
        if (!_loaded) { _closedThrough = (await _settings.GetAsync()).BooksClosedThrough?.Date; _loaded = true; }
        return _closedThrough;
    }

    /// <summary>The local calendar day of a stored UTC instant (sale dates, ledger rows).</summary>
    public static DateTime LocalDay(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().Date;

    public async Task<bool> IsClosedAsync(DateTime localDay) =>
        await ClosedThroughAsync() is { } c && localDay.Date <= c;

    /// <summary>Refusal for a new document (or a new date on one) that falls in a closed month.</summary>
    public async Task<LocalizedString?> NewDateAsync(DateTime localDay)
    {
        if (!await IsClosedAsync(localDay)) return null;
        var c = _closedThrough!.Value;
        return _loc["The date {0} is in a closed period (books closed through {1}). Use a later date, or ask an administrator to reopen the period.",
                    localDay.ToString("dd MMM yyyy"), c.ToString("dd MMM yyyy")];
    }

    /// <summary>Refusal for changing, cancelling or deleting an existing document dated in a closed month.</summary>
    public async Task<LocalizedString?> ExistingAsync(string document, DateTime localDay)
    {
        if (!await IsClosedAsync(localDay)) return null;
        var c = _closedThrough!.Value;
        return _loc["{0} is dated {1}, in a closed period (books closed through {2}). It can no longer be changed or cancelled; ask an administrator to reopen the period.",
                    document, localDay.ToString("dd MMM yyyy"), c.ToString("dd MMM yyyy")];
    }
}
