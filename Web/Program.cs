using SimpleERP.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

// Npgsql maps DateTime to `timestamp with time zone` by default and throws at runtime on
// any DateTime whose Kind isn't Utc. This codebase mixes DateTime.UtcNow, DateTime.Now and
// form-bound dates (Kind=Unspecified), so opt into the legacy mapping
// (`timestamp without time zone`) to preserve the SQLite-era semantics.
// Deliberate: fine for a single-timezone business. Revisit if timezone correctness matters.
// Must be set before any Npgsql type is used.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Security headers ─────────────────────────────────────────────────────────
builder.Services.AddAntiforgery(options => {
    options.HeaderName = "X-CSRF-TOKEN";    // for fetch() API calls
    options.Cookie.SecurePolicy  = CookieSecurePolicy.SameAsRequest;
    options.Cookie.HttpOnly      = true;
    options.Cookie.SameSite      = SameSiteMode.Strict;
});

builder.Services.AddRazorPages(options => {
    // All Razor Pages require antiforgery by default (already the case, explicit for clarity)
})
.AddMvcOptions(o => {
    // <input type="number"> always posts an invariant floating-point number, but the
    // default binder reads it with the server's culture — where a dot is a thousands
    // separator. Without this, a posted 3800000.0000 binds as 38,000,000,000.
    o.ModelBinderProviders.Insert(0, new SimpleERP.Web.Services.InvariantDecimalModelBinderProvider());
});
builder.Services.AddControllers();

// ── Localization ──────────────────────────────────────────────────────────────
// Resources live in the Application project beside SharedResource.cs, whose full type
// name is also the resource manifest prefix — so no ResourcesPath is set here.
builder.Services.AddLocalization();

// ── Database ─────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("SimpleERP")
    ?? throw new InvalidOperationException(
        "No 'SimpleERP' connection string configured. For local development run:\n" +
        "  dotnet user-secrets set \"ConnectionStrings:SimpleERP\" \"Host=localhost;Database=simpleerp;Username=simpleerp;Password=...\" --project Web\n" +
        "In deployment, supply it via environment configuration.");
builder.Services.AddInfrastructure(connectionString);

// ── Auto-backup ───────────────────────────────────────────────────────────────
// pg_dump to /backups — once at startup, then nightly. Keeps the last 30.
// (Previously this was an inline File.Copy of the SQLite file here, duplicating an
// unregistered BackupService. Now consolidated onto the single hosted service.)
builder.Services.AddHostedService<SimpleERP.Web.Services.BackupService>();

var app = builder.Build();

// ── DB init ──────────────────────────────────────────────────────────────────
// Applies pending EF Core migrations.
await DependencyInjection.InitDatabaseAsync(app.Services);

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

// Security headers on every response
app.Use(async (ctx, next) => {
    ctx.Response.Headers["X-Content-Type-Options"]  = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]         = "SAMEORIGIN";
    ctx.Response.Headers["X-XSS-Protection"]        = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
    // CSP: allow only same-origin scripts/styles; no inline scripts except the ones we write
    ctx.Response.Headers["Content-Security-Policy"]  =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';";
    await next();
});

// Inject antiforgery token into every page for use by fetch() JS calls
app.Use(async (ctx, next) => {
    if (ctx.Request.Path.StartsWithSegments("/api") == false) {
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
            new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict });
    }
    await next();
});

// ── Language (EN/ID) ──────────────────────────────────────────────────────────
// Must run before anything renders, so every page sees the right CurrentUICulture.
//
// Indonesian is the default: staff are primarily Indonesian-speaking and the business
// vocabulary (PPN, HPP, Laba Kotor…) is already Indonesian throughout. English is opt-in.
//
// Cookie is the ONLY culture provider, deliberately. The framework default also consults
// the browser's Accept-Language header, which would silently hand an English-locale
// browser an English UI and make the intended Indonesian default look broken. The
// requirement is an explicit per-browser choice, not implicit detection — so the header
// provider is left out rather than merely reordered.
var supportedCultures = new[] { "id", "en" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("id")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// Assigning the list REPLACES the framework's three defaults. Note that
// AddInitialRequestCultureProvider() would not: it only prepends, leaving
// AcceptLanguageHeaderRequestCultureProvider in place to answer whenever the cookie is
// absent — which silently served English to a fresh en-US browser and made the Indonesian
// default look broken. Verified live: with the cookie cleared the app must render Indonesian.
localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new CookieRequestCultureProvider()
};

app.UseRequestLocalization(localizationOptions);

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();
app.MapRazorPages();
app.MapControllers();

// Sets the language cookie and returns to where the user was. GET rather than POST, and
// therefore no antiforgery token: it writes a display preference, not business data, and
// the worst a forged request can do is show the victim their own app in the other language.
// LocalRedirect (not Redirect) so a crafted returnUrl can't bounce anyone off-site.
app.MapGet("/set-language", (string culture, string? returnUrl, HttpContext ctx) =>
{
    if (!supportedCultures.Contains(culture)) culture = "id";

    ctx.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions
        {
            Expires    = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,          // a UI preference, not tracking — survives consent gating
            SameSite   = SameSiteMode.Strict,
            HttpOnly   = false           // harmless to read client-side; no secret in it
        });

    return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

app.Run();
