using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Reads the diagnostic log for the viewer page. Mirrors <see cref="AuditService"/> —
/// a thin mapping layer over the repository, with no write path at all: entries arrive
/// through the Serilog sink, and nothing in the application may add, edit or delete one.
/// </summary>
public class AppLogService : IAppLogService
{
    private readonly IAppLogRepository _repo;
    public AppLogService(IAppLogRepository repo) => _repo = repo;

    public async Task<List<AppLogDto>> GetAsync(string? minLevel = null, DateTime? from = null,
        DateTime? to = null, string? search = null, int count = 200)
        => (await _repo.GetAsync(minLevel, from, to, search, count)).Select(Map).ToList();

    public async Task<AppLogSummaryDto> GetSummaryAsync(DateTime? from = null, DateTime? to = null)
    {
        var counts = await _repo.GetLevelCountsAsync(from, to);
        int For(string level) => counts.FirstOrDefault(c =>
            string.Equals(c.Level, level, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

        return new AppLogSummaryDto {
            Warnings = For("Warning"),
            Errors   = For("Error"),
            Fatals   = For("Fatal")
        };
    }

    private static AppLogDto Map(AppLog l) => new() {
        Id            = l.Id,
        Timestamp     = l.Timestamp,
        Level         = l.Level,
        Message       = l.Message,
        Exception     = l.Exception,
        Source        = ShortSource(l.Source),
        CorrelationId = l.CorrelationId,
        RequestPath   = l.RequestPath
    };

    /// <summary>
    /// "SimpleERP.Application.Services.SaleService" → "SaleService". The namespace is
    /// the same for almost every entry, so showing it in full spends the column's width
    /// on the one part that never varies.
    /// </summary>
    private static string? ShortSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var lastDot = source.LastIndexOf('.');
        return lastDot >= 0 && lastDot < source.Length - 1 ? source[(lastDot + 1)..] : source;
    }
}
