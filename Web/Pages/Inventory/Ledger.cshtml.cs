using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Inventory;
public class LedgerModel:PageModel{
    private readonly IInventoryService _svc;public LedgerModel(IInventoryService s)=>_svc=s;
    public List<InventoryLedgerDto> Entries{get;set;}=new();
    public DateTime? From{get;set;} public DateTime? To{get;set;}
    public async Task OnGetAsync(DateTime? from,DateTime? to){
        ViewData["Title"]="Ledger";From=from;To=to;
        Entries=await _svc.GetLedgerAsync(from?.ToUniversalTime(),to?.ToUniversalTime().AddDays(1));
    }
}
