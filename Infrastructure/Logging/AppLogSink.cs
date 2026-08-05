using System.Collections.Concurrent;
using Npgsql;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace SimpleERP.Infrastructure.Logging;

/// <summary>
/// Serilog sink writing Warning-and-above into the <c>AppLogs</c> table, so incidents can
/// be queried in SQL like everything else in this app — and, later, listed on a page.
///
/// Three rules shape the whole design, in priority order:
///
/// 1. <b>Logging must never break the application.</b> Every failure in here is caught and
///    dropped. A logging sink that throws turns a recoverable incident into an outage.
/// 2. <b>Logging must never block a request.</b> <see cref="Emit"/> only enqueues; a
///    background loop does the database work. A slow or hung database must not add its
///    latency to a user posting an invoice.
/// 3. <b>The file sink is authoritative, this one is a convenience.</b> Anything that
///    cannot be written here — including, necessarily, every failure of the database
///    itself — is still in <c>logs/</c>. That is why dropping events here is acceptable
///    rather than something to engineer around.
///
/// Deliberately raw Npgsql rather than EF Core: the sink is called from any thread, at any
/// time, including from background services and from before/after the DI container's
/// scopes exist. Resolving a scoped <c>AppDbContext</c> on that path would be wrong, and
/// enlisting a log write in a business transaction — which could then roll the log entry
/// back along with the failure that caused it — would be worse.
/// </summary>
public sealed class AppLogSink : ILogEventSink, IDisposable
{
    private const int  MaxQueue     = 10_000;   // bounded: a DB outage must not exhaust memory
    private const int  MaxBatch     = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private const string InsertSql = """
        INSERT INTO "AppLogs"
            ("Timestamp", "Level", "Message", "Exception", "Source", "CorrelationId", "RequestPath")
        VALUES (@ts, @level, @msg, @ex, @src, @cid, @path)
        """;

    private readonly string                        _connectionString;
    private readonly ConcurrentQueue<AppLogRow>    _queue = new();
    private readonly CancellationTokenSource       _stopping = new();
    private readonly Task                          _worker;

    private int _dropped;

    public AppLogSink(string connectionString)
    {
        _connectionString = connectionString;
        _worker = Task.Run(FlushLoopAsync);
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null) return;

        try
        {
            // Bounded, and the incoming event is what gets dropped rather than the oldest:
            // when the database is unreachable the earliest events are the ones explaining
            // why, so they are the ones worth keeping.
            if (_queue.Count >= MaxQueue) { Interlocked.Increment(ref _dropped); return; }

            _queue.Enqueue(new AppLogRow(
                Timestamp     : logEvent.Timestamp.UtcDateTime,
                Level         : logEvent.Level.ToString(),
                Message       : logEvent.RenderMessage(),
                Exception     : logEvent.Exception?.ToString(),
                Source        : Scalar(logEvent, "SourceContext",  300),
                CorrelationId : Scalar(logEvent, "CorrelationId",   64),
                RequestPath   : Scalar(logEvent, "RequestPath",    500)));
        }
        catch
        {
            // Never let the act of logging surface an exception to the caller.
        }
    }

    private async Task FlushLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try { await Task.Delay(FlushInterval, _stopping.Token); }
            catch (OperationCanceledException) { break; }

            await FlushAsync();
        }

        await FlushAsync();   // final drain on shutdown, so the last words aren't lost
    }

    private async Task FlushAsync()
    {
        if (_queue.IsEmpty) return;

        var batch = new List<AppLogRow>(MaxBatch);
        while (batch.Count < MaxBatch && _queue.TryDequeue(out var row)) batch.Add(row);
        if (batch.Count == 0) return;

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            // One transaction for the batch: a partial write here would be indistinguishable
            // from lost events when reading the table back later.
            await using var tx = await conn.BeginTransactionAsync();

            foreach (var row in batch)
            {
                await using var cmd = new NpgsqlCommand(InsertSql, conn, tx);
                cmd.Parameters.AddWithValue("ts",    row.Timestamp);
                cmd.Parameters.AddWithValue("level", row.Level);
                cmd.Parameters.AddWithValue("msg",   row.Message);
                cmd.Parameters.AddWithValue("ex",    (object?)row.Exception     ?? DBNull.Value);
                cmd.Parameters.AddWithValue("src",   (object?)row.Source        ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cid",   (object?)row.CorrelationId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("path",  (object?)row.RequestPath   ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            var lost = Interlocked.Exchange(ref _dropped, 0);
            if (lost > 0)
            {
                // Record the gap rather than hide it: a silently short log is worse than a
                // log that admits what it missed.
                await using var note = new NpgsqlCommand(InsertSql, conn);
                note.Parameters.AddWithValue("ts",    DateTime.UtcNow);
                note.Parameters.AddWithValue("level", "Warning");
                note.Parameters.AddWithValue("msg",
                    $"AppLogSink dropped {lost} log entries — the queue filled while the database was unreachable. " +
                    "The full record for that period is in the rolling file under logs/.");
                note.Parameters.AddWithValue("ex",   DBNull.Value);
                note.Parameters.AddWithValue("src",  typeof(AppLogSink).FullName!);
                note.Parameters.AddWithValue("cid",  DBNull.Value);
                note.Parameters.AddWithValue("path", DBNull.Value);
                await note.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            // The database is the thing that's broken. Report through Serilog's own
            // diagnostic channel (which the file sink is wired to), never by throwing.
            Interlocked.Add(ref _dropped, batch.Count);
            Serilog.Debugging.SelfLog.WriteLine("AppLogSink flush failed, {0} events dropped: {1}",
                batch.Count, ex);
        }
    }

    /// <summary>Reads one scalar Serilog property as trimmed text, or null if absent.</summary>
    private static string? Scalar(LogEvent e, string name, int maxLength)
    {
        if (!e.Properties.TryGetValue(name, out var value)) return null;
        var text = value is ScalarValue { Value: not null } s
            ? s.Value.ToString()
            : value.ToString().Trim('"');
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public void Dispose()
    {
        try
        {
            _stopping.Cancel();
            // Bounded wait: shutdown must not hang on an unreachable database.
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* shutdown is not a place to raise new problems */ }
        finally { _stopping.Dispose(); }
    }

    private readonly record struct AppLogRow(
        DateTime Timestamp,
        string   Level,
        string   Message,
        string?  Exception,
        string?  Source,
        string?  CorrelationId,
        string?  RequestPath);
}

public static class AppLogSinkExtensions
{
    /// <summary>
    /// Writes Warning-and-above to the <c>AppLogs</c> table. Pair it with a file sink —
    /// this one cannot, by construction, record the database being unavailable.
    /// </summary>
    public static Serilog.LoggerConfiguration AppLogTable(
        this LoggerSinkConfiguration sinkConfiguration,
        string connectionString,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Warning)
        => sinkConfiguration.Sink(new AppLogSink(connectionString), restrictedToMinimumLevel);
}
