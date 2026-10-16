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

    /// <param name="setsSaleDefaults">
    /// Admin only (G6, HC 2026-10-14): a customer's default discount, term and salesperson
    /// flow into every sale, so Staff can add a customer and edit its contact details, but
    /// what they post for the defaults is ignored (new customer: none; existing: unchanged).
    /// The credit limit (plafon, HC 2026-10-15) is Admin's in the same way; a customer Staff create starts at 0.
    /// </param>
    public async Task<ServiceResult> CreateAsync(CreateCustomerDto dto, bool setsSaleDefaults)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Customer name is required."]);
        // A customer Staff create starts cash only (limit 0) until Admin sets a limit (R4, HC 2026-10-16):
        // otherwise a new customer could buy on unlimited credit the moment Staff typed its name.
        if (!setsSaleDefaults) { dto.SalesPersonId = null; dto.PaymentTermId = null; dto.DefaultDiscountPercent = null; dto.CreditLimit = 0m; }
        var invalid = ValidateTaxIds(dto.TaxId, dto.NationalId)
                      ?? ValidateCreditLimit(dto.CreditLimit)
                      ?? await ValidateDefaultsAsync(dto.SalesPersonId, dto.PaymentTermId, dto.DefaultDiscountPercent);
        if (invalid != null) return invalid;
        await _customers.AddAsync(new Customer {
            // Control characters stripped: the name and address are printed on the LX (R9).
            Id = Guid.NewGuid(), Name = TextClean.StripControl(dto.Name)!.Trim().ToUpperInvariant(),
            Phone = TextClean.StripControl(dto.Phone)?.Trim(), Address = TextClean.StripControl(dto.Address)?.Trim(),
            TaxId = Blank(dto.TaxId), NationalId = Blank(dto.NationalId),
            SalesPersonId = dto.SalesPersonId, PaymentTermId = dto.PaymentTermId,
            DefaultDiscountPercent = NormaliseDiscount(dto.DefaultDiscountPercent),
            CreditLimit = dto.CreditLimit,
            IsActive = true, CreatedAt = DateTime.UtcNow });
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateAsync(UpdateCustomerDto dto, bool setsSaleDefaults)
    {
        var c = await _customers.GetByIdAsync(dto.Id);
        if (c == null) return _log.Refuse(_loc["Customer not found."]);
        if (!setsSaleDefaults)
        { dto.SalesPersonId = c.SalesPersonId; dto.PaymentTermId = c.PaymentTermId; dto.DefaultDiscountPercent = c.DefaultDiscountPercent;
          dto.CreditLimit = c.CreditLimit; }
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Name is required."]);
        var invalid = ValidateTaxIds(dto.TaxId, dto.NationalId)
                      ?? ValidateCreditLimit(dto.CreditLimit)
                      ?? await ValidateDefaultsAsync(dto.SalesPersonId, dto.PaymentTermId, dto.DefaultDiscountPercent,
                                                     c.SalesPersonId, c.PaymentTermId);
        if (invalid != null) return invalid;
        c.Name = TextClean.StripControl(dto.Name)!.Trim().ToUpperInvariant(); c.Phone = TextClean.StripControl(dto.Phone)?.Trim();
        c.Address = TextClean.StripControl(dto.Address)?.Trim(); c.IsActive = dto.IsActive;
        c.TaxId = Blank(dto.TaxId); c.NationalId = Blank(dto.NationalId);
        c.SalesPersonId = dto.SalesPersonId; c.PaymentTermId = dto.PaymentTermId;
        c.DefaultDiscountPercent = NormaliseDiscount(dto.DefaultDiscountPercent);
        c.CreditLimit = dto.CreditLimit;
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

    /// <summary>Empty = no limit; 0 is a real value (no credit at all), so it is kept, not blanked.</summary>
    private ServiceResult? ValidateCreditLimit(decimal? limit) =>
        limit is < 0m                  ? _log.Refuse(_loc["The credit limit cannot be negative."])
        : limit > Limits.MaxLineAmount ? _log.Refuse(_loc["Amount is too large (at most {0}).", Limits.MaxLineAmount.ToString("N0")])
        : null;

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>0% is the same as no discount; stored as null so "none" has one meaning.</summary>
    private static decimal? NormaliseDiscount(decimal? discount) => discount is > 0 ? discount : null;

    private static CustomerDto Map(Domain.Entities.Customer c) => new()
        { Id=c.Id, Name=c.Name, Phone=c.Phone, Address=c.Address, IsActive=c.IsActive,
          TaxId=c.TaxId, NationalId=c.NationalId,
          SalesPersonId=c.SalesPersonId, PaymentTermId=c.PaymentTermId,
          DefaultDiscountPercent=c.DefaultDiscountPercent, CreditLimit=c.CreditLimit };
}
