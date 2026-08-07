using SimpleERP.Infrastructure;
using SimpleERP.Infrastructure.Logging;
using SimpleERP.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;
using System.Globalization;
using Serilog;
using Serilog.Events;

// Npgsql maps DateTime to `timestamp with time zone` by default and throws at runtime on
// any DateTime whose Kind isn't Utc. This codebase mixes DateTime.UtcNow, DateTime.Now and
// form-bound dates (Kind=Unspecified), so opt into the legacy mapping
// (`timestamp without time zone`) to preserve the SQLite-era semantics.
// Deliberate: fine for a single-timezone business. Revisit if timezone correctness matters.
// Must be set before any Npgsql type is used.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
// Configured immediately after the builder so that everything below — a missing
// connection string, a failed migration, a DI misconfiguration — is recorded rather
// than only printed to a console nobody is watching. Before this existed, closing the
// console window destroyed every diagnostic the app had ever produced.
//
// Two sinks, and the split is deliberate:
//   • Rolling file in logs/ — the AUTHORITATIVE record. Every level, one file per day,
//     30 days retained (the same discipline as backups/), with a size cap so a runaway
//     loop cannot fill the disk. It keeps working when PostgreSQL does not, which is
//     precisely when the log matters most.
//   • AppLogs table — Warning and above only, so incidents are queryable in SQL like
//     everything else here and can back the viewer page. It can never record a database
//     outage, hence it is the convenience and the file is the source of truth.
//
// EF Core is pinned to Warning: at Information it logs the text of every SQL statement,
// which would bury the business events this exists to capture and churn the disk for no
// benefit. Raise it deliberately and temporarily when debugging a query.
const int LogRetainedFileCount = 30;
var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "logs");
Directory.CreateDirectory(logDirectory);

var logConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft",                     LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore",          LogEventLevel.Warning)
    .MinimumLevel.Override("System",                        LogEventLevel.Warning)
    // Required for the per-request CorrelationId/RequestPath properties pushed below to
    // reach the sinks.
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path                  : Path.Combine(logDirectory, "simpleerp-.log"),
        rollingInterval       : RollingInterval.Day,
        retainedFileCountLimit: LogRetainedFileCount,
        fileSizeLimitBytes    : 50L * 1024 * 1024,
        rollOnFileSizeLimit   : true,
        outputTemplate:
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}" +
            " «{SourceContext}»{NewLine}{Exception}");

// Only when there is a database to write to. A missing connection string is itself an
// error worth logging — to the file, which is why the file sink is configured first.
var logConnectionString = builder.Configuration.GetConnectionString("SimpleERP");
if (!string.IsNullOrWhiteSpace(logConnectionString))
    logConfig = logConfig.WriteTo.AppLogTable(logConnectionString, LogEventLevel.Warning);

Log.Logger = logConfig.CreateLogger();
builder.Host.UseSerilog();

// ── Security headers ─────────────────────────────────────────────────────────
builder.Services.AddAntiforgery(options => {
    options.HeaderName = "X-CSRF-TOKEN";    // for fetch() API calls
    options.Cookie.SecurePolicy  = CookieSecurePolicy.SameAsRequest;
    options.Cookie.HttpOnly      = true;
    options.Cookie.SameSite      = SameSiteMode.Strict;
});

// ── Authentication ───────────────────────────────────────────────────────────
// Plain cookie authentication, no ASP.NET Core Identity. The app needs a username, a
// password check and a role; Identity would bring UserManager, SignInManager and seven
// tables for features this business does not have (email confirmation, 2FA, external
// logins). Password hashes are still Identity's own format — see PasswordHasherAdapter —
// so adopting the full framework later would not invalidate a single stored credential.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath        = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.LogoutPath       = "/Account/Logout";
        // A working day, sliding: staff should not be logged out mid-invoice, but a
        // machine left overnight should not still be signed in the next morning.
        options.ExpireTimeSpan   = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name         = "SimpleERP.Auth";
        options.Cookie.HttpOnly     = true;   // unlike the culture cookie, this IS a secret
        options.Cookie.SameSite     = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential  = true;

        // Re-check the account on every request.
        //
        // An auth cookie is self-contained and signed: without this, deactivating someone
        // only stops them logging in *again*, while the session they already have keeps
        // working until the cookie expires — up to 12 hours of continued access to a
        // person who was just switched off. Verified live: a cookie for a deleted user
        // still opened /Sales/Create.
        //
        // A role change is picked up the same way, so demoting an Admin takes effect on
        // their next click rather than at their next login.
        options.Events.OnValidatePrincipal = async ctx => {
            var username = ctx.Principal?.Identity?.Name;
            var users    = ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            var account  = username == null ? null : await users.GetByUsernameAsync(username);

            if (account == null || !account.IsActive
                || !ctx.Principal!.IsInRole(account.Role.ToString()))
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options => {
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
});

