using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace SimpleERP.Web.Services;

/// <summary>
/// The app's one-line messages travel in the redirect URL (<c>?msg=…&amp;err=…</c>), and every
/// page showed whatever text it found there: a crafted link such as
/// <c>/Sales/{id}?msg=Transfer the rest to BCA 123-456</c> displayed that sentence as the
/// app's own message (security review R10, 2026-10-09).
///
/// One middleware instead of changing ~40 handlers: on the way out it signs the message of
/// every redirect the app itself issues (an HMAC over msg + err, appended as <c>ms=</c>);
/// on the way in it drops <c>msg</c>/<c>err</c> from a GET whose signature doesn't match,
/// before any page binds them. The key is per process: after a restart an old link simply
/// shows no message.
/// </summary>
public class FlashMessageSigning
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private readonly RequestDelegate _next;
    public FlashMessageSigning(RequestDelegate next) => _next = next;

    private static string Sign(string msg, string err)
    {
        var mac = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(msg + "\n" + err));
        return WebEncoders.Base64UrlEncode(mac, 0, 16);
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        if (q.ContainsKey("msg"))
        {
            var ok = q.TryGetValue("ms", out var sig)
                  && CryptographicOperations.FixedTimeEquals(
                         Encoding.ASCII.GetBytes(sig.ToString()),
                         Encoding.ASCII.GetBytes(Sign(q["msg"].ToString(), q["err"].ToString())));
            if (!ok)
            {
                var kept = q.Where(kv => kv.Key is not ("msg" or "err" or "ms"))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                ctx.Request.Query = new QueryCollection(kept);
            }
        }

        ctx.Response.OnStarting(() =>
        {
            var loc = ctx.Response.Headers.Location.ToString();
            var at  = loc.IndexOf('?');
            if (ctx.Response.StatusCode is >= 300 and < 400 && at >= 0)
            {
                var query = QueryHelpers.ParseQuery(loc[(at + 1)..]);
                if (query.TryGetValue("msg", out var msg) && !query.ContainsKey("ms"))
                {
                    query.TryGetValue("err", out var err);
                    ctx.Response.Headers.Location = loc + "&ms=" + Uri.EscapeDataString(Sign(msg.ToString(), err.ToString()));
                }
            }
            return Task.CompletedTask;
        });

        await _next(ctx);
    }
}
