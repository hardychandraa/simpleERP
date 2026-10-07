using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Npgsql;

namespace SimpleERP.Web.Services;

/// <summary>
/// Runs once at startup and then daily at noon: the office server may only be switched on
/// during working hours, so a midnight run could be missed for days (HC, 2026-10-06).
/// Dumps the PostgreSQL database to a timestamped file in /backups, keeping the last 30.
///
/// Replaces the previous SQLite File.Copy approach — a server-hosted database cannot be
/// backed up by copying a file off disk, so this shells out to pg_dump instead.
/// Uses custom format (-Fc): compressed, and restorable selectively via pg_restore.
///
/// Also run on demand from Settings → Backup (HC, 2026-10-07). Those files end in
/// <c>_manual.dump</c> and are never pruned: they are deliberate safe points, taken before
/// something risky, and HC copies them off the server by hand.
/// </summary>
public class BackupService : BackgroundService
{
    private const int KeepCount = 30;

    private readonly ILogger<BackupService> _logger;
    private readonly IConfiguration         _config;
    private readonly string                 _backupDir;
    // One pg_dump at a time: the schedule and the button may meet.
    private readonly SemaphoreSlim          _gate = new(1, 1);

    /// <summary>Outcome of one backup, for the Settings page.</summary>
    public record BackupResult(bool Success, string? FileName, long Size, string? Error);
    /// <summary>One file in the backups folder.</summary>
    public record BackupFile(string FileName, long Size, DateTime CreatedLocal, bool Manual);

    public string BackupDir => _backupDir;

    public BackupService(ILogger<BackupService> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _logger    = logger;
        _config    = config;
        _backupDir = Path.Combine(env.ContentRootPath, "backups");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup
        await RunBackupAsync(manual: false);

        // Then every day at noon, local time
        while (!stoppingToken.IsCancellationRequested)
        {
            var now  = DateTime.Now;
            var next = now.Date.AddHours(12);
            if (next <= now) next = next.AddDays(1);
            var wait = next - now;
            try { await Task.Delay(wait, stoppingToken); }
            catch (TaskCanceledException) { break; }

            if (!stoppingToken.IsCancellationRequested)
                await RunBackupAsync(manual: false);
        }
    }

    /// <summary>Dumps the database now. Never throws: a failure comes back as the result.</summary>
    public async Task<BackupResult> RunBackupAsync(bool manual, string? requestedBy = null)
    {
        await _gate.WaitAsync();
        try
        {
            var connectionString = _config.GetConnectionString("SimpleERP");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("Backup skipped: no SimpleERP connection string configured.");
                return new(false, null, 0, "No database connection string is configured.");
            }

            var csb = new NpgsqlConnectionStringBuilder(connectionString);
            Directory.CreateDirectory(_backupDir);

            var stamp      = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFile = Path.Combine(_backupDir, $"simpleerp_{stamp}{(manual ? "_manual" : "")}.dump");

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
                return new(false, null, 0, "Could not start pg_dump.");
            }

            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                _logger.LogError("Backup failed (pg_dump exit {code}): {err}", proc.ExitCode, stderr.Trim());
                // Don't leave a truncated/empty dump lying around looking like a good backup.
                if (File.Exists(backupFile)) File.Delete(backupFile);
                return new(false, null, 0, $"pg_dump exit {proc.ExitCode}: {stderr.Trim()}");
            }

            if (manual) _logger.LogInformation("Backup created on demand by {User}: {file}", requestedBy, backupFile);
            else        _logger.LogInformation("Backup created: {file}", backupFile);
            PurgeOldBackups();
            return new(true, Path.GetFileName(backupFile), new FileInfo(backupFile).Length, null);
        }
        catch (Exception ex)
        {
            // A backup failure must never take the application down.
            _logger.LogError(ex, "Backup failed");
            return new(false, null, 0, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Every backup in the folder, newest first.</summary>
    public List<BackupFile> ListBackups()
    {
        if (!Directory.Exists(_backupDir)) return new();
        return new DirectoryInfo(_backupDir).GetFiles("*.dump")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            // Only simpleerp_yyyyMMdd_HHmmss.dump comes from the schedule; anything else (the
            // button's _manual files, or a pre_*.dump taken by hand) was made on purpose.
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTime,
                                        !f.Name.StartsWith("simpleerp_", StringComparison.OrdinalIgnoreCase)
                                        || f.Name.EndsWith("_manual.dump", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Full path of a backup to download, or null. Only a bare file name that exists in the
    /// backups folder is accepted, so the name can't be used to reach anything else on disk.
    /// </summary>
    public string? ResolveBackupPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)
            || !fileName.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(_backupDir, fileName);
        return File.Exists(path) ? path : null;
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
        // Manual backups are kept: they were taken on purpose.
        var stale = Directory.GetFiles(_backupDir, "simpleerp_*.dump")
                             .Where(f => !f.EndsWith("_manual.dump", StringComparison.OrdinalIgnoreCase))
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
