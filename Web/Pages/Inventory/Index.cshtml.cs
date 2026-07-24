using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Inventory;
public class IndexModel:PageModel{
    private readonly IInventoryService _svc;public IndexModel(IInventoryService s)=>_svc=s;
    public List<StockLevelDto> Levels{get;set;}=new();
    public decimal TotalValue{get;set;}
    public async Task OnGetAsync(){ViewData["Title"]="Stock Levels";Levels=(await _svc.GetAllStockLevelsAsync()).OrderBy(s=>s.ProductName).ToList();TotalValue=Levels.Sum(s=>s.StockValue);}
}
