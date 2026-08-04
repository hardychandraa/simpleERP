using Microsoft.Extensions.Localization;
using SimpleERP.Application.Resources;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Expenses;

public class CategoriesModel : PageModel
{
    private readonly IExpenseService _svc;
    private readonly IStringLocalizer<SharedResource> _loc;
    public CategoriesModel(IExpenseService svc, IStringLocalizer<SharedResource> loc) { _svc = svc; _loc = loc; }

    public List<ExpenseCategoryDto> Categories { get; set; } = new();
    public string? Msg   { get; set; }
    public bool    IsErr { get; set; }

    [BindProperty] public ExpenseCategoryDto Input { get; set; } = new();

    public async Task OnGetAsync(string? msg, bool err = false)
    {
        ViewData["Title"] = "Expense Categories";
        Msg = msg; IsErr = err;
        Categories = await _svc.GetCategoriesAsync();
    }

    private string User_ => User.Identity?.Name ?? "staff";

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var result = await _svc.CreateCategoryAsync(Input, User_);
        return Redirect(result.Success
            ? $"/Expenses/Categories?msg={Uri.EscapeDataString(_loc["'{0}' added.", Input.Name].Value)}"
            : $"/Expenses/Categories?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        var result = await _svc.UpdateCategoryAsync(Input, User_);
        return Redirect(result.Success
            ? $"/Expenses/Categories?msg={Uri.EscapeDataString(_loc["'{0}' saved.", Input.Name].Value)}"
            : $"/Expenses/Categories?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await _svc.DeleteCategoryAsync(id, User_);
        return Redirect(result.Success
            ? $"/Expenses/Categories?msg={Uri.EscapeDataString(_loc["Category deleted."].Value)}"
            : $"/Expenses/Categories?err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
