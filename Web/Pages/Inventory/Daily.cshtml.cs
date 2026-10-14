using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Inventory;

/// <summary>
/// Cek Stok Harian (HC, 2026-10-14): opening, in, out and closing per product for one day, the
/// day's counts and anything backdated past them, and a printable sheet with an empty Fisik
/// column. Quantities only, so Staff and Admin see the same page.
/// </summary>
public class DailyModel : PageModel
{
    private readonly IStockCountService _counts;
    public DailyModel(IStockCountService counts) => _counts = counts;

    public DailyStockSheetDto Sheet { get; set; } = new();
    public List<DailyStockRowDto> Rows { get; set; } = new();
    public DateTime Day { get; set; }
    /// <summary>"moved" (default) or "all".</summary>
    public string Show { get; set; } = "moved";
    public string? Category { get; set; }
    public List<string> Categories { get; set; } = new();
    public bool IsToday => Day == DateTime.Today;
    public string? Error { get; set; }

    public async Task OnGetAsync(DateTime? date, string? show, string? category)
    {
        ViewData["Title"] = "Daily Stock Check";
        Day = (date ?? DateTime.Today).Date;
        if (Day > DateTime.Today) Day = DateTime.Today;
        Show = show == "all" ? "all" : "moved";
        Category = string.IsNullOrWhiteSpace(category) ? null : category;
        Sheet = await _counts.GetDailySheetAsync(Day);
        Categories = Sheet.Rows.Select(r => r.Category).Where(c => !string.IsNullOrWhiteSpace(c))
                               .Select(c => c!).Distinct().OrderBy(c => c).ToList();
        Rows = Sheet.Rows
            .Where(r => Show == "all" ? (r.IsActive || r.Closing != 0 || r.Moved) : r.Moved || r.CountId != null)
            .Where(r => Category == null || r.Category == Category)
            .ToList();
    }

    /// <summary>Starts a count of today's movers (the default daily routine) and opens it.</summary>
    public async Task<IActionResult> OnPostStartAsync()
    {
        var r = await _counts.CreateAsync(StockCountScope.MovedToday, null, this.CurrentUserName());
        if (!r.Success)
        {
            await OnGetAsync(null, null, null);
            Error = r.Error;
            return Page();
        }
        return RedirectToPage("/StockCounts/Count", new { id = r.Data });
    }
}
