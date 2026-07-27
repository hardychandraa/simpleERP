using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// The commission engine: accrues commission for a salesperson as revenue is collected,
/// and settles it in payouts. Mirrors RebateService's accrual/settlement shape.
///
/// Commission is a percentage of **revenue** (ex-PPN), not gross margin, earned **on
/// collection** — never at invoice time. A cash sale collects in full at creation; a
/// credit sale accrues in slices as each payment arrives, so commission is never paid
/// on debt that isn't collected. Accruals are per SaleItem so a future product- or
/// category-specific rate needs no redesign.
/// </summary>
public class CommissionService : ICommissionService
{
    private readonly ICommissionRuleRepository    _rules;
    private readonly ICommissionAccrualRepository _accruals;
    private readonly ICommissionPayoutRepository  _payouts;
    private readonly ISalesPersonRepository       _people;
    private readonly IProductRepository           _products;
    private readonly IAuditLogRepository          _audit;
    private readonly IUnitOfWork                  _uow;

    public CommissionService(ICommissionRuleRepository rules, ICommissionAccrualRepository accruals,
        ICommissionPayoutRepository payouts, ISalesPersonRepository people,
        IProductRepository products, IAuditLogRepository audit, IUnitOfWork uow)
    { _rules=rules; _accruals=accruals; _payouts=payouts; _people=people;
      _products=products; _audit=audit; _uow=uow; }

    // ── Accrual (called inside the SaleService transaction — no SaveChanges) ──

    /// <summary>Commission on a cash sale — the whole invoice is collected at creation.</summary>
    public Task AccrueForCashSaleAsync(Sale sale, IReadOnlyList<SaleItem> items)
        => AccrueAsync(sale, items, sale.GrandTotal, paymentRecordId: null);

    /// <summary>Commission on a credit payment — prorated to the fraction of the invoice it collects.</summary>
    public Task AccrueForPaymentAsync(Sale sale, PaymentRecord payment)
        => AccrueAsync(sale, sale.SaleItems.ToList(), payment.Amount, payment.Id);

    private async Task AccrueAsync(Sale sale, IReadOnlyList<SaleItem> items,
        decimal collectedAmount, Guid? paymentRecordId)
    {
        if (sale.SalesPersonId == null) return;              // unattributed — no commission
        if (sale.GrandTotal <= 0 || collectedAmount <= 0) return;

        var rules = await _rules.GetActiveForSalesPersonAsync(sale.SalesPersonId.Value);
        if (rules.Count == 0) return;

        // Each line's ex-PPN revenue share is prorated from the sale's TaxBase by its
        // net-of-discount line total, so the commission base matches the P&L's revenue.
        var totalNet = items.Sum(i => i.LineTotal - i.AllocatedInvoiceDiscount);
        if (totalNet <= 0m) return;

        var collectedFraction = collectedAmount / sale.GrandTotal;

        foreach (var item in items)
        {
            var rule = BestRule(rules, sale.SalesPersonId.Value, item, sale.SaleDate);
            if (rule == null) continue;

            var itemRevenue = sale.TaxBase * ((item.LineTotal - item.AllocatedInvoiceDiscount) / totalNet);
            var baseAmount  = Math.Round(itemRevenue * collectedFraction, 2, MidpointRounding.AwayFromZero);
            var amount      = Math.Round(baseAmount * rule.Rate / 100m, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m) continue;

            await _accruals.AddAsync(new CommissionAccrual {
                Id = Guid.NewGuid(), SaleId = sale.Id, SaleItemId = item.Id,
                PaymentRecordId = paymentRecordId, SalesPersonId = sale.SalesPersonId.Value,
                CommissionRuleId = rule.Id, BaseAmount = baseAmount, Rate = rule.Rate,
                Amount = amount, AccrualDate = DateTime.UtcNow
            });
        }
    }

    /// <summary>Voids (never deletes) a sale's unpaid accruals on cancellation. No SaveChanges.</summary>
    public async Task VoidForSaleAsync(Guid saleId)
    {
        var list = await _accruals.GetBySaleAsync(saleId);
        foreach (var a in list.Where(a => a.CommissionPayoutId == null && !a.IsVoided))
            a.IsVoided = true;   // tracked mutation — no Update() on possibly-Added entities
    }

