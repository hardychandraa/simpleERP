using Serilog.Context;

namespace SimpleERP.Web.Services;

/// <summary>
/// Gives every request a short correlation ID, attaches it (and the request path) to every
/// log line the request produces, and logs anything that escapes the pipeline.
///
/// The correlation ID is the point of the whole thing. Before this, the error page showed
/// the framework's trace identifier — a value that appeared nowhere else, so a user
/// reading it out could not be matched to anything. Now the same ID is on the error page,
/// on every log line from that request, in the <c>AppLogs</c> table and in the
/// <c>X-Correlation-ID</c> response header, so "I got an error, reference a3f9c2e14b7d"
/// is answerable with one query.
///
/// It sits near the top of the pipeline so the ID exists before anything can fail, and it
/// re-throws after logging: rendering the error response stays the job of
/// <c>UseExceptionHandler</c>, and swallowing here would turn a 500 into a blank 200.
/// </summary>
public class RequestDiagnosticsMiddleware
{
    public const string CorrelationIdItemKey = "CorrelationId";
    private const string HeaderName          = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestDiagnosticsMiddleware> _logger;

    public RequestDiagnosticsMiddleware(RequestDelegate next, ILogger<RequestDiagnosticsMiddleware> logger)
    { _next = next; _logger = logger; }

    public async Task InvokeAsync(HttpContext context)
    {
        // Short enough to read down a phone line, wide enough not to collide in practice.
        // Deliberately generated here rather than taken from a client header: an
        // attacker-supplied value would land verbatim in the log and in the table.
        var correlationId = Guid.NewGuid().ToString("N")[..12];

        context.Items[CorrelationIdItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath",   context.Request.Path.Value))
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                // Log here rather than relying on the framework's own unhandled-exception
                // entry, which carries none of this request's context and is emitted under
                // a category this app pins to Warning.
                _logger.LogError(ex,
                    "Unhandled exception on {Method} {Path} — reference {CorrelationId}",
                    context.Request.Method, context.Request.Path.Value, correlationId);
                throw;
            }
        }
    }
}
