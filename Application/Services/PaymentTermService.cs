using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// CRUD over the PaymentTerm master table. Terms that have been used on a posted
/// sale can be deactivated but never deleted, so historical invoices keep showing
/// the term they were actually written under.
/// </summary>
public class PaymentTermService : IPaymentTermService
{
    private const int MaxDueDays = 365;

    private readonly IPaymentTermRepository _terms;
    private readonly IAuditLogRepository    _audit;
    private readonly IUnitOfWork            _uow;

    public PaymentTermService(IPaymentTermRepository terms, IAuditLogRepository audit, IUnitOfWork uow)
    { _terms = terms; _audit = audit; _uow = uow; }

    public async Task<List<PaymentTermDto>> GetAllAsync(bool activeOnly = false)
    {
        var list = await _terms.GetAllAsync(activeOnly);
        var dtos = new List<PaymentTermDto>(list.Count);
        foreach (var t in list)
            dtos.Add(Map(t, await _terms.IsInUseAsync(t.Id)));
        return dtos;
    }

    public async Task<PaymentTermDto?> GetByIdAsync(Guid id)
    {
        var t = await _terms.GetByIdAsync(id);
        return t == null ? null : Map(t, await _terms.IsInUseAsync(t.Id));
    }

    public async Task<ServiceResult> CreateAsync(PaymentTermDto dto, string user)
    {
        var invalid = await ValidateAsync(dto, null);
        if (invalid != null) return invalid;

        var term = new PaymentTerm {
            Id        = Guid.NewGuid(),
            Name      = dto.Name.Trim(),
            DueDays   = dto.DueDays,
            IsActive  = dto.IsActive,
            SortOrder = dto.SortOrder
        };
        await _terms.AddAsync(term);
        await _audit.LogAsync(user, "PaymentTerm.Create", $"{term.Name} ({term.DueDays}d)");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(PaymentTermDto dto, string user)
    {
        var term = await _terms.GetByIdAsync(dto.Id);
        if (term == null) return ServiceResult.Fail("Payment term not found.");

        var invalid = await ValidateAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        // DueDays is deliberately editable even when the term is in use: DueDate is
        // computed and stored on each Sale at creation time, so changing the term
        // here affects only future sales, never posted ones.
        var before = $"{term.Name} ({term.DueDays}d, active={term.IsActive})";
        term.Name      = dto.Name.Trim();
        term.DueDays   = dto.DueDays;
        term.IsActive  = dto.IsActive;
        term.SortOrder = dto.SortOrder;

        _terms.Update(term);
        await _audit.LogAsync(user, "PaymentTerm.Update",
            $"{before} -> {term.Name} ({term.DueDays}d, active={term.IsActive})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, string user)
    {
        var term = await _terms.GetByIdAsync(id);
        if (term == null) return ServiceResult.Fail("Payment term not found.");

        if (await _terms.IsInUseAsync(id))
            return ServiceResult.Fail(
                $"'{term.Name}' is used by existing sales and cannot be deleted. " +
                "Set it to inactive instead — it will stop appearing on new sales " +
                "while existing invoices keep showing it.");

        _terms.Remove(term);
        await _audit.LogAsync(user, "PaymentTerm.Delete", term.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateAsync(PaymentTermDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult.Fail("Term name is required.");
        if (dto.DueDays < 0)
            return ServiceResult.Fail("Due days cannot be negative.");
        if (dto.DueDays > MaxDueDays)
            return ServiceResult.Fail($"Due days cannot exceed {MaxDueDays}.");
        if (await _terms.NameExistsAsync(dto.Name.Trim(), excludeId))
            return ServiceResult.Fail($"A payment term named '{dto.Name.Trim()}' already exists.");
        return null;
    }

    private static PaymentTermDto Map(PaymentTerm t, bool inUse) => new() {
        Id = t.Id, Name = t.Name, DueDays = t.DueDays,
        IsActive = t.IsActive, SortOrder = t.SortOrder, InUse = inUse
    };
}
