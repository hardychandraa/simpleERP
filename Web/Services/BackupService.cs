using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
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
///
/// Encrypted (HC, 2026-10-22): with a Backup:Passphrase set, every dump is checked with
/// pg_restore --list, encrypted to <c>.dump.enc</c> (<see cref="BackupCrypto"/>), read back,
/// and only then is the plain dump deleted. Without one, backups go on unencrypted and the
/// Backup page says so: a missing setting must never stop the backups themselves.
/// </summary>
public class BackupService : BackgroundService
{
    private const int KeepCount = 30;

    private readonly ILogger<BackupService> _logger;
    private readonly IConfiguration         _config;
    private readonly string                 _backupDir;
    // One pg_dump at a time: the schedule and the button may meet.
    private readonly SemaphoreSlim          _gate = new(1, 1);

    private readonly string?                _passphrase;

    public enum EncryptionState { On, NotConfigured, TooShort }

    /// <summary>Outcome of one backup, for the Settings page.</summary>
    public record BackupResult(bool Success, string? FileName, long Size, string? Error, bool Encrypted = false);
    /// <summary>One file in the backups folder.</summary>
    public record BackupFile(string FileName, long Size, DateTime CreatedLocal, bool Manual, bool Encrypted);

    public string BackupDir => _backupDir;
    public EncryptionState Encryption { get; }

    public BackupService(ILogger<BackupService> logger, IConfiguration config, IWebHostEnvironment env)
    {
        _logger    = logger;
        _config    = config;
        _backupDir = Path.Combine(env.ContentRootPath, "backups");

        var passphrase = config["Backup:Passphrase"];
        Encryption = string.IsNullOrEmpty(passphrase)                       ? EncryptionState.NotConfigured
                   : passphrase.Length < BackupCrypto.MinPassphraseLength ? EncryptionState.TooShort
                   :                                                        EncryptionState.On;
        _passphrase = Encryption == EncryptionState.On ? passphrase : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Encryption == EncryptionState.On) _logger.LogInformation("Backups are encrypted.");
        else _logger.LogWarning("Backups are NOT encrypted: Backup:Passphrase is {State} (at least {Min} characters needed).",
                                Encryption == EncryptionState.TooShort ? "too short" : "not set", BackupCrypto.MinPassphraseLength);

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
                FileName               = ResolvePgToolPath("pg_dump"),
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

            // A dump pg_restore can't read is not a backup, and must not be encrypted into one.
            var listError = await ListDumpAsync(backupFile);
            if (listError != null)
            {
                _logger.LogError("Backup failed: pg_restore cannot read the new dump: {err}", listError);
                File.Delete(backupFile);
                return new(false, null, 0, $"pg_restore --list: {listError}");
            }

            var encrypted = false;
            if (_passphrase != null)
            {
                try
                {
                    backupFile = await Task.Run(() => EncryptAndVerify(backupFile, _passphrase));
                    encrypted  = true;
                }
                catch (Exception ex)
                {
                    // Better a plain backup than none: keep the dump, say so loudly.
                    _logger.LogError(ex, "Backup encryption failed; the plain dump {file} is kept", backupFile);
                }
            }

