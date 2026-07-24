using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Customers;
public class EditModel:PageModel{
    private readonly ICustomerService _svc;public EditModel(ICustomerService s)=>_svc=s;
    [BindProperty]public UpdateCustomerDto Input{get;set;}=new();
    public string? Error{get;set;}
    public async Task<IActionResult> OnGetAsync(Guid id){
        ViewData["Title"]="Edit Customer";
        var c=await _svc.GetByIdAsync(id);if(c==null)return RedirectToPage("/Customers/Index");
        Input=new UpdateCustomerDto{Id=c.Id,Name=c.Name,Phone=c.Phone,Address=c.Address,IsActive=c.IsActive};
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(){
        ViewData["Title"]="Edit Customer";if(!ModelState.IsValid)return Page();
        var r=await _svc.UpdateAsync(Input);
        if(!r.Success){Error=r.Error;return Page();}
        return RedirectToPage("/Customers/Index",new{msg="Customer updated."});
    }
}
