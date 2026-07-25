using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// CRUD over the Supplier master table. A supplier with posted purchases can be
/// deactivated but never deleted — the purchase history, the AP balance and (from
/// Step 6) every rebate accrual all hang off it.
/// </summary>
public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository    _suppliers;
    private readonly IPaymentTermRepository _terms;
    private readonly IAuditLogRepository    _audit;
    private readonly IUnitOfWork            _uow;

    public SupplierService(ISupplierRepository suppliers, IPaymentTermRepository terms,
        IAuditLogRepository audit, IUnitOfWork uow)
    { _suppliers = suppliers; _terms = terms; _audit = audit; _uow = uow; }

    public async Task<List<SupplierDto>> GetAllAsync(bool activeOnly = false)
    {
        var list = await _suppliers.GetAllAsync(activeOnly);
        var dtos = new List<SupplierDto>(list.Count);
        foreach (var s in list)
            dtos.Add(Map(s, await _suppliers.IsInUseAsync(s.Id)));
        return dtos;
    }

    public async Task<SupplierDto?> GetByIdAsync(Guid id)
    {
        var s = await _suppliers.GetByIdAsync(id);
        return s == null ? null : Map(s, await _suppliers.IsInUseAsync(s.Id));
    }

    public async Task<ServiceResult> CreateAsync(SupplierDto dto, string user)
    {
        var invalid = await ValidateAsync(dto, null);
        if (invalid != null) return invalid;

        var supplier = new Supplier {
            Id            = Guid.NewGuid(),
            Name          = dto.Name.Trim(),
            Phone         = Blank(dto.Phone),
            Address       = Blank(dto.Address),
            TaxId         = Blank(dto.TaxId),
            PaymentTermId = dto.PaymentTermId,
            IsActive      = dto.IsActive,
            Notes         = Blank(dto.Notes),
            CreatedAt     = DateTime.UtcNow
        };
        await _suppliers.AddAsync(supplier);
        await _audit.LogAsync(user, "Supplier.Create", supplier.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(SupplierDto dto, string user)
    {
        var supplier = await _suppliers.GetByIdAsync(dto.Id);
        if (supplier == null) return ServiceResult.Fail("Supplier not found.");

        var invalid = await ValidateAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        var before = $"{supplier.Name} (active={supplier.IsActive})";
        supplier.Name          = dto.Name.Trim();
        supplier.Phone         = Blank(dto.Phone);
        supplier.Address       = Blank(dto.Address);
        supplier.TaxId         = Blank(dto.TaxId);
        supplier.PaymentTermId = dto.PaymentTermId;
        supplier.IsActive      = dto.IsActive;
        supplier.Notes         = Blank(dto.Notes);

        _suppliers.Update(supplier);
        await _audit.LogAsync(user, "Supplier.Update",
            $"{before} -> {supplier.Name} (active={supplier.IsActive})");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, string user)
    {
        var supplier = await _suppliers.GetByIdAsync(id);
        if (supplier == null) return ServiceResult.Fail("Supplier not found.");

        if (await _suppliers.IsInUseAsync(id))
            return ServiceResult.Fail(
                $"'{supplier.Name}' has posted purchases and cannot be deleted. " +
                "Set them to inactive instead — they will stop appearing on new " +
                "purchases while existing documents keep showing them.");

        _suppliers.Remove(supplier);
        await _audit.LogAsync(user, "Supplier.Delete", supplier.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateAsync(SupplierDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult.Fail("Supplier name is required.");
        if (dto.Name.Trim().Length > 300)
            return ServiceResult.Fail("Supplier name cannot exceed 300 characters.");
        if (await _suppliers.NameExistsAsync(dto.Name.Trim(), excludeId))
            return ServiceResult.Fail($"A supplier named '{dto.Name.Trim()}' already exists.");

        if (dto.PaymentTermId.HasValue)
        {
            var term = await _terms.GetByIdAsync(dto.PaymentTermId.Value);
            if (term == null) return ServiceResult.Fail("Selected payment term not found.");
        }
        return null;
    }

    private static string? Blank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static SupplierDto Map(Supplier s, bool inUse) => new() {
        Id = s.Id, Name = s.Name, Phone = s.Phone, Address = s.Address,
        TaxId = s.TaxId, PaymentTermId = s.PaymentTermId,
        PaymentTermName = s.PaymentTerm?.Name ?? "",
        IsActive = s.IsActive, Notes = s.Notes, InUse = inUse
    };
}
