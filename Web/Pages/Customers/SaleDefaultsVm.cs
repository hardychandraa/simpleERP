using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;

namespace SimpleERP.Web.Pages.Customers;

/// <summary>What the _SaleDefaults partial needs: the option lists and the saved values.</summary>
public record SaleDefaultsVm(List<SalesPersonDto> People, List<PaymentTermDto> Terms,
                             Guid? SalesPersonId, Guid? PaymentTermId, decimal? DiscountPercent)
{
    /// <summary>All people and terms, inactive included, so a retired saved value still shows.</summary>
    public static async Task<(List<SalesPersonDto>, List<PaymentTermDto>)> LoadOptionsAsync(
        ISalesPersonService people, IPaymentTermService terms)
        => (await people.GetAllAsync(activeOnly: false), await terms.GetAllAsync(activeOnly: false));
}

/// <summary>What the _CreditLimit partial needs (Admin only): the saved plafon and, on Edit, where the customer stands.</summary>
public record CreditLimitVm(decimal? Limit, CustomerCreditDto? Status, CreditSuggestionDto? Suggestion = null);
