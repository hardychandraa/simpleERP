using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
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

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<SalesPersonService> _log;
    public SalesPersonService(ISalesPersonRepository people, IAuditLogRepository audit, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<SalesPersonService> log)
    { _people = people; _audit = audit; _uow = uow;  _loc = loc; _log = log; }

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
            Code     = NormaliseCode(dto.Code),
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
        if (person == null) return _log.Refuse(_loc["Sales person not found."]);

        var invalid = await ValidateAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        var before = $"{person.Name} (active={person.IsActive})";
        person.Name     = dto.Name.Trim();
        person.Code     = NormaliseCode(dto.Code);
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
        if (person == null) return _log.Refuse(_loc["Sales person not found."]);

        if (await _people.IsInUseAsync(id))
            return _log.Refuse(_loc["'{0}' is credited with existing sales and cannot be deleted. Set them to inactive instead — they will stop appearing on new sales while existing invoices keep showing them.", person.Name]);

        _people.Remove(person);
        await _audit.LogAsync(user, "SalesPerson.Delete", person.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateAsync(SalesPersonDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return _log.Refuse(_loc["Name is required."]);
        if (dto.Name.Trim().Length > 200)
            return _log.Refuse(_loc["Name cannot exceed 200 characters."]);
        if (dto.Phone?.Trim().Length > 50)
            return _log.Refuse(_loc["Phone cannot exceed 50 characters."]);
        if (await _people.NameExistsAsync(dto.Name.Trim(), excludeId))
            return _log.Refuse(_loc["A sales person named '{0}' already exists.", dto.Name.Trim()]);
        // The code, not the name, is what the printed invoice shows — so it is required and
        // must identify exactly one person.
        var code = NormaliseCode(dto.Code);
        if (code == null)
            return _log.Refuse(_loc["Sales code is required (e.g. S01)."]);
        if (code.Length > 10)
            return _log.Refuse(_loc["Sales code cannot exceed 10 characters."]);
        if (await _people.CodeExistsAsync(code, excludeId))
            return _log.Refuse(_loc["Sales code '{0}' is already used by another sales person.", code]);
        return null;
    }

    private static string? NormaliseCode(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static SalesPersonDto Map(SalesPerson p, bool inUse) => new() {
        Id = p.Id, Name = p.Name, Code = p.Code, Phone = p.Phone, IsActive = p.IsActive, InUse = inUse
    };
}
