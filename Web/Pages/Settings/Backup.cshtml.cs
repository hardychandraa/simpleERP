using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Settings;

/// <summary>
/// Backups on demand (HC, 2026-10-07): "Backup now" before something risky, and a download
/// link so a copy can be taken off the server by hand. Admin-only via the /Settings folder
/// convention in Program.cs. There is deliberately no restore here: restoring overwrites
/// everything, so it stays a technician step (deployment runbook).
/// </summary>
public class BackupModel : PageModel
{
    private readonly BackupService _backup;
    public BackupModel(BackupService backup) => _backup = backup;

    public List<BackupService.BackupFile> Files { get; set; } = new();
    public string  BackupDir => _backup.BackupDir;
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public void OnGet(string? msg, bool err = false)
    {
        ViewData["Title"] = "Backup";
        Msg = msg; IsErr = err;
        Files = _backup.ListBackups();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var r = await _backup.RunBackupAsync(manual: true, this.CurrentUserName());
        return RedirectToPage(new {
            msg = r.Success ? $"✓ {r.FileName} ({r.Size / 1024.0:N0} KB)" : $"✗ {r.Error}",
            err = !r.Success });
    }

    public IActionResult OnGetDownload(string file)
    {
        var path = _backup.ResolveBackupPath(file);
        if (path == null) return NotFound();
        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }
}
