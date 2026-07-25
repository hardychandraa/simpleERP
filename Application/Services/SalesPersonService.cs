using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// CRUD over the SalesPerson master table. Someone credited with a posted sale can be
/// deactivated but never deleted, so historical attribution — and the commission that
/// will be calculated from it — stays resolvable.
/// </summary>
public class SalesPersonService : ISalesPersonService
{
    private readonly ISalesPersonRepository _people;
    private readonly IAuditLogRepository    _audit;
    private readonly IUnitOfWork            _uow;

    public SalesPersonService(ISalesPersonRepository people, IAuditLogRepository audit, IUnitOfWork uow)
    { _people = people; _audit = audit; _uow = uow; }

    public async Task<List<SalesPersonDto>> GetAllAsync(bool activeOnly = false)
    {
        var list = await _people.GetAllAsync(activeOnly);
        var dtos = new List<SalesPersonDto>(list.Count);
        foreach (var p in list)
            dtos.Add(Map(p, await _people.IsInUseAsync(p.Id)));
        return dtos;
    }

    public async Task<SalesPersonDto?> GetByIdAsync(Guid id)
    {
        var p = await _people.GetByIdAsync(id);
        return p == null ? null : Map(p, await _people.IsInUseAsync(p.Id));
    }

    public async Task<ServiceResult> CreateAsync(SalesPersonDto dto, string user)
    {
        var invalid = await ValidateAsync(dto, null);
        if (invalid != null) return invalid;

        var person = new SalesPerson {
            Id       = Guid.NewGuid(),
            Name     = dto.Name.Trim(),
            Phone    = Blank(dto.Phone),
            IsActive = dto.IsActive
        };
        await _people.AddAsync(person);
        await _audit.LogAsync(user, "SalesPerson.Create", person.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(SalesPersonDto dto, string user)
    {
        var person = await _people.GetByIdAsync(dto.Id);
        if (person == null) return ServiceResult.Fail("Sales person not found.");

        var invalid = await ValidateAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        var before = $"{person.Name} (active={person.IsActive})";
        person.Name     = dto.Name.Trim();
        person.Phone    = Blank(dto.Phone);
        person.IsActive = dto.IsActive;

        _people.Update(person);
        await _audit.LogAsync(user, "SalesPerson.Update",
            $"{before} -> {person.Name} (active={person.IsActive})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, string user)
    {
        var person = await _people.GetByIdAsync(id);
        if (person == null) return ServiceResult.Fail("Sales person not found.");

        if (await _people.IsInUseAsync(id))
            return ServiceResult.Fail(
                $"'{person.Name}' is credited with existing sales and cannot be deleted. " +
                "Set them to inactive instead — they will stop appearing on new sales " +
                "while existing invoices keep showing them.");

        _people.Remove(person);
        await _audit.LogAsync(user, "SalesPerson.Delete", person.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateAsync(SalesPersonDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult.Fail("Name is required.");
        if (dto.Name.Trim().Length > 200)
            return ServiceResult.Fail("Name cannot exceed 200 characters.");
        if (dto.Phone?.Trim().Length > 50)
            return ServiceResult.Fail("Phone cannot exceed 50 characters.");
        if (await _people.NameExistsAsync(dto.Name.Trim(), excludeId))
            return ServiceResult.Fail($"A sales person named '{dto.Name.Trim()}' already exists.");
        return null;
    }

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static SalesPersonDto Map(SalesPerson p, bool inUse) => new() {
        Id = p.Id, Name = p.Name, Phone = p.Phone, IsActive = p.IsActive, InUse = inUse
    };
}
