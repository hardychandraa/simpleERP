using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// The rebate engine: evaluates supplier rebate rules when purchases are posted and
/// paid (accrual), and settles them when the supplier reconciles (realization).
///
/// ── Accrual arithmetic (interpretation, pending HC/consultant confirmation) ──
/// The phase-plan fixes the entities and the trigger points but not the exact amount
/// for every condition×reward pairing. The rule applied here:
///   • The CONDITION gates whether a purchase/line/payment qualifies.
///   • The REWARD sets the amount — EXCEPT PriceDrop, whose amount is intrinsically
///     the price difference (ReferenceCost − UnitCost) × Qty and ignores reward fields.
///   • Per-line rewards (PercentDiscount, and PriceDrop) accrue once per qualifying
///     line. Flat/goods rewards (FixedCash, CreditNote, InKindGoods, LuckyDraw) accrue
///     once per qualifying (rule, purchase) so a flat reward isn't multiplied by line
///     count.
///   • Volume thresholds are checked per product (cumulative period qty of that
///     product from that supplier, including the current purchase). A supplier-wide
///     rule (ProductId null) still checks each line against its own product's total.
/// These are documented so they can be corrected against a real supplier reconciliation
/// sheet without reshaping the schema.
/// </summary>
public class RebateService : IRebateService
{
    private readonly IRebateRuleRepository        _rules;
    private readonly IRebateAccrualRepository     _accruals;
    private readonly IRebateRealizationRepository _realizations;
    private readonly IPurchaseRepository          _purchases;
    private readonly ISupplierRepository          _suppliers;
    private readonly IProductRepository           _products;
    private readonly IBranchRepository            _branches;
    private readonly IAppSettingsRepository       _settings;
    private readonly IAuditLogRepository          _audit;
    private readonly InventoryService             _inventory;
    private readonly IUnitOfWork                  _uow;

    public RebateService(IRebateRuleRepository rules, IRebateAccrualRepository accruals,
        IRebateRealizationRepository realizations, IPurchaseRepository purchases,
        ISupplierRepository suppliers, IProductRepository products, IBranchRepository branches,
        IAppSettingsRepository settings, IAuditLogRepository audit,
        InventoryService inventory, IUnitOfWork uow)
    { _rules=rules; _accruals=accruals; _realizations=realizations; _purchases=purchases;
      _suppliers=suppliers; _products=products; _branches=branches; _settings=settings;
      _audit=audit; _inventory=inventory; _uow=uow; }

    // ── Evaluation (called inside the PurchaseService transaction — no SaveChanges) ──

