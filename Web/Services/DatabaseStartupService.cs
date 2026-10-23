using System.Diagnostics;
using Npgsql;
using SimpleERP.Infrastructure;

namespace SimpleERP.Web.Services;

/// <summary>
/// Waits for PostgreSQL, then applies EF Core migrations, before the web server takes a
/// request (HC, 2026-10-22: after some restarts the service stayed Stopped until someone ran
/// Start-Service by hand).
///
/// Why: Windows starts the service once the PostgreSQL *service* is running, but after a power
/// cut or an update restart Postgres can still be recovering ("the database system is starting
/// up") or not yet listening. Migrating once, before app.Run(), failed on that first try, and
/// it happened before the service had told Windows it was running, so Windows' "restart on
/// failure" never applied. Here it runs in StartingAsync: after Windows has been told the
/// service is running, but before any hosted service's StartAsync (Kestrel, the startup
/// backup). It retries what can pass by itself (no answer, starting up, timeouts) for
/// Startup:DatabaseWaitSeconds (default 180), and fails at once on what can't (wrong password,
/// no such database). A failure ends the process with exit code 1 (Program.cs), which is what
/// makes Windows restart it.
/// </summary>
public sealed class DatabaseStartupService : IHostedLifecycleService
{
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider                 _services;
    private readonly IConfiguration                   _config;
    private readonly ILogger<DatabaseStartupService>  _logger;

    public DatabaseStartupService(IServiceProvider services, IConfiguration config, ILogger<DatabaseStartupService> logger)
    {
        _services = services;
        _config   = config;
        _logger   = logger;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await WaitForDatabaseAsync(cancellationToken);
        try
        {
            await DependencyInjection.InitDatabaseAsync(_services);
            _logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            // A failed migration leaves the app running against a schema it does not expect, so
            // it must be recorded loudly rather than surfacing later as a column-not-found.
            _logger.LogCritical(ex, "Database migration failed — the application cannot serve requests reliably.");
            throw;
        }
    }

    private async Task WaitForDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = _config.GetConnectionString("SimpleERP");
        var maxWait = TimeSpan.FromSeconds(_config.GetValue("Startup:DatabaseWaitSeconds", 180));
        var clock   = Stopwatch.StartNew();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = new NpgsqlCommand("SELECT 1", conn);
                await cmd.ExecuteScalarAsync(cancellationToken);
                if (attempt > 1)
                    _logger.LogInformation("Database ready after {Seconds:N0} s ({Attempts} attempts).",
                                           clock.Elapsed.TotalSeconds, attempt);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && clock.Elapsed < maxWait && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Database not ready yet (attempt {Attempt}, {Seconds:N0} s of {Max:N0} s): {Error}",
                                   attempt, clock.Elapsed.TotalSeconds, maxWait.TotalSeconds, ex.Message);
                await Task.Delay(RetryEvery, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Worth waiting for: Postgres not listening yet, still starting up / recovering, timeouts.
    /// Not: a wrong password (SQLSTATE class 28) or a missing database (3D000) — waiting can't
    /// fix those, and failing at once puts the real cause at the top of the log.
    /// </summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        PostgresException pg => !(pg.SqlState.StartsWith("28") || pg.SqlState == "3D000"),
        NpgsqlException or System.Net.Sockets.SocketException or TimeoutException => true,
        _ => false
    };

    public Task StartAsync(CancellationToken cancellationToken)   => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken)    => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
