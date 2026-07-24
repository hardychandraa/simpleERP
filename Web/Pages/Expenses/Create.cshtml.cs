using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Expenses;

public class CreateModel : PageModel
{
    private readonly IExpenseService _svc;
    public CreateModel(IExpenseService svc) => _svc = svc;

    [BindProperty] public CreateExpenseDto Input { get; set; } = new();
    public List<ExpenseCategoryDto> Categories { get; set; } = new();
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Record Expense";
        Input.ExpenseDate = DateTime.Today;
        Categories = await _svc.GetCategoriesAsync(activeOnly: true);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = "Record Expense";
        var result = await _svc.CreateAsync(Input, User.Identity?.Name ?? "staff");
        if (!result.Success)
        {
            Error = result.Error;
            Categories = await _svc.GetCategoriesAsync(activeOnly: true);
            return Page();
        }
        return Redirect("/Expenses?msg=Expense+recorded.");
    }
}
