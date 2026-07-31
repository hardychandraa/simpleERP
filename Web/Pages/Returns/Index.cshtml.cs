using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Returns;

/// <summary>
/// Both directions of returns in one place. A return always starts from the document it
/// reverses — there's no "new return" button here, because a return with no invoice or
/// purchase behind it has no prices, no costs and no returnable quantities to check
/// against. Staff open the invoice or the purchase and return from there.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IReturnService _returns;
    public IndexModel(IReturnService returns) => _returns = returns;

    public List<ReturnListDto> CustomerReturns { get; set; } = new();
    public List<ReturnListDto> SupplierReturns { get; set; } = new();

    public DateTime? From   { get; set; }
    public DateTime? To     { get; set; }
    public string?   Search { get; set; }
    public string?   Msg    { get; set; }
    public bool      IsErr  { get; set; }

    /// <summary>Active returns only — cancelled ones moved no money and no goods.</summary>
    public decimal CustomerCreditTotal => CustomerReturns
        .Where(r => r.Status == "Active").Sum(r => r.GrandTotal);
    public decimal SupplierDebitTotal => SupplierReturns
        .Where(r => r.Status == "Active").Sum(r => r.GrandTotal);

    public async Task OnGetAsync(DateTime? from, DateTime? to, string? search,
                                string? msg, bool err = false)
    {
        ViewData["Title"] = "Returns";
        From = from; To = to; Search = search;
        Msg  = msg;  IsErr = err;

        CustomerReturns = await _returns.GetCustomerReturnsAsync(from, to, search);
        SupplierReturns = await _returns.GetSupplierReturnsAsync(from, to, search);
    }
}
