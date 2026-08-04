using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Customers;
public class CreateModel:PageModel{
    private readonly ICustomerService _svc;    private readonly IStringLocalizer<SharedResource> _loc;
public CreateModel(ICustomerService s, IStringLocalizer<SharedResource> loc) { _svc=s; _loc = loc; }
    [BindProperty]public CreateCustomerDto Input{get;set;}=new();
    public string? Error{get;set;}
    public void OnGet(){ViewData["Title"]="Add Customer";}
    public async Task<IActionResult> OnPostAsync(){
        ViewData["Title"]="Add Customer";
        if(!ModelState.IsValid)return Page();
        var r=await _svc.CreateAsync(Input);
        if(!r.Success){Error=r.Error;return Page();}
        return RedirectToPage("/Customers/Index",new{msg=_loc["Customer created."].Value});
    }
}