    /// <summary>
    /// Accrues Volume and PriceDrop rebates for a just-built purchase. OnTimePayment is
    /// not evaluated here — it depends on payment timing, handled at payment.
    /// </summary>
    public async Task EvaluateOnPurchaseAsync(Purchase purchase, IReadOnlyList<PurchaseItem> items)
    {
        var rules = await _rules.GetActiveForSupplierAsync(purchase.SupplierId);
        if (rules.Count == 0) return;

        // This purchase's own qty per product, so a product spread over two lines counts once.
        var thisPurchaseQtyByProduct = items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Qty));

        foreach (var rule in rules)
        {
            if (!WithinPeriod(rule, purchase.PurchaseDate)) continue;

            switch (rule.ConditionType)
            {
                case RebateConditionType.Volume:
                    await AccrueVolumeAsync(rule, purchase, items, thisPurchaseQtyByProduct);
                    break;
                case RebateConditionType.PriceDrop:
                    AccruePriceDrop(rule, purchase, items);
                    break;
                // OnTimePayment: evaluated at payment time, not here.
            }
        }
    }

    private async Task AccrueVolumeAsync(RebateRule rule, Purchase purchase,
        IReadOnlyList<PurchaseItem> items, Dictionary<Guid, decimal> thisPurchaseQtyByProduct)
    {
        // Lines this rule looks at: the specific product, or all of them.
        var lines = items.Where(i => rule.ProductId == null || i.ProductId == rule.ProductId).ToList();
        if (lines.Count == 0) return;

        // Which lines clear the threshold. Cumulative = prior period qty (from the DB,
        // excluding this not-yet-saved purchase) + this purchase's qty for the product.
        var qualifyingLines = new List<PurchaseItem>();
        foreach (var line in lines)
        {
            var prior = await _purchases.GetPurchasedQtyAsync(
                purchase.SupplierId, line.ProductId, rule.PeriodStart, rule.PeriodEnd);
            var cumulative = prior + thisPurchaseQtyByProduct.GetValueOrDefault(line.ProductId, 0m);

            var qtyOk   = !rule.ThresholdQty.HasValue   || cumulative >= rule.ThresholdQty.Value;
            var valueOk = !rule.ThresholdValue.HasValue || LineNetExTax(line, purchase) >= rule.ThresholdValue.Value;
            if (qtyOk && valueOk) qualifyingLines.Add(line);
        }
        if (qualifyingLines.Count == 0) return;

        if (IsPerLineReward(rule.RewardType))
        {
            foreach (var line in qualifyingLines)
                await AddAccrualAsync(rule, purchase, line,
                    line.Qty, RewardAmountForLine(rule, line, purchase));
        }
        else
        {
            // Flat/goods reward: one accrual for the whole purchase.
            var qty = rule.RewardType == RebateRewardType.InKindGoods ? (rule.RewardQty ?? 0m)
                                                                       : qualifyingLines.Sum(l => l.Qty);
            await AddAccrualAsync(rule, purchase, null, qty, FlatRewardAmount(rule));
        }
    }

    private void AccruePriceDrop(RebateRule rule, Purchase purchase, IReadOnlyList<PurchaseItem> items)
    {
        if (!rule.ReferenceCost.HasValue) return;
        var lines = items.Where(i => (rule.ProductId == null || i.ProductId == rule.ProductId)
                                  && i.UnitCost < rule.ReferenceCost.Value);
        foreach (var line in lines)
        {
            // The reward is the drop itself, regardless of the rule's reward fields.
            var amount = Math.Round((rule.ReferenceCost!.Value - line.UnitCost) * line.Qty, 2, MidpointRounding.AwayFromZero);
            AddAccrual(rule, purchase, line, line.Qty, amount);
        }
    }

    /// <summary>
    /// Accrues and settles an OnTimePayment rebate the moment a qualifying payment is
    /// recorded — self-executing, per the plan. Cash rewards net through withholding
    /// immediately; in-kind pulls stock in; LuckyDraw is left outstanding (its value
    /// can't be known automatically). No SaveChanges — runs in the payment transaction.
    /// </summary>
    public async Task EvaluateOnPaymentAsync(Purchase purchase, DateTime paymentDate, string user)
    {
        // Only the first payment that clears the balance settles the term — evaluate
        // once, when the purchase becomes fully paid.
        if (purchase.AmountPaid < purchase.GrandTotal) return;

        var rules = (await _rules.GetActiveForSupplierAsync(purchase.SupplierId))
            .Where(r => r.ConditionType == RebateConditionType.OnTimePayment
                     && WithinPeriod(r, purchase.PurchaseDate))
            .ToList();
        if (rules.Count == 0 || !purchase.DueDate.HasValue) return;

        // Already earned an on-time rebate on this purchase? Don't double-count.
        var existing = await _accruals.GetByPurchaseAsync(purchase.Id);

        foreach (var rule in rules)
        {
            if (existing.Any(a => a.RebateRuleId == rule.Id && !a.IsVoided)) continue;

            var graceDays = rule.OnTimePaymentDays ?? 0;
            var deadline  = purchase.DueDate.Value.Date.AddDays(graceDays);
            if (paymentDate.Date > deadline) continue;   // paid late — no reward

            var settings = await _settings.GetAsync();

            if (rule.RewardType == RebateRewardType.InKindGoods && rule.RewardProductId.HasValue)
            {
                var accrual = BuildAccrual(rule, purchase, null, rule.RewardQty ?? 0m, 0m);
                await _accruals.AddAsync(accrual);
                await RealizeInKindCore(accrual, rule.RewardProductId.Value, rule.RewardQty ?? 0m,
                    purchase.SupplierId, null, "Auto: on-time payment", user);
            }
            else if (rule.RewardType == RebateRewardType.LuckyDraw)
            {
                // Can't fabricate a value — accrue outstanding, settle later by hand.
                await _accruals.AddAsync(BuildAccrual(rule, purchase, null, 0m, 0m));
            }
            else
            {
                var gross   = FlatOrPercentRewardForPurchase(rule, purchase);
                var accrual = BuildAccrual(rule, purchase, null, 1m, gross);
                await _accruals.AddAsync(accrual);
                await RealizeCashCore(new[] { accrual }, purchase.SupplierId, rule.RewardType,
                    gross, settings.RebateWithholdingRate, null, "Auto: on-time payment", user);
            }
        }
    }

    /// <summary>
    /// Voids (never deletes) a purchase's outstanding accruals when it's cancelled.
    /// Already-settled accruals are left alone — that money changed hands. No SaveChanges.
    /// </summary>
    public async Task VoidAccrualsForPurchaseAsync(Guid purchaseId, string user)
    {
        var list = await _accruals.GetByPurchaseAsync(purchaseId);
        foreach (var a in list.Where(a => a.RebateRealizationId == null && !a.IsVoided))
        {
            a.IsVoided = true;
            _accruals.Update(a);
        }
    }

    // ── Realization (own transaction) ──

    public async Task<ServiceResult> RealizeCashAsync(RealizeCashDto dto, string user)
    {
        if (dto.GrossAmount <= 0) return ServiceResult.Fail("Gross amount must be greater than zero.");
        var supplier = await _suppliers.GetByIdAsync(dto.SupplierId);
        if (supplier == null) return ServiceResult.Fail("Supplier not found.");

        // Settle all outstanding cash accruals for the supplier as one lump — the shape
        // the supplier reconciliation sheet actually uses (many accruals, one payment).
        var outstanding = (await _accruals.GetOutstandingBySupplierAsync(dto.SupplierId))
            .Where(a => a.RewardType != RebateRewardType.InKindGoods
                     && a.RewardType != RebateRewardType.LuckyDraw)
            .ToList();
        if (outstanding.Count == 0)
            return ServiceResult.Fail("This supplier has no outstanding cash rebates to settle.");

        var settings = await _settings.GetAsync();
        await RealizeCashCore(outstanding, dto.SupplierId, RebateRewardType.CreditNote,
            dto.GrossAmount, settings.RebateWithholdingRate,
            Trim(dto.ReferenceId, 100), Trim(dto.Notes, 500), user);

        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RealizeLuckyDrawAsync(RealizeLuckyDrawDto dto, string user)
    {
        if (dto.GrossAmount <= 0) return ServiceResult.Fail("Settled amount must be greater than zero.");
        var accrual = await _accruals.GetByIdAsync(dto.AccrualId);
        if (accrual == null) return ServiceResult.Fail("Accrual not found.");
        if (accrual.RewardType != RebateRewardType.LuckyDraw)
            return ServiceResult.Fail("That accrual is not a Lucky Draw.");
        if (accrual.RebateRealizationId != null || accrual.IsVoided)
            return ServiceResult.Fail("That accrual is already settled or voided.");

        var settings = await _settings.GetAsync();
        // The value is only known now — record it on the accrual too, so effective-cost
        // reporting has a real number.
        accrual.Amount = dto.GrossAmount;
        _accruals.Update(accrual);

        await RealizeCashCore(new[] { accrual }, accrual.SupplierId, RebateRewardType.LuckyDraw,
            dto.GrossAmount, settings.RebateWithholdingRate,
            Trim(dto.ReferenceId, 100), Trim(dto.Notes, 500), user);

        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RealizeInKindAsync(RealizeInKindDto dto, string user)
    {
        var accrual = await _accruals.GetByIdAsync(dto.AccrualId);
        if (accrual == null) return ServiceResult.Fail("Accrual not found.");
        if (accrual.RewardType != RebateRewardType.InKindGoods)
            return ServiceResult.Fail("That accrual is not an in-kind reward.");
        if (accrual.RebateRealizationId != null || accrual.IsVoided)
            return ServiceResult.Fail("That accrual is already settled or voided.");

        var rule = await _rules.GetByIdAsync(accrual.RebateRuleId);
        if (rule?.RewardProductId == null)
            return ServiceResult.Fail("The rebate rule has no reward product to receive.");

        await RealizeInKindCore(accrual, rule.RewardProductId.Value, accrual.Qty,
            accrual.SupplierId, Trim(dto.ReferenceId, 100), Trim(dto.Notes, 500), user);

        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ── Realization cores (no SaveChanges — shared by manual + auto paths) ──

    private async Task RealizeCashCore(IReadOnlyCollection<RebateAccrual> accruals, Guid supplierId,
        RebateRewardType rewardType, decimal gross, decimal withholdingRate,
        string? reference, string? notes, string user)
    {
        // Net by subtraction so gross = withholding + net exactly, at any rate.
        var withholding = Math.Round(gross * withholdingRate, 2, MidpointRounding.AwayFromZero);
        var net = gross - withholding;

        var realization = new RebateRealization {
            Id = Guid.NewGuid(), SupplierId = supplierId, RealizationDate = DateTime.UtcNow,
            RewardType = rewardType, GrossAmount = gross,
            WithholdingRate = withholdingRate, WithholdingAmount = withholding, NetAmount = net,
            ReferenceId = reference, Notes = notes, CreatedBy = user
        };
        await _realizations.AddAsync(realization);

        // Mutating the tracked accruals is enough — EF persists the change on save.
        // Do NOT call Update(): these may be freshly-Added entities (the on-time-payment
        // path adds an accrual and settles it in the same unit of work), and Update()
        // would flip them to Modified and emit an UPDATE for a row that doesn't exist yet.
        foreach (var a in accruals)
            a.RebateRealizationId = realization.Id;

        await _audit.LogAsync(user, "Rebate.RealizeCash",
            $"{gross:N0} gross − {withholding:N0} WHT = {net:N0} net, {accruals.Count} accrual(s)");
    }

    private async Task RealizeInKindCore(RebateAccrual accrual, Guid productId, decimal qty,
        Guid supplierId, string? reference, string? notes, string user)
    {
        var branch = await _branches.GetDefaultAsync()
            ?? throw new InvalidOperationException("Default branch not found.");

        var realization = new RebateRealization {
            Id = Guid.NewGuid(), SupplierId = supplierId, RealizationDate = DateTime.UtcNow,
            RewardType = RebateRewardType.InKindGoods,
            InKindProductId = productId, InKindQty = qty,
            ReferenceId = reference, Notes = notes, CreatedBy = user
        };
        await _realizations.AddAsync(realization);

        // Tracked mutation only — see RealizeCashCore for why Update() is avoided here.
        accrual.RebateRealizationId = realization.Id;

        if (qty > 0)
            await _inventory.StockInForRebateAsync(productId, qty, realization.Id, branch.Id);

        var product = await _products.GetByIdAsync(productId);
        await _audit.LogAsync(user, "Rebate.RealizeInKind", $"{qty:N0} x {product?.Name} received free");
    }

    // ── Accrual builders ──

    private async Task AddAccrualAsync(RebateRule rule, Purchase purchase, PurchaseItem? line,
        decimal qty, decimal amount)
        => await _accruals.AddAsync(BuildAccrual(rule, purchase, line, qty, amount));

    private void AddAccrual(RebateRule rule, Purchase purchase, PurchaseItem? line,
        decimal qty, decimal amount)
        => _accruals.AddAsync(BuildAccrual(rule, purchase, line, qty, amount));

    private static RebateAccrual BuildAccrual(RebateRule rule, Purchase purchase, PurchaseItem? line,
        decimal qty, decimal amount) => new() {
            Id = Guid.NewGuid(), RebateRuleId = rule.Id, SupplierId = purchase.SupplierId,
            PurchaseId = purchase.Id, PurchaseItemId = line?.Id,
            RewardType = rule.RewardType, Qty = qty, Amount = amount,
            AccrualDate = DateTime.UtcNow
        };

    // ── Reward-amount helpers ──

    private static bool IsPerLineReward(RebateRewardType t) => t == RebateRewardType.PercentDiscount;

    private static decimal RewardAmountForLine(RebateRule rule, PurchaseItem line, Purchase purchase)
        => rule.RewardType switch {
            RebateRewardType.PercentDiscount =>
                Math.Round(LineNetExTax(line, purchase) * (rule.RewardRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero),
            _ => 0m
        };

    private static decimal FlatRewardAmount(RebateRule rule) => rule.RewardType switch {
        RebateRewardType.FixedCash  => rule.RewardAmount ?? 0m,
        RebateRewardType.CreditNote => rule.RewardAmount ?? 0m,
        _ => 0m   // InKindGoods and LuckyDraw carry no cash value at accrual
    };

    private static decimal FlatOrPercentRewardForPurchase(RebateRule rule, Purchase purchase)
        => rule.RewardType switch {
            RebateRewardType.PercentDiscount =>
                Math.Round(purchase.TaxBase * (rule.RewardRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero),
            _ => rule.RewardAmount ?? 0m
        };

    /// <summary>
    /// A line's ex-PPN value net of both discounts — the base a percentage rebate is
    /// figured on. Mirrors the ex-tax costing PurchaseService uses for inventory.
    /// </summary>
    private static decimal LineNetExTax(PurchaseItem line, Purchase purchase)
    {
        var net = line.LineTotal - line.AllocatedInvoiceDiscount;
        if (purchase.IsTaxInclusive && purchase.TaxRate > 0m)
            net = Math.Round(net / (1m + purchase.TaxRate), 2, MidpointRounding.AwayFromZero);
        return net;
    }

    private static bool WithinPeriod(RebateRule rule, DateTime date)
        => (!rule.PeriodStart.HasValue || date.Date >= rule.PeriodStart.Value.Date)
        && (!rule.PeriodEnd.HasValue   || date.Date <= rule.PeriodEnd.Value.Date);

    private static string? Trim(string? s, int max)
        => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length > max ? s.Trim()[..max] : s.Trim());

    // ── Rule CRUD ──

    public async Task<List<RebateRuleDto>> GetRulesAsync(bool activeOnly = false)
    {
        var list = await _rules.GetAllAsync(activeOnly);
        var dtos = new List<RebateRuleDto>(list.Count);
        foreach (var r in list) dtos.Add(MapRule(r, await _rules.IsInUseAsync(r.Id)));
        return dtos;
    }

    public async Task<RebateRuleDto?> GetRuleAsync(Guid id)
    {
        var r = await _rules.GetByIdAsync(id);
        return r == null ? null : MapRule(r, await _rules.IsInUseAsync(r.Id));
    }

    public async Task<ServiceResult> CreateRuleAsync(RebateRuleDto dto, string user)
    {
        var invalid = await ValidateRuleAsync(dto, null);
        if (invalid != null) return invalid;

        var rule = new RebateRule { Id = Guid.NewGuid() };
        ApplyRule(rule, dto);
        await _rules.AddAsync(rule);
        await _audit.LogAsync(user, "RebateRule.Create", rule.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateRuleAsync(RebateRuleDto dto, string user)
    {
        var rule = await _rules.GetByIdAsync(dto.Id);
        if (rule == null) return ServiceResult.Fail("Rebate rule not found.");
        var invalid = await ValidateRuleAsync(dto, dto.Id);
        if (invalid != null) return invalid;

        ApplyRule(rule, dto);
        _rules.Update(rule);
        await _audit.LogAsync(user, "RebateRule.Update", rule.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteRuleAsync(Guid id, string user)
    {
        var rule = await _rules.GetByIdAsync(id);
        if (rule == null) return ServiceResult.Fail("Rebate rule not found.");
        if (await _rules.IsInUseAsync(id))
            return ServiceResult.Fail(
                $"'{rule.Name}' has already accrued rebates and cannot be deleted. " +
                "Set it inactive instead — it stops applying to new purchases while " +
                "existing accruals keep their link to it.");
        _rules.Remove(rule);
        await _audit.LogAsync(user, "RebateRule.Delete", rule.Name);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<ServiceResult?> ValidateRuleAsync(RebateRuleDto dto, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return ServiceResult.Fail("Rule name is required.");
        if (dto.SupplierId == Guid.Empty) return ServiceResult.Fail("Supplier is required.");
        if (await _rules.NameExistsAsync(dto.Name.Trim(), excludeId))
            return ServiceResult.Fail($"A rebate rule named '{dto.Name.Trim()}' already exists.");
        if (await _suppliers.GetByIdAsync(dto.SupplierId) == null)
            return ServiceResult.Fail("Supplier not found.");

        switch (dto.ConditionType)
        {
            case RebateConditionType.Volume:
                if (dto.ThresholdQty is < 0 || dto.ThresholdValue is < 0)
                    return ServiceResult.Fail("Volume thresholds cannot be negative.");
                break;
            case RebateConditionType.PriceDrop:
                if (!(dto.ReferenceCost > 0))
                    return ServiceResult.Fail("Price-drop rules need a reference cost greater than zero.");
                break;
            case RebateConditionType.OnTimePayment:
                if (dto.OnTimePaymentDays is < 0)
                    return ServiceResult.Fail("On-time grace days cannot be negative.");
                break;
        }

        switch (dto.RewardType)
        {
            case RebateRewardType.PercentDiscount:
                if (!(dto.RewardRate > 0) || dto.RewardRate >= 100)
                    return ServiceResult.Fail("Percent reward must be between 0 and 100.");
                break;
            case RebateRewardType.FixedCash:
            case RebateRewardType.CreditNote:
                if (!(dto.RewardAmount > 0))
                    return ServiceResult.Fail("Cash reward amount must be greater than zero.");
                break;
            case RebateRewardType.InKindGoods:
                if (dto.RewardProductId == null || !(dto.RewardQty > 0))
                    return ServiceResult.Fail("In-kind reward needs a product and a quantity.");
                if (await _products.GetByIdAsync(dto.RewardProductId.Value) == null)
                    return ServiceResult.Fail("Reward product not found.");
                break;
            // LuckyDraw needs no reward fields — value is unknown until settled.
        }

        if (dto.PeriodStart.HasValue && dto.PeriodEnd.HasValue && dto.PeriodEnd < dto.PeriodStart)
            return ServiceResult.Fail("Period end cannot be before period start.");
        return null;
    }

    private static void ApplyRule(RebateRule rule, RebateRuleDto dto)
    {
        rule.Name              = dto.Name.Trim();
        rule.SupplierId        = dto.SupplierId;
        rule.ProductId         = dto.ProductId;
        rule.ConditionType     = dto.ConditionType;
        rule.ThresholdQty      = dto.ConditionType == RebateConditionType.Volume ? dto.ThresholdQty : null;
        rule.ThresholdValue    = dto.ConditionType == RebateConditionType.Volume ? dto.ThresholdValue : null;
        rule.ReferenceCost     = dto.ConditionType == RebateConditionType.PriceDrop ? dto.ReferenceCost : null;
        rule.OnTimePaymentDays = dto.ConditionType == RebateConditionType.OnTimePayment ? dto.OnTimePaymentDays : null;
        rule.PeriodStart       = dto.PeriodStart;
        rule.PeriodEnd         = dto.PeriodEnd;
        rule.RewardType        = dto.RewardType;
        rule.RewardRate        = dto.RewardType == RebateRewardType.PercentDiscount ? dto.RewardRate : null;
        rule.RewardAmount      = dto.RewardType is RebateRewardType.FixedCash or RebateRewardType.CreditNote ? dto.RewardAmount : null;
        rule.RewardProductId   = dto.RewardType == RebateRewardType.InKindGoods ? dto.RewardProductId : null;
        rule.RewardQty         = dto.RewardType == RebateRewardType.InKindGoods ? dto.RewardQty : null;
        rule.IsActive          = dto.IsActive;
    }

    // ── Query mappers ──

    public async Task<List<RebateAccrualDto>> GetAccrualsAsync(Guid? supplierId = null, bool? outstandingOnly = null)
        => (await _accruals.GetAllAsync(supplierId, outstandingOnly)).Select(MapAccrual).ToList();

    public async Task<List<RebateAccrualDto>> GetAccrualsForPurchaseAsync(Guid purchaseId)
        => (await _accruals.GetByPurchaseAsync(purchaseId)).Select(MapAccrual).ToList();

    public async Task<List<RebateOutstandingDto>> GetOutstandingSummaryAsync()
        => (await _accruals.GetOutstandingSummaryAsync()).Select(s => new RebateOutstandingDto {
            SupplierId = s.SupplierId, SupplierName = s.SupplierName,
            CashAccrualCount = s.CashAccrualCount, CashAmount = s.CashAmount,
            InKindAccrualCount = s.InKindAccrualCount, LuckyDrawCount = s.LuckyDrawCount
        }).ToList();

    public async Task<List<RebateRealizationDto>> GetRealizationsAsync(Guid? supplierId = null)
        => (await _realizations.GetAllAsync(supplierId)).Select(r => new RebateRealizationDto {
            Id = r.Id, RealizationDate = r.RealizationDate, SupplierName = r.Supplier?.Name ?? "",
            RewardType = r.RewardType.ToString(), GrossAmount = r.GrossAmount,
            WithholdingRate = r.WithholdingRate, WithholdingAmount = r.WithholdingAmount, NetAmount = r.NetAmount,
            InKindProductName = r.InKindProduct?.Name ?? "", InKindQty = r.InKindQty,
            ReferenceId = r.ReferenceId, Notes = r.Notes, AccrualCount = r.Accruals?.Count ?? 0
        }).ToList();

    private static RebateRuleDto MapRule(RebateRule r, bool inUse) => new() {
        Id = r.Id, Name = r.Name, SupplierId = r.SupplierId, SupplierName = r.Supplier?.Name ?? "",
        ProductId = r.ProductId, ProductName = r.Product?.Name ?? "",
        ConditionType = r.ConditionType, ThresholdQty = r.ThresholdQty, ThresholdValue = r.ThresholdValue,
        ReferenceCost = r.ReferenceCost, OnTimePaymentDays = r.OnTimePaymentDays,
        PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd,
        RewardType = r.RewardType, RewardRate = r.RewardRate, RewardAmount = r.RewardAmount,
        RewardProductId = r.RewardProductId, RewardProductName = r.RewardProduct?.Name ?? "",
        RewardQty = r.RewardQty, IsActive = r.IsActive, InUse = inUse
    };

    private static RebateAccrualDto MapAccrual(RebateAccrual a) => new() {
        Id = a.Id, AccrualDate = a.AccrualDate, RuleName = a.Rule?.Name ?? "",
        SupplierName = a.Supplier?.Name ?? "", SupplierId = a.SupplierId,
        RewardType = a.RewardType.ToString(),
        PurchaseNumber = a.Purchase?.PurchaseNumber ?? "", PurchaseId = a.PurchaseId,
        Qty = a.Qty, Amount = a.Amount,
        IsSettled = a.RebateRealizationId != null, IsVoided = a.IsVoided
    };
}
