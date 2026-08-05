using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Web.Services;

/// <summary>
/// Trims the <c>AppLogs</c> table to the same window the rolling files keep, once at
/// startup and then daily.
///
/// It matters more here than it looks: unlike the log files, this table is inside the
/// database, so every row is copied into all 30 nightly <c>pg_dump</c> archives. An
/// unbounded diagnostic table would therefore be paid for thirty times over in backup
/// size, and would slowly make the restore drill slower for no diagnostic benefit —
/// nobody debugs an incident from eighteen months ago.
///
/// The rolling files remain the longer-lived record only in the sense that they hold
/// every level; both are capped at the same number of days deliberately, so "how far
/// back can I look" has one answer rather than two.
/// </summary>
public class LogRetentionService : BackgroundService
{
    /// <summary>Kept in step with the file sink's retainedFileCountLimit in Program.cs.</summary>
    private const int RetentionDays = 30;

    private readonly IServiceProvider              _services;
    private readonly ILogger<LogRetentionService>  _logger;

    public LogRetentionService(IServiceProvider services, ILogger<LogRetentionService> logger)
    { _services = services; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PurgeAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            var now  = DateTime.Now;
            var next = now.Date.AddDays(1).AddMinutes(30);   // just after the nightly backup
            try { await Task.Delay(next - now, stoppingToken); }
            catch (TaskCanceledException) { break; }

            if (!stoppingToken.IsCancellationRequested) await PurgeAsync();
        }
    }

    private async Task PurgeAsync()
    {
        try
        {
            // Scoped explicitly: this is a singleton hosted service, so it cannot take a
            // scoped repository through its own constructor.
            using var scope = _services.CreateScope();
            var repo   = scope.ServiceProvider.GetRequiredService<IAppLogRepository>();
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

            var removed = await repo.PurgeOlderThanAsync(cutoff);
            if (removed > 0)
                _logger.LogInformation(
                    "Log retention: removed {RemovedCount} diagnostic entries older than {Cutoff:yyyy-MM-dd}.",
                    removed, cutoff);
        }
        catch (Exception ex)
        {
            // Housekeeping failing must never take the application down, exactly as with
            // the backup service.
            _logger.LogError(ex, "Log retention sweep failed.");
        }
    }
}
