namespace SimpleERP.Domain.Entities;

/// <summary>
/// Diagnostic log — what went wrong, for debugging. Deliberately NOT the same thing as
/// <see cref="AuditLog"/>, which is the business trail of what actually happened and is
/// written by the services themselves.
///
/// The distinction matters and is easy to erode: AuditLog answers "who posted this
/// invoice", this answers "why did the app refuse, or crash, at 14:32". Refused
/// operations belong here and never in AuditLog — an attempt that was blocked is not a
/// business event, and mixing the two would stop AuditLog meaning anything.
///
/// Only Warning and above reaches this table. Everything, at every level, also goes to
/// the rolling file in logs/ — which is the authoritative record, because it survives
/// the database being down and this table cannot.
///
/// Immutable. Never update rows; deletion is by retention sweep only.
/// </summary>
public class AppLog
{
    public long     Id        { get; set; }          // auto-increment for ordering
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Serilog level name — Warning, Error or Fatal. Stored as text, not an
    /// enum: this table is read by SQL far more often than by C#, and the level being
    /// legible in a query result is worth more than four bytes.</summary>
    public string   Level     { get; set; } = string.Empty;

    /// <summary>The rendered message, already formatted with its parameters.</summary>
    public string   Message   { get; set; } = string.Empty;

    /// <summary>Full exception text including stack trace, when one was attached.</summary>
    public string?  Exception { get; set; }

    /// <summary>Emitting type, e.g. "SimpleERP.Application.Services.SaleService".</summary>
    public string?  Source    { get; set; }

    /// <summary>
    /// Ties this entry to one HTTP request, and to the reference shown on the error page.
    /// The whole point of surfacing that reference to the user is that it can be pasted
    /// into a query here.
    /// </summary>
    public string?  CorrelationId { get; set; }

    /// <summary>Request path this arose on, when there was one. Null for startup and
    /// background-service entries, which belong to no request.</summary>
    public string?  RequestPath   { get; set; }
}
