using SimpleERP.Web.Services;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Customers;
[HandlesBindingErrors]
public class EditModel:PageModel{
    private readonly ICustomerService _svc;    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ISalesPersonService _people; private readonly IPaymentTermService _terms;
public EditModel(ICustomerService s, ISalesPersonService people, IPaymentTermService terms, IStringLocalizer<SharedResource> loc)
    { _svc=s; _people=people; _terms=terms; _loc = loc; }
    [BindProperty]public UpdateCustomerDto Input{get;set;}=new();
    public string? Error{get;set;}
    public SaleDefaultsVm Defaults{get;set;}=null!;
    public async Task<IActionResult> OnGetAsync(Guid id){
        ViewData["Title"]="Edit Customer";
        var c=await _svc.GetByIdAsync(id);if(c==null)return RedirectToPage("/Customers/Index");
        Input=new UpdateCustomerDto{Id=c.Id,Name=c.Name,Phone=c.Phone,Address=c.Address,IsActive=c.IsActive,
            TaxId=c.TaxId,NationalId=c.NationalId,
            SalesPersonId=c.SalesPersonId,PaymentTermId=c.PaymentTermId,DefaultDiscountPercent=c.DefaultDiscountPercent};
        await LoadAsync();
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(Guid id){
        ViewData["Title"]="Edit Customer";
        // The customer is the one in the URL, never an id posted in the form (security review R6).
        Input.Id=id;
        if(!ModelState.IsValid){Error=_loc["Please fill in every required field with a valid value."];await LoadAsync();return Page();}
        var r=await _svc.UpdateAsync(Input);
        if(!r.Success){Error=r.Error;await LoadAsync();return Page();}
        return RedirectToPage("/Customers/Index",new{msg=_loc["Customer updated."].Value});
    }
    private async Task LoadAsync(){
        var (people,terms)=await SaleDefaultsVm.LoadOptionsAsync(_people,_terms);
        Defaults=new(people,terms,Input.SalesPersonId,Input.PaymentTermId,Input.DefaultDiscountPercent);
    }
}
