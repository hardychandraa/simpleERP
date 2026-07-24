using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Expenses;

public class EditModel : PageModel
{
    private readonly IExpenseService _svc;
    public EditModel(IExpenseService svc) => _svc = svc;

    [BindProperty] public UpdateExpenseDto Input { get; set; } = new();
    public List<ExpenseCategoryDto> Categories { get; set; } = new();
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        ViewData["Title"] = "Edit Expense";
        var e = await _svc.GetByIdAsync(id);
        if (e == null) return Redirect("/Expenses?err=true&msg=Expense+not+found.");

        Input = new UpdateExpenseDto {
            Id = e.Id, ExpenseDate = e.ExpenseDate, CategoryId = e.CategoryId,
            Amount = e.Amount, Description = e.Description, ReferenceNo = e.ReferenceNo };
        Categories = await _svc.GetCategoriesAsync(activeOnly: true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Edit Expense";
        var result = await _svc.UpdateAsync(Input, User.Identity?.Name ?? "staff");
        if (!result.Success)
        {
            Error = result.Error;
            Categories = await _svc.GetCategoriesAsync(activeOnly: true);
            return Page();
        }
        return Redirect("/Expenses?msg=Expense+updated.");
    }
}
