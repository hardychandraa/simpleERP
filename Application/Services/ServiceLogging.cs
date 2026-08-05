using System.Runtime.CompilerServices;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;

namespace SimpleERP.Application.Services;

/// <summary>
/// Records a refused operation on its way back to the caller.
///
/// Every service method that turns a request down returns <c>ServiceResult.Fail(reason)</c>.
/// The user sees the reason; until this existed, nothing else did — so the most common
/// support question there is, "it won't let me do this and I don't know why", had no
/// evidence behind it at all. There are 238 such sites, and hand-writing a log line at
/// each would have been 238 chances to write a slightly different one.
///
/// Instead the reason is logged here, once, and <see cref="CallerMemberNameAttribute"/>
/// supplies the operation name for free — so <c>SaleService.CreateAsync</c> refusing
/// produces "Refused CreateAsync: Customer 'X' is inactive." against a SourceContext of
/// <c>SimpleERP.Application.Services.SaleService</c>, with no per-site authoring.
///
/// Warning, not Error: a refusal is the application working correctly. It is worth
/// keeping because a *pattern* of refusals is a signal — the same operator hitting the
/// same wall all week is a training or a design problem — but no single one is a fault.
///
/// Note these land in the diagnostic log only, never in <c>AuditLog</c>. An attempt that
/// was blocked is not a business event, and mixing the two would stop the audit trail
/// meaning "what actually happened".
/// </summary>
public static class ServiceLogging
{
    private const string RefusedTemplate = "Refused {Operation}: {Reason}";

    // ── LocalizedString overloads ─────────────────────────────────────────────
    // Almost every call site passes _loc["…"], which is a LocalizedString, and these
    // overloads exist so that the log and the screen can disagree on language —
    // deliberately.
    //
    // LocalizedString carries both halves: Name is the resource key, which by this
    // project's convention IS the canonical English sentence, and Value is the
    // translation for whatever culture the operator has selected. So the user is shown
    // Value and the log records Name.
    //
    // Without this the logged text would follow the operator's cookie, and a log read
    // months later would be a permanent mix of English and Indonesian — the exact
    // outcome HC rejected for the audit log (see decisions.md, 2026-08-01). A
    // diagnostic record has to be greppable by one stable phrase.

    /// <summary>Logs the refusal in canonical English, returns it in the user's language.</summary>
    public static ServiceResult Refuse(this ILogger log, LocalizedString reason,
        [CallerMemberName] string operation = "")
    {
        log.LogWarning(RefusedTemplate, operation, reason.Name);
        return ServiceResult.Fail(reason.Value);
    }

    /// <summary>Generic mirror of the above, for methods that return data on success.</summary>
    public static ServiceResult<T> Refuse<T>(this ILogger log, LocalizedString reason,
        [CallerMemberName] string operation = "")
    {
        log.LogWarning(RefusedTemplate, operation, reason.Name);
        return ServiceResult<T>.Fail(reason.Value);
    }

    // ── Plain-string overloads ────────────────────────────────────────────────
    // For the handful of sites that forward an already-resolved message, such as an
    // inner service's ServiceResult.Error. Nothing better is available there: the key
    // was consumed when that message was produced.

    /// <summary>Logs the refusal and returns the failed result, unchanged.</summary>
    public static ServiceResult Refuse(this ILogger log, string reason,
        [CallerMemberName] string operation = "")
    {
        log.LogWarning(RefusedTemplate, operation, reason);
        return ServiceResult.Fail(reason);
    }

    /// <summary>
    /// Generic mirror, for the methods that return data on success. The type argument has
    /// to be written out at the call site because it cannot be inferred from a failure —
    /// there is no value to infer it from.
    /// </summary>
    public static ServiceResult<T> Refuse<T>(this ILogger log, string reason,
        [CallerMemberName] string operation = "")
    {
        log.LogWarning(RefusedTemplate, operation, reason);
        return ServiceResult<T>.Fail(reason);
    }
}