    /// <summary>
    /// Picks the rule that applies to a line: those whose scoping matches, ranked by
    /// Priority then specificity (product beats category beats general), then name.
    /// </summary>
    private static CommissionRule? BestRule(IEnumerable<CommissionRule> rules,
        Guid salesPersonId, SaleItem item, DateTime saleDate)
    {
        var category = item.Product?.Category;
        return rules
            .Where(r => (r.SalesPersonId == null || r.SalesPersonId == salesPersonId)
                     && (r.ProductId == null || r.ProductId == item.ProductId)
                     && (r.Category == null || string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
                     && (!r.EffectiveFrom.HasValue || saleDate.Date >= r.EffectiveFrom.Value.Date)
                     && (!r.EffectiveTo.HasValue   || saleDate.Date <= r.EffectiveTo.Value.Date))
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => Specificity(r))
            .ThenBy(r => r.Name)
            .FirstOrDefault();
    }

    private static int Specificity(CommissionRule r)
        => (r.ProductId != null ? 4 : 0) + (r.Category != null ? 2 : 0) + (r.SalesPersonId != null ? 1 : 0);

    // ── Payout (own transaction) ──

    public async Task<ServiceResult> PayoutAsync(PayoutCommissionDto dto, string user)
    {
        var person = await _people.GetByIdAsync(dto.SalesPersonId);
        if (person == null) return ServiceResult.Fail("Sales person not found.");

        var unpaid = await _accruals.GetUnpaidBySalesPersonAsync(dto.SalesPersonId);
        if (unpaid.Count == 0)
            return ServiceResult.Fail($"{person.Name} has no unpaid commission to pay out.");

        var total = unpaid.Sum(a => a.Amount);
        var payout = new CommissionPayout {
            Id = Guid.NewGuid(), SalesPersonId = dto.SalesPersonId,
            PayoutDate = DateTime.UtcNow, Amount = total,
            Notes = Trim(dto.Notes, 500), CreatedBy = user
        };
        await _payouts.AddAsync(payout);

        foreach (var a in unpaid) a.CommissionPayoutId = payout.Id;   // tracked mutation

        await _audit.LogAsync(user, "Commission.Payout",
            $"{person.Name}: {total:N0} across {unpaid.Count} accrual(s)");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ── Rule CRUD ──

    public async Task<List<CommissionRuleDto>> GetRulesAsync(bool activeOnly = false)
    {
        var list = await _rules.GetAllAsync(activeOnly);
        var dtos = new List<CommissionRuleDto>(list.Count);
        foreach (var r in list) dtos.Add(MapRule(r, await _rules.IsInUseAsync(r.Id)));
        return dtos;
    }

    public async Task<CommissionRuleDto?> GetRuleAsync(Guid id)
    {
        var r = await _rules.GetByIdAsync(id);
        return r == null ? null : MapRule(r, await _rules.IsInUseAsync(r.Id));
    }

    public async Task<ServiceResult> CreateRuleAsync(CommissionRuleDto dto, string user)
    {
        var invalid = await ValidateRuleAsync(dto, null);
        if (invalid != null) return invalid;
        var rule = new CommissionRule { Id = Guid.NewGuid() };
        ApplyRule(rule, dto);
        await _rules.AddAsync(rule);
        await _audit.LogAsync(user, "CommissionRule.Create", $"{rule.Name} ({rule.Rate:0.##}%)");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateRuleAsync(CommissionRuleDto dto, string user)
    {
        var rule = await _rules.GetByIdAsync(dto.Id);
        if (rule == null) return ServiceResult.Fail("Commission rule not found.");
        var invalid = await ValidateRuleAsync(dto, dto.Id);
        if (invalid != null) return invalid;
        ApplyRule(rule, dto);
        _rules.Update(rule);
        await _audit.LogAsync(user, "CommissionRule.Update", $"{rule.Name} ({rule.Rate:0.##}%)");
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteRuleAsync(Guid id, string user)
    {
        var rule = await _rules.GetByIdAsync(id);
        if (rule == null) return ServiceResult.Fail("Commission rule not found.");
        if (await _rules.IsInUseAsync(id))
            return ServiceResult.Fail(
                $"'{rule.Name}' has already accrued commission and cannot be deleted. " +
                "Set it inactive instead — it stops applying to new collections while " +
                "existing accruals keep their link to it.");
        _rules.Remove(rule);
        await _audit.LogAsync(user, "CommissionRule.Delete", rule.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateRuleAsync(CommissionRuleDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Rule name is required.");
        if (!(dto.Rate > 0) || dto.Rate >= 100)  return ServiceResult.Fail("Rate must be between 0 and 100 percent.");
        if (await _rules.NameExistsAsync(dto.Name.Trim(), excludeId))
            return ServiceResult.Fail($"A commission rule named '{dto.Name.Trim()}' already exists.");
        if (dto.SalesPersonId.HasValue && await _people.GetByIdAsync(dto.SalesPersonId.Value) == null)
            return ServiceResult.Fail("Sales person not found.");
        if (dto.ProductId.HasValue && await _products.GetByIdAsync(dto.ProductId.Value) == null)
            return ServiceResult.Fail("Product not found.");
        if (dto.EffectiveFrom.HasValue && dto.EffectiveTo.HasValue && dto.EffectiveTo < dto.EffectiveFrom)
            return ServiceResult.Fail("Effective end cannot be before effective start.");
        return null;
    }

    private static void ApplyRule(CommissionRule rule, CommissionRuleDto dto)
    {
        rule.Name          = dto.Name.Trim();
        rule.SalesPersonId = dto.SalesPersonId;
        rule.ProductId     = dto.ProductId;
        rule.Category      = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        rule.Rate          = dto.Rate;
        rule.Priority      = dto.Priority;
        rule.EffectiveFrom = dto.EffectiveFrom;
        rule.EffectiveTo   = dto.EffectiveTo;
        rule.IsActive      = dto.IsActive;
    }

    // ── Queries ──

    public async Task<List<CommissionAccrualDto>> GetAccrualsAsync(Guid? salesPersonId = null, bool? unpaidOnly = null)
        => (await _accruals.GetAllAsync(salesPersonId, unpaidOnly)).Select(MapAccrual).ToList();

    public async Task<List<CommissionAccrualDto>> GetAccrualsForSaleAsync(Guid saleId)
        => (await _accruals.GetBySaleAsync(saleId)).Select(MapAccrual).ToList();

    public async Task<List<CommissionUnpaidDto>> GetUnpaidSummaryAsync()
        => (await _accruals.GetUnpaidSummaryAsync()).Select(s => new CommissionUnpaidDto {
            SalesPersonId = s.SalesPersonId, SalesPersonName = s.SalesPersonName,
            AccrualCount = s.AccrualCount, Amount = s.Amount
        }).ToList();

    public async Task<List<CommissionPayoutDto>> GetPayoutsAsync(Guid? salesPersonId = null)
        => (await _payouts.GetAllAsync(salesPersonId)).Select(p => new CommissionPayoutDto {
            Id = p.Id, PayoutDate = p.PayoutDate, SalesPersonName = p.SalesPerson?.Name ?? "",
            Amount = p.Amount, Notes = p.Notes, AccrualCount = p.Accruals?.Count ?? 0, CreatedBy = p.CreatedBy
        }).ToList();

    private static string? Trim(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    private static CommissionRuleDto MapRule(CommissionRule r, bool inUse) => new() {
        Id = r.Id, Name = r.Name, SalesPersonId = r.SalesPersonId,
        SalesPersonName = r.SalesPerson?.Name ?? "", ProductId = r.ProductId,
        ProductName = r.Product?.Name ?? "", Category = r.Category, Rate = r.Rate,
        Priority = r.Priority, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo,
        IsActive = r.IsActive, InUse = inUse
    };

    private static CommissionAccrualDto MapAccrual(CommissionAccrual a) => new() {
        Id = a.Id, AccrualDate = a.AccrualDate, SalesPersonName = a.SalesPerson?.Name ?? "",
        SalesPersonId = a.SalesPersonId, RuleName = a.Rule?.Name ?? "",
        InvoiceNumber = a.Sale?.InvoiceNumber ?? "", SaleId = a.SaleId,
        ProductName = a.SaleItem?.Product?.Name ?? "", BaseAmount = a.BaseAmount,
        Rate = a.Rate, Amount = a.Amount,
        IsPaid = a.CommissionPayoutId != null, IsVoided = a.IsVoided
    };
}
