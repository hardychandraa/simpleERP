using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;
using System.Diagnostics;
namespace SimpleERP.Web.Pages;
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel {
    /// <summary>
    /// The reference shown to the user. This is the correlation ID assigned by
    /// <see cref="RequestDiagnosticsMiddleware"/>, which is also on every log line the
    /// failed request emitted and in the <c>AppLogs</c> table — so quoting it is enough
    /// to find the exception:
    ///   SELECT * FROM "AppLogs" WHERE "CorrelationId" = '…';
    ///
    /// It used to be <c>Activity.Current?.Id ?? TraceIdentifier</c>, which appeared
    /// nowhere else and so could never be looked up. Those remain the fallback for the
    /// case where the error escaped before the middleware ran.
    /// </summary>
    public string? RequestId { get; set; }
    public void OnGet() =>
        RequestId = HttpContext.Items[RequestDiagnosticsMiddleware.CorrelationIdItemKey] as string
                 ?? Activity.Current?.Id
                 ?? HttpContext.TraceIdentifier;
}
