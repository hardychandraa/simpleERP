using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Audit;
public class IndexModel : PageModel {
    private readonly IAuditService _svc;
    public IndexModel(IAuditService svc) => _svc = svc;
    public List<AuditLogDto> Logs { get; set; } = new();
    public async Task OnGetAsync() {
        ViewData["Title"] = "Audit Log";
        Logs = await _svc.GetRecentAsync(200);
    }
}