            if (manual) _logger.LogInformation("Backup created on demand by {User}: {file}", requestedBy, backupFile);
            else        _logger.LogInformation("Backup created: {file}", backupFile);
            PurgeOldBackups();
            return new(true, Path.GetFileName(backupFile), new FileInfo(backupFile).Length, null, encrypted);
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
        return new DirectoryInfo(_backupDir).EnumerateFiles()
            .Where(f => IsBackupName(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            // Only simpleerp_yyyyMMdd_HHmmss.dump(.enc) comes from the schedule; anything else
            // (the button's _manual files, or a pre_*.dump taken by hand) was made on purpose.
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTime, !IsScheduled(f.Name), IsEncrypted(f.Name)))
            .ToList();
    }

    /// <summary>
    /// Full path of a backup to download, or null. Only a bare file name that exists in the
    /// backups folder is accepted, so the name can't be used to reach anything else on disk.
    /// </summary>
    public string? ResolveBackupPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)
            || !IsBackupName(fileName)) return null;
        var path = Path.Combine(_backupDir, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Encrypts the dump next to it, reads the encrypted file back and compares it with the dump,
    /// and only then deletes the dump. Returns the encrypted file; throws (dump kept) otherwise.
    /// </summary>
    private static string EncryptAndVerify(string dumpFile, string passphrase)
    {
        var encFile = dumpFile + BackupCrypto.Extension;
        BackupCrypto.EncryptFile(dumpFile, encFile, passphrase);
        byte[] expected;
        using (var dump = File.OpenRead(dumpFile)) expected = SHA256.HashData(dump);
        if (!BackupCrypto.DecryptedSha256(encFile, passphrase).AsSpan().SequenceEqual(expected))
        {
            File.Delete(encFile);
            throw new CryptographicException("The encrypted backup did not read back identical to the dump.");
        }
        File.Delete(dumpFile);
        return encFile;
    }

    /// <summary>
    /// pg_restore --list reads the dump's whole table of contents: null if it is readable, else
    /// the error. pg_restore missing is only a warning: a tooling gap, not a bad dump.
    /// </summary>
    private async Task<string?> ListDumpAsync(string dumpFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = ResolvePgToolPath("pg_restore"),
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        psi.ArgumentList.Add("--list");
        psi.ArgumentList.Add(dumpFile);

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.LogWarning("Backup not checked: could not start pg_restore ({err}).", ex.Message);
            return null;
        }
        if (proc is null) return null;
        using (proc)
        {
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdout, stderr);
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? null : $"exit {proc.ExitCode}: {stderr.Result.Trim()}";
        }
    }

    /// <summary>
    /// pg_dump/pg_restore are often not on PATH on Windows. Allow an explicit override via
    /// Backup:PgDumpPath (pg_restore is taken from the same folder), otherwise probe the
    /// standard install locations, newest first.
    /// </summary>
    private string ResolvePgToolPath(string tool)
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var configured = _config["Backup:PgDumpPath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return tool == "pg_dump" ? configured : Path.Combine(Path.GetDirectoryName(configured) ?? "", exe);

        var candidates = Directory.Exists(@"C:\Program Files\PostgreSQL")
            ? Directory.GetDirectories(@"C:\Program Files\PostgreSQL")
                       .OrderByDescending(d => d)
                       .Select(d => Path.Combine(d, "bin", exe))
                       .Where(File.Exists)
            : Enumerable.Empty<string>();

        return candidates.FirstOrDefault() ?? tool;   // fall back to PATH
    }

    private static bool IsEncrypted(string name) =>
        name.EndsWith(".dump" + BackupCrypto.Extension, StringComparison.OrdinalIgnoreCase);
    private static bool IsBackupName(string name) =>
        IsEncrypted(name) || name.EndsWith(".dump", StringComparison.OrdinalIgnoreCase);
    /// <summary>simpleerp_yyyyMMdd_HHmmss.dump(.enc): made by the schedule, so pruned.</summary>
    private static bool IsScheduled(string name)
    {
        var plain = IsEncrypted(name) ? name[..^BackupCrypto.Extension.Length] : name;
        return plain.StartsWith("simpleerp_", StringComparison.OrdinalIgnoreCase)
            && !plain.EndsWith("_manual.dump", StringComparison.OrdinalIgnoreCase);
    }

    private void PurgeOldBackups()
    {
        // Filenames are timestamped yyyyMMdd_HHmmss, so lexical order == chronological order.
        // Manual backups are kept: they were taken on purpose.
        var stale = Directory.EnumerateFiles(_backupDir, "simpleerp_*")
                             .Select(Path.GetFileName)
                             .Where(f => IsBackupName(f!) && IsScheduled(f!))
                             .Select(f => Path.Combine(_backupDir, f!))
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
