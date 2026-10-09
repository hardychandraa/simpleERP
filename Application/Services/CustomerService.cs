using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly ISalesPersonRepository _people;
    private readonly IPaymentTermRepository _terms;
    private readonly IUnitOfWork _uow;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<CustomerService> _log;
    public CustomerService(ICustomerRepository c, ISalesPersonRepository people, IPaymentTermRepository terms,
        IUnitOfWork uow, IStringLocalizer<SharedResource> loc, ILogger<CustomerService> log)
    { _customers=c; _people=people; _terms=terms; _uow=uow;  _loc = loc; _log = log; }

    public async Task<List<CustomerDto>> GetAllAsync(string? search = null)
        => Filter(await _customers.GetAllAsync(), search);
    public async Task<List<CustomerDto>> GetAllActiveAsync(string? search = null)
        => Filter(await _customers.GetAllActiveAsync(), search);
    public async Task<CustomerDto?> GetByIdAsync(Guid id)
        { var c = await _customers.GetByIdAsync(id); return c == null ? null : Map(c); }

    public async Task<ServiceResult> CreateAsync(CreateCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Customer name is required."]);
        var invalid = ValidateTaxIds(dto.TaxId, dto.NationalId)
                      ?? await ValidateDefaultsAsync(dto.SalesPersonId, dto.PaymentTermId, dto.DefaultDiscountPercent);
        if (invalid != null) return invalid;
        await _customers.AddAsync(new Customer {
            // Control characters stripped: the name and address are printed on the LX (R9).
            Id = Guid.NewGuid(), Name = TextClean.StripControl(dto.Name)!.Trim().ToUpperInvariant(),
            Phone = TextClean.StripControl(dto.Phone)?.Trim(), Address = TextClean.StripControl(dto.Address)?.Trim(),
            TaxId = Blank(dto.TaxId), NationalId = Blank(dto.NationalId),
            SalesPersonId = dto.SalesPersonId, PaymentTermId = dto.PaymentTermId,
            DefaultDiscountPercent = NormaliseDiscount(dto.DefaultDiscountPercent),
            IsActive = true, CreatedAt = DateTime.UtcNow });
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UpdateCustomerDto dto)
    {
        var c = await _customers.GetByIdAsync(dto.Id);
        if (c == null) return _log.Refuse(_loc["Customer not found."]);
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Name is required."]);
        var invalid = ValidateTaxIds(dto.TaxId, dto.NationalId)
                      ?? await ValidateDefaultsAsync(dto.SalesPersonId, dto.PaymentTermId, dto.DefaultDiscountPercent,
                                                     c.SalesPersonId, c.PaymentTermId);
        if (invalid != null) return invalid;
        c.Name = TextClean.StripControl(dto.Name)!.Trim().ToUpperInvariant(); c.Phone = TextClean.StripControl(dto.Phone)?.Trim();
        c.Address = TextClean.StripControl(dto.Address)?.Trim(); c.IsActive = dto.IsActive;
        c.TaxId = Blank(dto.TaxId); c.NationalId = Blank(dto.NationalId);
        c.SalesPersonId = dto.SalesPersonId; c.PaymentTermId = dto.PaymentTermId;
        c.DefaultDiscountPercent = NormaliseDiscount(dto.DefaultDiscountPercent);
        _customers.Update(c); await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private List<CustomerDto> Filter(List<Domain.Entities.Customer> list, string? search)
    {
        var q = string.IsNullOrWhiteSpace(search) ? list
            : list.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                           || (c.Phone ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                           || (c.TaxId ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                           || (c.NationalId ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        return q.Select(Map).ToList();
    }

    /// <summary>
    /// The sale defaults must point at a real, active salesperson and term (an inactive one
    /// is still accepted when it is the value already saved, so editing a phone number never
    /// fails because the term was retired), and the discount must be a usable percentage.
    /// </summary>
    private async Task<ServiceResult?> ValidateDefaultsAsync(Guid? personId, Guid? termId, decimal? discount,
        Guid? currentPersonId = null, Guid? currentTermId = null)
    {
        if (discount is < 0 or >= 100)
            return _log.Refuse(_loc["Discount percent must be between 0 and 100."]);
        if (personId is { } p && p != currentPersonId && await _people.GetByIdAsync(p) is not { IsActive: true })
            return _log.Refuse(_loc["Sales person not found."]);
        if (termId is { } t && t != currentTermId && await _terms.GetByIdAsync(t) is not { IsActive: true })
            return _log.Refuse(_loc["Payment term not found."]);
        return null;
    }

    /// <summary>
    /// Light format checks only, both fields optional: a NIK is exactly 16 digits; an NPWP is
    /// 15 digits (old format) or 16 (new format, 2024) once its dots and dashes are removed.
    /// Stored as typed so it reads the way it is printed on the card.
    /// </summary>
    private ServiceResult? ValidateTaxIds(string? npwp, string? nik)
    {
        if (Blank(nik) is { } n && !(n.Length == 16 && n.All(char.IsAsciiDigit)))
            return _log.Refuse(_loc["NIK must be exactly 16 digits."]);
        if (Blank(npwp) is { } t)
        {
            var digits = new string(t.Where(ch => ch is not ('.' or '-' or ' ')).ToArray());
            if (!digits.All(char.IsAsciiDigit) || digits.Length is not (15 or 16))
                return _log.Refuse(_loc["NPWP must have 15 or 16 digits."]);
        }
        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>0% is the same as no discount; stored as null so "none" has one meaning.</summary>
    private static decimal? NormaliseDiscount(decimal? discount) => discount is > 0 ? discount : null;

    private static CustomerDto Map(Domain.Entities.Customer c) => new()
        { Id=c.Id, Name=c.Name, Phone=c.Phone, Address=c.Address, IsActive=c.IsActive,
          TaxId=c.TaxId, NationalId=c.NationalId,
          SalesPersonId=c.SalesPersonId, PaymentTermId=c.PaymentTermId,
          DefaultDiscountPercent=c.DefaultDiscountPercent };
}
