using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Rebates;

/// <summary>
/// The rebate claim centre: what's outstanding, and the three ways to settle it
/// (cash lump per supplier, in-kind goods receipt, lucky-draw settled amount), plus
/// the settlement history.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IRebateService      _svc;
    private readonly IAppSettingsService _settings;
    public IndexModel(IRebateService svc, IAppSettingsService settings)
    { _svc = svc; _settings = settings; }

    public List<RebateOutstandingDto>  Outstanding    { get; set; } = new();
    public List<RebateAccrualDto>      InKindAccruals { get; set; } = new();
    public List<RebateAccrualDto>      LuckyAccruals  { get; set; } = new();
    public List<RebateRealizationDto>  History        { get; set; } = new();
    /// <summary>Withholding rate as a percent, for the net-of-withholding preview.</summary>
    public decimal WithholdingPercent { get; set; }

    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Rebates";
        Msg = msg; IsErr = err;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Outstanding = await _svc.GetOutstandingSummaryAsync();
        var open = await _svc.GetAccrualsAsync(outstandingOnly: true);
        InKindAccruals = open.Where(a => a.RewardType == nameof(RebateRewardType.InKindGoods)).ToList();
        LuckyAccruals  = open.Where(a => a.RewardType == nameof(RebateRewardType.LuckyDraw)).ToList();
        History = await _svc.GetRealizationsAsync();
        WithholdingPercent = (await _settings.GetAsync()).RebateWithholdingPercent;
    }

    private string User_ => User.Identity?.Name ?? "staff";

    public async Task<IActionResult> OnPostRealizeCashAsync(RealizeCashDto input)
    {
        var r = await _svc.RealizeCashAsync(input, User_);
        return Redirect(r.Success
            ? "/Rebates?msg=Cash+rebate+settled."
            : $"/Rebates?err=true&msg={Uri.EscapeDataString(r.Error!)}");
    }

    public async Task<IActionResult> OnPostRealizeInKindAsync(RealizeInKindDto input)
    {
        var r = await _svc.RealizeInKindAsync(input, User_);
        return Redirect(r.Success
            ? "/Rebates?msg=In-kind+goods+received+into+stock."
            : $"/Rebates?err=true&msg={Uri.EscapeDataString(r.Error!)}");
    }

    public async Task<IActionResult> OnPostRealizeLuckyDrawAsync(RealizeLuckyDrawDto input)
    {
        var r = await _svc.RealizeLuckyDrawAsync(input, User_);
        return Redirect(r.Success
            ? "/Rebates?msg=Lucky+draw+settled."
            : $"/Rebates?err=true&msg={Uri.EscapeDataString(r.Error!)}");
    }
}
