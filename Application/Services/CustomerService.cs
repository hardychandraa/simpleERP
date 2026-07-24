using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _uow;
    public CustomerService(ICustomerRepository c, IUnitOfWork uow) { _customers=c; _uow=uow; }

    public async Task<List<CustomerDto>> GetAllAsync(string? search = null)
        => Filter(await _customers.GetAllAsync(), search);
    public async Task<List<CustomerDto>> GetAllActiveAsync(string? search = null)
        => Filter(await _customers.GetAllActiveAsync(), search);
    public async Task<CustomerDto?> GetByIdAsync(Guid id)
        { var c = await _customers.GetByIdAsync(id); return c == null ? null : Map(c); }

    public async Task<ServiceResult> CreateAsync(CreateCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Customer name is required.");
        await _customers.AddAsync(new Customer {
            Id = Guid.NewGuid(), Name = dto.Name.Trim(),
            Phone = dto.Phone?.Trim(), Address = dto.Address?.Trim(),
            IsActive = true, CreatedAt = DateTime.UtcNow });
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UpdateCustomerDto dto)
    {
        var c = await _customers.GetByIdAsync(dto.Id);
        if (c == null) return ServiceResult.Fail("Customer not found.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Name is required.");
        c.Name = dto.Name.Trim(); c.Phone = dto.Phone?.Trim();
        c.Address = dto.Address?.Trim(); c.IsActive = dto.IsActive;
        _customers.Update(c); await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private List<CustomerDto> Filter(List<Domain.Entities.Customer> list, string? search)
    {
        var q = string.IsNullOrWhiteSpace(search) ? list
            : list.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || (c.Phone ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        return q.Select(Map).ToList();
    }

    private static CustomerDto Map(Domain.Entities.Customer c) => new()
        { Id=c.Id, Name=c.Name, Phone=c.Phone, Address=c.Address, IsActive=c.IsActive };
}
