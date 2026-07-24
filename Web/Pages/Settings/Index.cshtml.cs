using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Settings;
public class IndexModel : PageModel {
    private readonly IAppSettingsService _svc;
    public IndexModel(IAppSettingsService svc) => _svc = svc;
    [BindProperty] public AppSettingsDto Input { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }
    public async Task OnGetAsync(string? msg, bool err=false) {
        ViewData["Title"] = "Settings";
        Input = await _svc.GetAsync();
        Msg=msg; IsErr=err;
    }
    public async Task<IActionResult> OnPostAsync() {
        ViewData["Title"] = "Settings";
        if (!ModelState.IsValid) return Page();
        var r = await _svc.SaveAsync(Input);
        return RedirectToPage(new { msg=r.Success?"Settings saved.":r.Error, err=!r.Success });
    }
}