builder.Services.AddRazorPages(options => {
    // All Razor Pages require antiforgery by default (already the case, explicit for clarity)

    // ── Access control ───────────────────────────────────────────────────────
    // Deny by default: every page requires a login unless it is named below. Stated as
    // a convention rather than ~50 [Authorize] attributes so that a page added later is
    // protected because nobody did anything, rather than exposed because somebody forgot.
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Denied");
    options.Conventions.AllowAnonymousToPage("/Error");
    // Creates the first Admin on an empty database, then refuses forever. Anonymous by
    // necessity — there is nobody to authenticate as until it has been used once.
    options.Conventions.AllowAnonymousToPage("/Account/Setup");

    // Admin-only: what the business earns, what it spends, and what anyone is paid.
    // Staff run the day-to-day operation (sales, purchases, payments, returns, stock)
    // and deliberately cannot see commission, rebate income, expenses or the financial
    // reports. HC's call, 2026-08-07.
    options.Conventions.AuthorizeFolder("/Settings",        "AdminOnly");
    options.Conventions.AuthorizeFolder("/Commissions",     "AdminOnly");
    options.Conventions.AuthorizeFolder("/CommissionRules", "AdminOnly");
    options.Conventions.AuthorizeFolder("/Rebates",         "AdminOnly");
    options.Conventions.AuthorizeFolder("/RebateRules",     "AdminOnly");
    options.Conventions.AuthorizeFolder("/Expenses",        "AdminOnly");
    options.Conventions.AuthorizeFolder("/Audit",           "AdminOnly");
    // /Reports is split, not gated wholesale: End of Day and Warranty are operational
    // lookups staff need, while these two are the income picture.
    options.Conventions.AuthorizePage("/Reports/ProfitLoss", "AdminOnly");
    options.Conventions.AuthorizePage("/Reports/Position",   "AdminOnly");
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

// ── Log retention ─────────────────────────────────────────────────────────────
// Keeps the AppLogs table to the same 30-day window as the log files. The files are
// pruned by the sink itself; the table needs sweeping because it rides along in every
// nightly pg_dump.
builder.Services.AddHostedService<LogRetentionService>();

var app = builder.Build();

// ── Startup diagnostics ───────────────────────────────────────────────────────
// The first thing in every log file. When "it worked yesterday", this line is what
// tells you which build, which environment and which database yesterday actually meant.
// The connection string is parsed rather than printed: it carries the password.
var startupDb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
// Given a SourceContext of its own so startup entries are attributable in the file and
// filterable in the table, rather than showing an empty source like any bare Log call.
var startupLog = Log.ForContext("SourceContext", "SimpleERP.Startup");
startupLog.Information(
    "SimpleERP starting — environment {Environment}, database {Database} on {Host}:{Port}, " +
    "default culture {Culture}, logs kept {RetainedDays} days in {LogDirectory}",
    app.Environment.EnvironmentName, startupDb.Database, startupDb.Host, startupDb.Port,
    "id", LogRetainedFileCount, logDirectory);

// ── DB init ──────────────────────────────────────────────────────────────────
// Applies pending EF Core migrations.
try
{
    await DependencyInjection.InitDatabaseAsync(app.Services);
    startupLog.Information("Database migrations applied.");
}
catch (Exception ex)
{
    // A failed migration leaves the app running against a schema it does not expect, so
    // it must be recorded loudly rather than surfacing later as a column-not-found.
    startupLog.Fatal(ex, "Database migration failed — the application cannot serve requests reliably.");
    throw;
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Registered in every environment now, not just Production. In Development the framework
// still puts its own developer exception page ahead of this one, so the rich diagnostic
// page is unchanged — but the middleware below now logs the exception on the way past in
// both environments, which is the part that was missing.
app.UseExceptionHandler("/Error");

// Correlation ID + unhandled-exception logging. As early as possible, so everything
// below is covered and every log line the request emits carries its reference.
app.UseMiddleware<RequestDiagnosticsMiddleware>();

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
// Between routing and antiforgery: routing has to have selected the endpoint before
// authorization can know what it protects.
app.UseAuthentication();
app.UseAuthorization();

// Inject antiforgery token into every page for use by fetch() JS calls.
//
// Must run AFTER UseAuthentication(): an antiforgery token embeds the identity it was
// issued to, and GetAndStoreTokens caches the pair for the rest of the request. This
// block used to sit up beside the security headers, where ctx.User is still anonymous —
// so every form rendered a token stamped "anonymous", which UseAntiforgery then compared
// against the signed-in user and rejected. Every POST in the app returned 400 the moment
// logins existed. Found live; a green build cannot see this.
app.Use(async (ctx, next) => {
    if (ctx.Request.Path.StartsWithSegments("/api") == false) {
        var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
            new CookieOptions { HttpOnly = false, SameSite = SameSiteMode.Strict });
    }
    await next();
});

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

try
{
    app.Run();
    startupLog.Information("SimpleERP stopped cleanly.");
}
catch (Exception ex)
{
    // The host itself fell over — a port already in use, a bad certificate. Without this
    // the process would simply vanish, which is the single most confusing failure mode.
    startupLog.Fatal(ex, "SimpleERP terminated unexpectedly.");
    throw;
}
finally
{
    // Both sinks buffer. Without this flush the last — and most interesting — entries
    // before a crash are the ones guaranteed to be lost.
    Log.CloseAndFlush();
}
