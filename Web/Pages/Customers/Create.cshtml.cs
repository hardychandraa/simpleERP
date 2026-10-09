using SimpleERP.Web.Services;
using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;using Microsoft.AspNetCore.Mvc.RazorPages;
namespace SimpleERP.Web.Pages.Customers;
[HandlesBindingErrors]
public class CreateModel:PageModel{
    private readonly ICustomerService _svc;    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ISalesPersonService _people; private readonly IPaymentTermService _terms;
public CreateModel(ICustomerService s, ISalesPersonService people, IPaymentTermService terms, IStringLocalizer<SharedResource> loc)
    { _svc=s; _people=people; _terms=terms; _loc = loc; }
    [BindProperty]public CreateCustomerDto Input{get;set;}=new();
    public string? Error{get;set;}
    public SaleDefaultsVm Defaults{get;set;}=null!;
    public async Task OnGetAsync(){ViewData["Title"]="Add Customer";await LoadAsync();}
    public async Task<IActionResult> OnPostAsync(){
        ViewData["Title"]="Add Customer";
        if(!ModelState.IsValid){Error=_loc["Please fill in every required field with a valid value."];await LoadAsync();return Page();}
        var r=await _svc.CreateAsync(Input);
        if(!r.Success){Error=r.Error;await LoadAsync();return Page();}
        return RedirectToPage("/Customers/Index",new{msg=_loc["Customer created."].Value});
    }
    private async Task LoadAsync(){
        var (people,terms)=await SaleDefaultsVm.LoadOptionsAsync(_people,_terms);
        Defaults=new(people,terms,Input.SalesPersonId,Input.PaymentTermId,Input.DefaultDiscountPercent);
    }
}
