using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;

namespace SimpleERP.Web.Services;

/// <summary>
/// Refuses a POST when a submitted value can't be read, instead of running the handler with
/// the property's default: a New Sale with the date "31/31/2026" posted dated today, an
/// invoice discount of "abc" posted as 0, payment type 99 as Cash (security review R7,
/// 2026-10-09). The browser's own inputs never send such values, so this only meets a
/// tampered form. A blank field is not a failure (its attempted value is empty): blank
/// optional fields keep working as before.
/// </summary>
public class BindingFailureFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var req = context.HttpContext.Request;
        if (HttpMethods.IsPost(req.Method)
            && context.HandlerInstance?.GetType().IsDefined(typeof(HandlesBindingErrorsAttribute), true) != true)
        {
            var bad = context.ModelState
                .Where(kv => kv.Value is { Errors.Count: > 0 } v && !string.IsNullOrEmpty(v.AttemptedValue))
                .Select(kv => kv.Key).ToList();
            if (bad.Count > 0)
            {
                var sp = context.HttpContext.RequestServices;
                sp.GetRequiredService<ILogger<BindingFailureFilter>>().LogWarning(
                    "Refused {Method} {Path}{Query} by {User}: unreadable value(s) in {Fields}",
                    req.Method, req.Path, req.QueryString, context.HttpContext.User.Identity?.Name, string.Join(", ", bad));
                // A plain 400 rather than a redirect back: several of these pages (New Sale,
                // Goods In, returns) don't display a ?msg=, and only a tampered form gets here.
                var loc = sp.GetRequiredService<IStringLocalizer<SharedResource>>();
                context.Result = new ContentResult {
                    StatusCode  = StatusCodes.Status400BadRequest,
                    ContentType = "text/plain; charset=utf-8",
                    Content     = loc["A value on the form could not be read ({0}). Nothing was saved.", string.Join(", ", bad)].Value
                };
                return;
            }
        }
        await next();
    }
}

/// <summary>
/// The page model checks ModelState itself and shows its own message (Customers, Products,
/// Stok Masuk, Adjust, Settings), so BindingFailureFilter leaves it alone.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HandlesBindingErrorsAttribute : Attribute { }
