using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SimpleERP.Web.Services;

namespace SimpleERP.Web.Pages.Expenses;

public class EditModel : PageModel
{
    private readonly IExpenseService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public EditModel(IExpenseService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    [BindProperty] public UpdateExpenseDto Input { get; set; } = new();
    public List<ExpenseCategoryDto> Categories { get; set; } = new();
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ViewData["Title"] = "Edit Expense";
        var e = await _svc.GetByIdAsync(id);
        if (e == null) return Redirect($"/Expenses?err=true&msg={Uri.EscapeDataString(_loc["Expense not found."].Value)}");

        Input = new UpdateExpenseDto {
            Id = e.Id, ExpenseDate = e.ExpenseDate, CategoryId = e.CategoryId,
            Amount = e.Amount, Description = e.Description, ReferenceNo = e.ReferenceNo };
        Categories = await _svc.GetCategoriesAsync(activeOnly: true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Edit Expense";
        var result = await _svc.UpdateAsync(Input, this.CurrentUserName());
        if (!result.Success)
        {
            Error = result.Error;
            Categories = await _svc.GetCategoriesAsync(activeOnly: true);
            return Page();
        }
        return Redirect($"/Expenses?msg={Uri.EscapeDataString(_loc["Expense updated."].Value)}");
    }
}
