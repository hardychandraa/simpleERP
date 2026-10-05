using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
namespace SimpleERP.Web.Pages.Inventory;
public class StockInModel:PageModel{
    private readonly IInventoryService _inv;private readonly IProductService _prod;
    private readonly IStringLocalizer<SharedResource> _loc;
    public StockInModel(IInventoryService i,IProductService p, IStringLocalizer<SharedResource> loc){_inv=i;_prod=p; _loc = loc; }
    [BindProperty]public StockInDto Input{get;set;}=new();
    public List<SelectListItem> Products{get;set;}=new();
    public string? Error{get;set;} public string? Success{get;set;}
    public async Task OnGetAsync(){ViewData["Title"]="Stock In";await Load();}
    public async Task<IActionResult> OnPostAsync(){
        ViewData["Title"]="Stock In";await Load();
        if(!ModelState.IsValid)return Page();
        var r=await _inv.StockInAsync(Input);
        if(!r.Success){Error=r.Error;return Page();}
        Success=_loc["Stock received successfully."];Input=new StockInDto();return Page();
    }
    private async Task Load(){
        var list=await _prod.GetAllActiveAsync();
        Products=list.Select(p=>new SelectListItem($"{p.Name} ({p.SKU}) — {_loc["Stk:"]} {p.CurrentStock:N0}",p.Id.ToString())).ToList();
    }
}
