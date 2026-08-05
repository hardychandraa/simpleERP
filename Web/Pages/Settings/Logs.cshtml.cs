using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.Settings;

/// <summary>
/// Diagnostic log viewer, reading the Warning-and-above rows the Serilog sink writes to
/// <c>AppLogs</c>. The rolling files under <c>logs/</c> stay the complete record — they
/// carry every level, and they survive the database being down, which is exactly when
/// this page cannot load.
///
/// ⚠ There is no authentication anywhere in this application yet (Tier 3 readiness item),
/// so this page — including stack traces, internal paths and business figures — is
/// readable by anyone who can reach the server on the network. Built now at HC's explicit
/// request, with that understood; it should be restricted the moment logins exist.
/// </summary>
public class LogsModel : PageModel
{
    private const int MaxRows = 200;

    private readonly IAppLogService _svc;
    public LogsModel(IAppLogService svc) => _svc = svc;

    public List<AppLogDto>  Entries { get; set; } = new();
    public AppLogSummaryDto Summary { get; set; } = new();

    public string?   Level  { get; set; }
    public string?   Search { get; set; }
    public DateTime? From   { get; set; }
    public DateTime? To     { get; set; }

    public async Task OnGetAsync(string? level, string? search, DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Diagnostic Log";

        Level  = string.IsNullOrWhiteSpace(level) ? null : level;
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        From   = from;
        To     = to;

        // The filters are inclusive of the whole end day: a user picking "to 4 August"
        // means through the end of the 4th, not up to 00:00 on it. The repository
        // compares with a strict upper bound, so the boundary is added here.
        //
        // Local-to-UTC on the way in, because every stored timestamp is UTC while the
        // date pickers — and the times rendered below — are local. Without this the
        // filter would silently miss up to 7 hours at each edge on this box.
        var fromUtc = From?.Date.ToUniversalTime();
        var toUtc   = To?.Date.AddDays(1).ToUniversalTime();

        Entries = await _svc.GetAsync(Level, fromUtc, toUtc, Search, MaxRows);
        Summary = await _svc.GetSummaryAsync(fromUtc, toUtc);
    }

    public bool AtRowLimit => Entries.Count >= MaxRows;
}
