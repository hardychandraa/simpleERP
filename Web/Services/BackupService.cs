using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Npgsql;

namespace SimpleERP.Web.Services;

/// <summary>
/// Runs once at startup and then daily at midnight.
/// Dumps the PostgreSQL database to a timestamped file in /backups, keeping the last 30.
///
/// Replaces the previous SQLite File.Copy approach — a server-hosted database cannot be
/// backed up by copying a file off disk, so this shells out to pg_dump instead.
/// Uses custom format (-Fc): compressed, and restorable selectively via pg_restore.
/// </summary>
public class BackupService : BackgroundService
{
    private const int KeepCount = 30;

    private readonly ILogger<BackupService> _logger;
    private readonly IConfiguration         _config;
    private readonly string                 _backupDir;

    public BackupService(ILogger<BackupService> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _logger    = logger;
        _config    = config;
        _backupDir = Path.Combine(env.ContentRootPath, "backups");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup
        await RunBackupAsync();

        // Then every day at midnight
        while (!stoppingToken.IsCancellationRequested)
        {
            var now  = DateTime.Now;
            var next = now.Date.AddDays(1);   // midnight tonight
            var wait = next - now;
            try { await Task.Delay(wait, stoppingToken); }
            catch (TaskCanceledException) { break; }

            if (!stoppingToken.IsCancellationRequested)
                await RunBackupAsync();
        }
    }

    private async Task RunBackupAsync()
    {
        try
        {
            var connectionString = _config.GetConnectionString("SimpleERP");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("Backup skipped: no SimpleERP connection string configured.");
                return;
            }

            var csb = new NpgsqlConnectionStringBuilder(connectionString);
            Directory.CreateDirectory(_backupDir);

            var stamp      = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFile = Path.Combine(_backupDir, $"simpleerp_{stamp}.dump");

            var psi = new ProcessStartInfo
            {
                FileName               = ResolvePgDumpPath(),
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            psi.ArgumentList.Add($"--host={csb.Host}");
            psi.ArgumentList.Add($"--port={csb.Port}");
            psi.ArgumentList.Add($"--username={csb.Username}");
            psi.ArgumentList.Add($"--dbname={csb.Database}");
            psi.ArgumentList.Add("--format=custom");
            psi.ArgumentList.Add($"--file={backupFile}");
            psi.ArgumentList.Add("--no-password");   // never block on an interactive prompt

            // pg_dump reads the password from the environment rather than the command line,
            // so it never appears in the process list.
            if (!string.IsNullOrEmpty(csb.Password))
                psi.Environment["PGPASSWORD"] = csb.Password;

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                _logger.LogError("Backup failed: could not start pg_dump.");
                return;
            }

            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                _logger.LogError("Backup failed (pg_dump exit {code}): {err}", proc.ExitCode, stderr.Trim());
                // Don't leave a truncated/empty dump lying around looking like a good backup.
                if (File.Exists(backupFile)) File.Delete(backupFile);
                return;
            }

            _logger.LogInformation("Backup created: {file}", backupFile);
            PurgeOldBackups();
        }
        catch (Exception ex)
        {
            // A backup failure must never take the application down.
            _logger.LogError(ex, "Backup failed");
        }
    }

    /// <summary>
    /// pg_dump is often not on PATH on Windows. Allow an explicit override via
    /// Backup:PgDumpPath, otherwise probe the standard install locations, newest first.
    /// </summary>
    private string ResolvePgDumpPath()
    {
        var configured = _config["Backup:PgDumpPath"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var candidates = Directory.Exists(@"C:\Program Files\PostgreSQL")
            ? Directory.GetDirectories(@"C:\Program Files\PostgreSQL")
                       .OrderByDescending(d => d)
                       .Select(d => Path.Combine(d, "bin", "pg_dump.exe"))
                       .Where(File.Exists)
            : Enumerable.Empty<string>();

        return candidates.FirstOrDefault() ?? "pg_dump";   // fall back to PATH
    }

    private void PurgeOldBackups()
    {
        // Filenames are timestamped yyyyMMdd_HHmmss, so lexical order == chronological order.
        var stale = Directory.GetFiles(_backupDir, "simpleerp_*.dump")
                             .OrderByDescending(f => f)
                             .Skip(KeepCount)
                             .ToList();
        foreach (var old in stale)
        {
            File.Delete(old);
            _logger.LogInformation("Old backup removed: {file}", old);
        }
    }
}
