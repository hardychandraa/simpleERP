using SimpleERP.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;

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

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();
app.MapRazorPages();
app.MapControllers();

app.Run();
