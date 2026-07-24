using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SimpleERP.Web.Pages.Expenses;

public class IndexModel : PageModel
{
    private readonly IExpenseService _svc;
    public IndexModel(IExpenseService svc) => _svc = svc;

    public List<ExpenseDto>         Expenses   { get; set; } = new();
    public List<ExpenseCategoryDto> Categories { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To   { get; set; }
    public Guid?    CategoryId { get; set; }
    public string?  Msg   { get; set; }
    public bool     IsErr { get; set; }

    public decimal Total => Expenses.Sum(e => e.Amount);

    public async Task OnGetAsync(DateTime? from, DateTime? to, Guid? categoryId, string? msg, bool err = false)
    {
        ViewData["Title"] = "Expenses";
        Msg = msg; IsErr = err;

        // Default to the current month, matching how expenses are usually reviewed.
        To   = to?.Date   ?? DateTime.Today;
        From = from?.Date ?? new DateTime(To.Year, To.Month, 1);
        if (From > To) (From, To) = (To, From);
        CategoryId = categoryId;

        Categories = await _svc.GetCategoriesAsync();
        Expenses   = await _svc.GetAllAsync(From, To, CategoryId);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, DateTime from, DateTime to)
    {
        var result = await _svc.DeleteAsync(id, User.Identity?.Name ?? "staff");
        var range  = $"from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        return Redirect(result.Success
            ? $"/Expenses?{range}&msg=Expense+deleted."
            : $"/Expenses?{range}&err=true&msg={Uri.EscapeDataString(result.Error!)}");
    }
}
