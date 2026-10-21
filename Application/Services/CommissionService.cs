using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// The commission engine: accrues commission for a salesperson as revenue is collected,
/// and settles it in payouts. Mirrors RebateService's accrual/settlement shape.
///
/// Commission is a percentage of the **tax-inclusive invoice total** (GrandTotal), not
/// gross margin and not ex-PPN revenue, earned **on collection** — never at invoice time.
/// A cash sale collects in full at creation; a credit sale accrues in slices as each
/// payment arrives, so commission is never paid on debt that isn't collected. Accruals
/// are per SaleItem so a product- or category-specific rate needs no redesign.
///
/// The basis changed from TaxBase to GrandTotal on 2026-08-05 (HC's decision). It means
/// commission is paid on the PPN portion as well — money collected on the government's
/// behalf rather than earned — which is a deliberate trade for a figure staff can check
/// against the number printed on the invoice. Raised explicitly before the change; see
/// decisions.md. Rates still come from CommissionRule, unchanged.
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

    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<CommissionService> _log;
    public CommissionService(ICommissionRuleRepository rules, ICommissionAccrualRepository accruals,
        ICommissionPayoutRepository payouts, ISalesPersonRepository people,
        IProductRepository products, IAuditLogRepository audit, IUnitOfWork uow,
        IStringLocalizer<SharedResource> loc, ILogger<CommissionService> log)
    { _rules=rules; _accruals=accruals; _payouts=payouts; _people=people;
      _products=products; _audit=audit; _uow=uow;  _loc = loc; _log = log; }

    // ── Accrual (called inside the SaleService transaction — no SaveChanges) ──

    /// <summary>Commission on a cash sale — the whole invoice is collected at creation.</summary>
    public Task AccrueForCashSaleAsync(Sale sale, IReadOnlyList<SaleItem> items)
        => AccrueAsync(sale, items, sale.GrandTotal, paymentRecordId: null);

    /// <summary>
    /// Commission on a credit payment — prorated to the fraction of the invoice it collects.
    /// <paramref name="cashCollected"/> is the cash in it when part of the payment was funded
    /// by credit notes in a settlement (that part earns nothing: G7, HC 2026-10-14).
    /// </summary>
    public Task AccrueForPaymentAsync(Sale sale, PaymentRecord payment, decimal? cashCollected = null)
        // Dated when the money arrived, not when it was typed in (HC, 2026-10-21).
        => AccrueAsync(sale, sale.SaleItems.ToList(), cashCollected ?? payment.Amount, payment.Id, payment.PaymentDate);

    private async Task AccrueAsync(Sale sale, IReadOnlyList<SaleItem> items,
        decimal collectedAmount, Guid? paymentRecordId, DateTime? accrualDate = null)
    {
        if (sale.SalesPersonId == null) return;              // unattributed — no commission
        if (sale.GrandTotal <= 0 || collectedAmount <= 0) return;

        var rules = await _rules.GetActiveForSalesPersonAsync(sale.SalesPersonId.Value);
        if (rules.Count == 0) return;

        // Each line's share of the tax-inclusive invoice total, prorated by its
        // net-of-discount line total. Deliberately GrandTotal rather than TaxBase: the
        // commission base is what the customer was actually billed, so 0.5% of a
        // 1,110,000 invoice is 5,550 — a figure that ties to the printed document.
        var totalNet = items.Sum(i => i.LineTotal - i.AllocatedInvoiceDiscount);
        if (totalNet <= 0m) return;

        var collectedFraction = collectedAmount / sale.GrandTotal;

        foreach (var item in items)
        {
            var rule = BestRule(rules, sale.SalesPersonId.Value, item, sale.SaleDate);
            if (rule == null) continue;

            var itemRevenue = sale.GrandTotal * ((item.LineTotal - item.AllocatedInvoiceDiscount) / totalNet);
            var baseAmount  = Math.Round(itemRevenue * collectedFraction, 2, MidpointRounding.AwayFromZero);
            var amount      = Math.Round(baseAmount * rule.Rate / 100m, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m) continue;

            await _accruals.AddAsync(new CommissionAccrual {
                Id = Guid.NewGuid(), SaleId = sale.Id, SaleItemId = item.Id,
                PaymentRecordId = paymentRecordId, SalesPersonId = sale.SalesPersonId.Value,
                CommissionRuleId = rule.Id, BaseAmount = baseAmount, Rate = rule.Rate,
                Amount = amount, AccrualDate = accrualDate ?? DateTime.UtcNow
            });
        }
    }

    /// <summary>True when any of this sale's commission has already been paid to the salesperson.</summary>
    public async Task<bool> HasPaidOutForSaleAsync(Guid saleId) =>
        (await _accruals.GetBySaleAsync(saleId)).Any(a => a.CommissionPayoutId != null && !a.IsVoided && a.Amount > 0);

    /// <summary>Voids (never deletes) a sale's unpaid accruals on cancellation. No SaveChanges.</summary>
    public async Task VoidForSaleAsync(Guid saleId)
    {
        var list = await _accruals.GetBySaleAsync(saleId);
        foreach (var a in list.Where(a => a.CommissionPayoutId == null && !a.IsVoided))
            a.IsVoided = true;   // tracked mutation — no Update() on possibly-Added entities
    }

    /// <summary>
    /// The salespeople with commission on this sale. A caller that is about to change what is
    /// paid or unpaid locks them (IUnitOfWork.LockAsync) so a payout for the same person waits
    /// instead of settling rows mid-change; PayoutAsync takes the same lock.
    /// </summary>
    public async Task<Guid[]> SalesPeopleForSaleAsync(Guid saleId) =>
        (await _accruals.GetBySaleAsync(saleId)).Select(a => a.SalesPersonId).Distinct().ToArray();

    /// <summary>
    /// A cancelled sale keeps no commission (HC, 2026-10-18). VoidForSaleAsync voids what is
    /// still unpaid; what was already paid out can't be touched, so this adds one negative row
    /// per line, salesperson and rule for the net paid amount. It starts unpaid, so it comes
    /// off the salesperson's next payout, the same way a return's clawback does. Before this a
    /// cancel kept the paid part with the salesperson and nothing recorded it was owed back.
    /// Call after VoidForSaleAsync, inside the cancel's transaction, with the salespeople
    /// locked. No SaveChanges.
    /// </summary>
    public async Task ClawBackPaidForCancelledSaleAsync(Guid saleId, string user)
    {
        var paid = (await _accruals.GetBySaleAsync(saleId))
            .Where(a => !a.IsVoided && a.CommissionPayoutId != null)
            .GroupBy(a => (a.SaleItemId, a.SalesPersonId, a.CommissionRuleId));
        decimal total = 0m; var rows = 0;
        foreach (var g in paid)
        {
            var net = g.Sum(a => a.Amount);
            if (net == 0m) continue;
            var first = g.First();
            await _accruals.AddAsync(new CommissionAccrual {
                Id = Guid.NewGuid(), SaleId = saleId, SaleItemId = g.Key.SaleItemId, PaymentRecordId = null,
                SalesPersonId = g.Key.SalesPersonId, CommissionRuleId = g.Key.CommissionRuleId,
                BaseAmount = -g.Sum(a => a.BaseAmount), Rate = first.Rate, Amount = -net,
                AccrualDate = DateTime.UtcNow
            });
            total += net; rows++;
        }
        if (rows == 0) return;
        await _audit.LogAsync(user, "Commission.ClawBackForCancel", $"sale {saleId}: {total:N0} already paid, {rows} row(s)");
        _log.LogInformation(
            "Commission already paid on cancelled sale {SaleId} clawed back: {Amount} in {Rows} row(s), by {User}",
            saleId, total, rows, user);
    }

    /// <summary>
    /// Claws back commission on a customer return (HC, 2026-07-31 — the salesperson doesn't
    /// keep commission on revenue that came back).
    ///
    /// Never edits or voids the original accrual rows — a customer return can happen long
    /// after a commission has already been paid out, and a paid row can't be touched. Instead
    /// adds one negative reversal accrual per original (positive) accrual on the returned
    /// line, sized to the SAME fraction of the line the return represents
    /// (<c>creditAmount / netLineTotal</c> — exactly what <see cref="ReturnService"/> already
    /// computed for the credit note). The reversal starts unpaid, so it nets off the
    /// salesperson's next payout rather than silently forgiving what was already paid.
    ///
    /// The basis for each reversal is deliberately the ORIGINAL accrual's own amount, not
    /// the line's current running total — that keeps repeated partial returns of the same
    /// line correct without double-clawing: two returns each taking back 30% of a line
    /// correctly claw back 30% + 30% = 60% of what was originally earned, not 30% of a
    /// shrinking remainder. "Original" = a positive row with no return: a reversal written when
    /// an earlier return was cancelled is positive too, and must not raise the basis. Each
    /// clawback carries its return's id so cancelling that return can undo exactly it.
    /// No SaveChanges — runs inside ReturnService's transaction.
    /// </summary>
    public async Task ClawBackForReturnAsync(Guid saleId, Guid returnId,
        IReadOnlyDictionary<Guid, (decimal CreditAmount, decimal NetLineTotal)> returnedLines, string user)
    {
        if (returnedLines.Count == 0) return;

        var accruals = await _accruals.GetBySaleAsync(saleId);
        var clawedBack = 0;
        // Earnings whose payment was reversed after being paid out are already being clawed
        // back by that reversal; counting them here would take the same commission twice.
        var reversed = ReversedPaymentIds(accruals);

        foreach (var group in accruals.Where(a => !a.IsVoided && !(a.PaymentRecordId is { } pid && reversed.Contains(pid))).GroupBy(a => a.SaleItemId))
        {
            if (!returnedLines.TryGetValue(group.Key, out var ret) || ret.NetLineTotal <= 0m) continue;
            var fraction = ret.CreditAmount / ret.NetLineTotal;
            if (fraction <= 0m) continue;

            foreach (var original in group.Where(a => a.Amount > 0m && a.CustomerReturnId == null))
            {
                var clawback = Math.Round(original.Amount * fraction, 2, MidpointRounding.AwayFromZero);
                if (clawback <= 0m) continue;

                await _accruals.AddAsync(new CommissionAccrual {
                    Id = Guid.NewGuid(), SaleId = original.SaleId, SaleItemId = original.SaleItemId,
                    PaymentRecordId = null, SalesPersonId = original.SalesPersonId,
                    CommissionRuleId = original.CommissionRuleId,
                    BaseAmount = -Math.Round(original.BaseAmount * fraction, 2, MidpointRounding.AwayFromZero),
                    Rate = original.Rate, Amount = -clawback, AccrualDate = DateTime.UtcNow,
                    CustomerReturnId = returnId
                });
                clawedBack++;
            }
        }

        if (clawedBack > 0)
        {
            await _audit.LogAsync(user, "Commission.ClawBackForReturn",
                $"sale {saleId}: {clawedBack} accrual(s) reversed");
            // Same reasoning as the rebate clawback: this reduces someone's pay as a side
            // effect of a return, so it must be traceable when the figure is queried.
            _log.LogInformation(
                "Commission clawed back on sale {SaleId} — {ReversedCount} accrual(s) reversed, by {User}",
                saleId, clawedBack, user);
        }
    }

    /// <summary>
    /// True if one of these lines has a clawback written before clawbacks carried their return
    /// (2026-10-18), so it can't be told which return it belongs to. Such a return still can't be
    /// cancelled; on prod there were none (0 returns), so this only guards old test data.
    /// </summary>
    public async Task<bool> HasUnlinkedClawbackForSaleItemsAsync(Guid saleId, IEnumerable<Guid> saleItemIds)
    {
        var ids = saleItemIds.ToHashSet();
        var accruals = await _accruals.GetBySaleAsync(saleId);
        // A payment reversal's clawback carries its payment, so it is not one of those.
        return accruals.Any(a => ids.Contains(a.SaleItemId) && a.Amount < 0m && a.CustomerReturnId == null
                              && a.PaymentRecordId == null && !a.IsVoided);
    }

    /// <summary>Payments with a reversal clawback: a negative row carrying the payment's id.</summary>
    private static HashSet<Guid> ReversedPaymentIds(IEnumerable<CommissionAccrual> accruals) =>
        accruals.Where(a => a.Amount < 0m && a.PaymentRecordId != null && a.CustomerReturnId == null && !a.IsVoided)
                .Select(a => a.PaymentRecordId!.Value).ToHashSet();

    /// <summary>
    /// A payment was reversed (HC, 2026-10-21): the commission it earned goes. Rows still unpaid
    /// are voided; rows already paid out can't be touched, so one negative row per line,
    /// salesperson and rule takes the paid part off the next payout. The negative row carries
    /// the payment's id (PaymentRecordId), which is how a later return knows those earnings
    /// are already being taken back. Call inside the reversal's transaction with the
    /// salespeople locked. No SaveChanges.
    /// </summary>
    public async Task ReverseForPaymentAsync(Guid saleId, Guid paymentId, string user)
    {
        var rows = (await _accruals.GetBySaleAsync(saleId))
            .Where(a => a.PaymentRecordId == paymentId && !a.IsVoided && a.Amount > 0m).ToList();
        var voided = 0;
        foreach (var a in rows.Where(a => a.CommissionPayoutId == null)) { a.IsVoided = true; voided++; }

        decimal clawed = 0m; var added = 0;
        foreach (var g in rows.Where(a => a.CommissionPayoutId != null)
                              .GroupBy(a => (a.SaleItemId, a.SalesPersonId, a.CommissionRuleId)))
        {
            var paid = g.Sum(a => a.Amount);
            await _accruals.AddAsync(new CommissionAccrual {
                Id = Guid.NewGuid(), SaleId = saleId, SaleItemId = g.Key.SaleItemId, PaymentRecordId = paymentId,
                SalesPersonId = g.Key.SalesPersonId, CommissionRuleId = g.Key.CommissionRuleId,
                BaseAmount = -g.Sum(a => a.BaseAmount), Rate = g.First().Rate, Amount = -paid,
                AccrualDate = DateTime.UtcNow
            });
            clawed += paid; added++;
        }
        if (voided + added == 0) return;
        await _audit.LogAsync(user, "Commission.ReverseForPayment",
            $"sale {saleId} payment {paymentId}: {voided} unpaid row(s) voided, {clawed:N0} already paid clawed back in {added} row(s)");
        _log.LogInformation(
            "Commission on reversed payment {PaymentId} (sale {SaleId}): {Voided} unpaid row(s) voided, {Clawed} paid clawed back, by {User}",
            paymentId, saleId, voided, clawed, user);
    }

    /// <summary>
    /// Cancelling a return gives back the commission its clawback took (HC, 2026-10-18): a
    /// clawback still unpaid is voided; one already netted off a payout gets a positive row for
    /// the same amount, unpaid, so the next payout pays it back. Was: such a return could not be
    /// cancelled at all. Call inside the cancel's transaction with the salespeople locked.
    /// No SaveChanges.
    /// </summary>
    public async Task ReverseClawBackForReturnAsync(Guid saleId, Guid returnId, string user)
    {
        var clawbacks = (await _accruals.GetBySaleAsync(saleId))
            .Where(a => a.CustomerReturnId == returnId && a.Amount < 0m && !a.IsVoided).ToList();
        int voided = 0, reversed = 0;
        foreach (var c in clawbacks)
        {
            if (c.CommissionPayoutId == null) { c.IsVoided = true; voided++; continue; }   // tracked mutation
            await _accruals.AddAsync(new CommissionAccrual {
                Id = Guid.NewGuid(), SaleId = c.SaleId, SaleItemId = c.SaleItemId, PaymentRecordId = null,
                SalesPersonId = c.SalesPersonId, CommissionRuleId = c.CommissionRuleId,
                BaseAmount = -c.BaseAmount, Rate = c.Rate, Amount = -c.Amount,
                AccrualDate = DateTime.UtcNow, CustomerReturnId = returnId
            });
            reversed++;
        }
        if (voided + reversed == 0) return;
        await _audit.LogAsync(user, "Commission.ReverseClawBack",
            $"return {returnId}: {voided} unpaid clawback(s) voided, {reversed} paid one(s) reversed");
        _log.LogInformation(
            "Return {ReturnId} cancelled: commission clawback undone ({Voided} voided, {Reversed} reversed), by {User}",
            returnId, voided, reversed, user);
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
        // Same lock a sale or return cancel takes on its salespeople: one waits for the other,
        // so a row can't be voided as unpaid while this payout settles it (HC, 2026-10-18).
        await _uow.LockAsync(dto.SalesPersonId);
        var person = await _people.GetByIdAsync(dto.SalesPersonId);
        if (person == null) return _log.Refuse(_loc["Sales person not found."]);

        var unpaid = await _accruals.GetUnpaidBySalesPersonAsync(dto.SalesPersonId);
        if (unpaid.Count == 0)
            return _log.Refuse(_loc["{0} has no unpaid commission to pay out.", person.Name]);

        var total = unpaid.Sum(a => a.Amount);
        // Clawbacks can exceed new earnings; a payout of zero or less would record the
        // salesperson paying the shop. It waits until new commission covers it.
        if (total <= 0m)
            return _log.Refuse(_loc["{0}'s unpaid commission nets to {1}: clawbacks exceed what was earned, so there is nothing to pay. It carries to the next payout.", person.Name, total.ToString("N0")]);
        var payout = new CommissionPayout {
            Id = Guid.NewGuid(), SalesPersonId = dto.SalesPersonId,
            PayoutDate = DateTime.UtcNow, Amount = total,
            Notes = Trim(dto.Notes, 500), CreatedBy = user
        };
        await _payouts.AddAsync(payout);

        foreach (var a in unpaid) a.CommissionPayoutId = payout.Id;   // tracked mutation

        await _audit.LogAsync(user, "Commission.Payout",
            $"{person.Name}: {total:N0} across {unpaid.Count} accrual(s)");
        _log.LogInformation(
            "Commission paid out to {SalesPerson} — {Amount} across {AccrualCount} accrual(s), by {User}",
            person.Name, total, unpaid.Count, user);
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
        if (rule == null) return _log.Refuse(_loc["Commission rule not found."]);
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
        if (rule == null) return _log.Refuse(_loc["Commission rule not found."]);
        if (await _rules.IsInUseAsync(id))
            return _log.Refuse(_loc["'{0}' has already accrued commission and cannot be deleted. Set it inactive instead — it stops applying to new collections while existing accruals keep their link to it.", rule.Name]);
        _rules.Remove(rule);
        await _audit.LogAsync(user, "CommissionRule.Delete", rule.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateRuleAsync(CommissionRuleDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return _log.Refuse(_loc["Rule name is required."]);
        if (!(dto.Rate > 0) || dto.Rate >= 100)  return _log.Refuse(_loc["Rate must be between 0 and 100 percent."]);
        if (await _rules.NameExistsAsync(dto.Name.Trim(), excludeId))
            return _log.Refuse(_loc["A commission rule named '{0}' already exists.", dto.Name.Trim()]);
        if (dto.SalesPersonId.HasValue && await _people.GetByIdAsync(dto.SalesPersonId.Value) == null)
            return _log.Refuse(_loc["Sales person not found."]);
        if (dto.ProductId.HasValue && await _products.GetByIdAsync(dto.ProductId.Value) == null)
            return _log.Refuse(_loc["Product not found."]);
        if (dto.EffectiveFrom.HasValue && dto.EffectiveTo.HasValue && dto.EffectiveTo < dto.EffectiveFrom)
            return _log.Refuse(_loc["Effective end cannot be before effective start."]);
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
