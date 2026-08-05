using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// Operating expenses (Biaya Usaha) and their categories.
///
/// Unlike Sales, expenses are editable and deletable after posting: they move no
/// stock and touch no ledger, so correcting a mistyped amount is a plain edit
/// rather than a reversal. Every change is audited with before/after values so
/// the history is still reconstructible.
/// </summary>
public class ExpenseService : IExpenseService
{
    private readonly IExpenseRepository         _expenses;
    private readonly IExpenseCategoryRepository _categories;
    private readonly IAuditLogRepository        _audit;
    private readonly IUnitOfWork                _uow;

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<ExpenseService> _log;
    public ExpenseService(IExpenseRepository expenses, IExpenseCategoryRepository categories,
                          IAuditLogRepository audit, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<ExpenseService> log)
    { _expenses = expenses; _categories = categories; _audit = audit; _uow = uow;  _loc = loc; _log = log; }

    // ── Expenses ──────────────────────────────────────────────────────────────

    public async Task<List<ExpenseDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, Guid? categoryId = null)
    {
        // Callers pass an inclusive end date; the repository filters on a half-open
        // range, so push the boundary out by a day to include the final day itself.
        var list = await _expenses.GetAllAsync(from?.Date, to?.Date.AddDays(1), categoryId);
        return list.Select(Map).ToList();
    }

    public async Task<ExpenseDto?> GetByIdAsync(Guid id)
    {
        var e = await _expenses.GetByIdAsync(id);
        return e == null ? null : Map(e);
    }

    public async Task<ServiceResult> CreateAsync(CreateExpenseDto dto, string user)
    {
        var invalid = await ValidateExpenseAsync(dto);
        if (invalid != null) return invalid;

        var expense = new Expense {
            Id                = Guid.NewGuid(),
            ExpenseDate       = dto.ExpenseDate.Date,
            ExpenseCategoryId = dto.CategoryId,
            Amount            = dto.Amount,
            Description       = Trim(dto.Description, 500),
            ReferenceNo       = Trim(dto.ReferenceNo, 100),
            CreatedBy         = user,
            CreatedAt         = DateTime.UtcNow
        };
        await _expenses.AddAsync(expense);

        var cat = await _categories.GetByIdAsync(dto.CategoryId);
        await _audit.LogAsync(user, "Expense.Create",
            $"{cat?.Name} {expense.Amount:N0} on {expense.ExpenseDate:yyyy-MM-dd}");
        _log.LogInformation(
            "Expense recorded — {Amount} to {Category} dated {ExpenseDate:yyyy-MM-dd}, by {User}",
            expense.Amount, cat?.Name ?? "unknown category", expense.ExpenseDate, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UpdateExpenseDto dto, string user)
    {
        var expense = await _expenses.GetByIdAsync(dto.Id);
        if (expense == null) return _log.Refuse(_loc["Expense not found."]);

        var invalid = await ValidateExpenseAsync(dto);
        if (invalid != null) return invalid;

        var before = $"{expense.Category?.Name} {expense.Amount:N0} on {expense.ExpenseDate:yyyy-MM-dd}";

        expense.ExpenseDate       = dto.ExpenseDate.Date;
        expense.ExpenseCategoryId = dto.CategoryId;
        expense.Amount            = dto.Amount;
        expense.Description       = Trim(dto.Description, 500);
        expense.ReferenceNo       = Trim(dto.ReferenceNo, 100);

        _expenses.Update(expense);
        var cat = await _categories.GetByIdAsync(dto.CategoryId);
        await _audit.LogAsync(user, "Expense.Update",
            $"{before} -> {cat?.Name} {expense.Amount:N0} on {expense.ExpenseDate:yyyy-MM-dd}");
        // Both sides are logged: an expense edit changes a period's Laba Usaha after the
        // fact, so knowing only the new figure is not enough to explain a moved P&L.
        _log.LogInformation(
            "Expense amended — was {Before}, now {Amount} to {Category} dated {ExpenseDate:yyyy-MM-dd}, by {User}",
            before, expense.Amount, cat?.Name ?? "unknown category", expense.ExpenseDate, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, string user)
    {
        var expense = await _expenses.GetByIdAsync(id);
        if (expense == null) return _log.Refuse(_loc["Expense not found."]);

        _expenses.Remove(expense);
        await _audit.LogAsync(user, "Expense.Delete",
            $"{expense.Category?.Name} {expense.Amount:N0} on {expense.ExpenseDate:yyyy-MM-dd}");
        // A hard delete — the row is gone, so this line and the audit entry are the only
        // remaining evidence the expense ever existed.
        _log.LogInformation(
            "Expense deleted — {Amount} to {Category} dated {ExpenseDate:yyyy-MM-dd}, by {User}",
            expense.Amount, expense.Category?.Name ?? "unknown category", expense.ExpenseDate, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateExpenseAsync(CreateExpenseDto dto)
    {
        if (dto.CategoryId == Guid.Empty) return _log.Refuse(_loc["Category is required."]);
        if (dto.Amount <= 0)              return _log.Refuse(_loc["Amount must be greater than zero."]);
        if (dto.ExpenseDate == default)   return _log.Refuse(_loc["Date is required."]);
        // A future-dated expense is almost always a typo in the year field, and it
        // would silently distort any period report that includes it. Compared
        // local-to-local, matching Sale's stricter guard — see decisions.md, 2026-07-31.
        if (dto.ExpenseDate.Date > DateTime.Now.Date)
            return _log.Refuse(_loc["Expense date cannot be in the future."]);

        var cat = await _categories.GetByIdAsync(dto.CategoryId);
        if (cat == null)   return _log.Refuse(_loc["Category not found."]);
        if (!cat.IsActive) return _log.Refuse(_loc["Category '{0}' is no longer active.", cat.Name]);
        return null;
    }

    // ── Categories ────────────────────────────────────────────────────────────

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false)
    {
        var list = await _categories.GetAllAsync(activeOnly);
        var dtos = new List<ExpenseCategoryDto>(list.Count);
        foreach (var c in list)
            dtos.Add(new ExpenseCategoryDto {
                Id = c.Id, Name = c.Name, IsActive = c.IsActive,
                IsTaxDeductible = c.IsTaxDeductible, SortOrder = c.SortOrder,
                InUse = await _categories.IsInUseAsync(c.Id) });
        return dtos;
    }

    public async Task<ServiceResult> CreateCategoryAsync(ExpenseCategoryDto dto, string user)
    {
        var invalid = await ValidateCategoryAsync(dto, null);
        if (invalid != null) return invalid;

        await _categories.AddAsync(new ExpenseCategory {
            Id = Guid.NewGuid(), Name = dto.Name.Trim(), IsActive = dto.IsActive,
            IsTaxDeductible = dto.IsTaxDeductible, SortOrder = dto.SortOrder });
        await _audit.LogAsync(user, "ExpenseCategory.Create", dto.Name.Trim());
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateCategoryAsync(ExpenseCategoryDto dto, string user)
    {
        var cat = await _categories.GetByIdAsync(dto.Id);
        if (cat == null) return _log.Refuse(_loc["Category not found."]);

        var invalid = await ValidateCategoryAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        var before = $"{cat.Name} (active={cat.IsActive}, deductible={cat.IsTaxDeductible})";
        cat.Name            = dto.Name.Trim();
        cat.IsActive        = dto.IsActive;
        cat.IsTaxDeductible = dto.IsTaxDeductible;
        cat.SortOrder       = dto.SortOrder;

        _categories.Update(cat);
        await _audit.LogAsync(user, "ExpenseCategory.Update",
            $"{before} -> {cat.Name} (active={cat.IsActive}, deductible={cat.IsTaxDeductible})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteCategoryAsync(Guid id, string user)
    {
        var cat = await _categories.GetByIdAsync(id);
        if (cat == null) return _log.Refuse(_loc["Category not found."]);

        if (await _categories.IsInUseAsync(id))
            return _log.Refuse(_loc["'{0}' has expenses recorded against it and cannot be deleted. Set it to inactive instead — it will stop appearing on new expenses while existing ones stay categorised.", cat.Name]);

        _categories.Remove(cat);
        await _audit.LogAsync(user, "ExpenseCategory.Delete", cat.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateCategoryAsync(ExpenseCategoryDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return _log.Refuse(_loc["Category name is required."]);
        if (await _categories.NameExistsAsync(dto.Name.Trim(), excludeId))
            return _log.Refuse(_loc["A category named '{0}' already exists.", dto.Name.Trim()]);
        return null;
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private static ExpenseDto Map(Expense e) => new() {
        Id = e.Id, ExpenseDate = e.ExpenseDate, CategoryId = e.ExpenseCategoryId,
        CategoryName = e.Category?.Name ?? "", Amount = e.Amount,
        Description = e.Description, ReferenceNo = e.ReferenceNo, CreatedBy = e.CreatedBy
    };
}
