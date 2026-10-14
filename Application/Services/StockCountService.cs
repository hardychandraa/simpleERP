using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Resources;
using SimpleERP.Domain.Entities;
using SimpleERP.Domain.Enums;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

/// <summary>
/// End-of-day stock check (HC, 2026-10-14): the daily stock sheet, and stock counts (stock opname).
///
/// Staff count blind and submit; the system stock of each line is recorded at the instant of
/// submitting, and a line that doesn't match waits for Admin. Only Admin changes stock, by
/// posting the difference as an adjustment dated at the count moment, or explains it without
/// changing stock. Whole units only (HC, 2026-10-14).
///
/// The difference Admin posts is re-measured at decision time against the stock at the count
/// moment *as the ledger says it now*: a sale dated before the count but entered after it (the
/// forgotten invoice that usually explains a count difference) is already taken into account,
/// so posting can't take the same unit out twice.
/// </summary>
public class StockCountService : IStockCountService
{
    // A whole-catalogue count must fit on one document; the catalogue is ~60 products today.
    private const int MaxCountLines = 1000;

    private readonly IStockCountRepository      _counts;
    private readonly IInventoryLedgerRepository _ledger;
    private readonly IProductRepository         _products;
    private readonly IBranchRepository          _branches;
    private readonly IStockAdjustmentRepository _adjustments;
    private readonly IAuditLogRepository        _audit;
    private readonly IUnitOfWork                _uow;
    private readonly PeriodLock                 _period;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<StockCountService> _log;

    public StockCountService(IStockCountRepository counts, IInventoryLedgerRepository ledger,
        IProductRepository products, IBranchRepository branches, IStockAdjustmentRepository adjustments,
        IAuditLogRepository audit, IUnitOfWork uow, PeriodLock period,
        IStringLocalizer<SharedResource> loc, ILogger<StockCountService> log)
    {
        _counts = counts; _ledger = ledger; _products = products; _branches = branches;
        _adjustments = adjustments; _audit = audit; _uow = uow; _period = period; _loc = loc; _log = log;
    }

    private static DateTime DayStartUtc(DateTime localDay) =>
        DateTime.SpecifyKind(localDay.Date, DateTimeKind.Local).ToUniversalTime();

    private static readonly DateTime FarFuture = new(3000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Typed text as stored: control characters out, trimmed, null when empty, at most <paramref name="max"/> characters.</summary>
    private static string? Clean(string? s, int max)
    {
        s = TextClean.StripControl(s)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }

    // ── Daily stock sheet ────────────────────────────────────────────────────
    public async Task<DailyStockSheetDto> GetDailySheetAsync(DateTime localDay)
    {
        var from = DayStartUtc(localDay);
        var to   = DayStartUtc(localDay.AddDays(1));
        var opening = await _ledger.GetBalancesBeforeAsync(null, from);
        var moves   = await _ledger.GetStockCardAsync(null, from, to);
        var moved   = (await _ledger.GetProductsMovedAsync(null, from, to)).ToHashSet();

        var rows = new Dictionary<Guid, DailyStockRowDto>();
        foreach (var p in await _products.GetAllAsync())
            rows[p.Id] = new DailyStockRowDto {
                ProductId = p.Id, ProductName = p.Name, SKU = p.SKU, Category = p.Category, IsActive = p.IsActive,
                Opening = opening.GetValueOrDefault(p.Id), Moved = moved.Contains(p.Id) };

        foreach (var m in moves)
        {
            if (!rows.TryGetValue(m.ProductId, out var r)) continue;
            switch (m.ReferenceType)
            {
                case ReferenceType.Purchase: case ReferenceType.PurchaseOrder: case ReferenceType.RebateInKind:
                    r.InPurchase += m.QtyIn; r.OutOther += m.QtyOut; break;
                case ReferenceType.CustomerReturn:
                    r.InReturn += m.QtyIn; r.OutOther += m.QtyOut; break;
                case ReferenceType.Sale:
                    r.OutSale += m.QtyOut; r.InOther += m.QtyIn; break;
                case ReferenceType.SupplierReturn:
                    r.OutReturn += m.QtyOut; r.InOther += m.QtyIn; break;
                default:   // Cancel, Adjustment
                    r.InOther += m.QtyIn; r.OutOther += m.QtyOut; break;
            }
        }

        // The day's counts: the last submitted count of each product, and anything backdated past it.
        var lines = await _counts.GetSubmittedLinesAsync(from, to);
        foreach (var l in lines)   // oldest first, so the last one wins
            if (rows.TryGetValue(l.ProductId, out var r))
            {
                r.CountId = l.StockCountId; r.CountNumber = l.StockCount!.CountNumber;
                r.CountedQty = l.CountedQty; r.CountDecision = l.Decision;
                r.EnteredAfterCount.Clear();
            }
        foreach (var g in lines.GroupBy(l => l.StockCountId))
        {
            var count = g.First().StockCount!;
            var late = await LateEntriesAsync(count.SubmittedAt!.Value, g.Select(l => l.ProductId),
                                              g.Where(l => l.StockAdjustmentId != null).Select(l => l.StockAdjustmentId!.Value));
            foreach (var (productId, entries) in late)
                if (rows.TryGetValue(productId, out var r) && r.CountId == count.Id)
                    r.EnteredAfterCount = entries.Select(e => e.DocumentNumber).Distinct().ToList();
        }

        return new DailyStockSheetDto {
            Day  = localDay.Date,
            Rows = rows.Values
                .Where(r => r.IsActive || r.Opening != 0 || r.Moved)
                .OrderBy(r => r.ProductName).ToList() };
    }

    /// <summary>
    /// Ledger rows of these products dated before <paramref name="countAt"/> but written after it,
    /// leaving out the count's own adjustments, resolved to their documents.
    /// </summary>
    private async Task<Dictionary<Guid, List<LateEntryDto>>> LateEntriesAsync(
        DateTime countAt, IEnumerable<Guid> productIds, IEnumerable<Guid> ownAdjustments)
    {
        var own  = ownAdjustments.ToHashSet();
        var rows = (await _ledger.GetEnteredLateAsync(productIds, countAt, countAt))
                   .Where(e => !own.Contains(e.ReferenceId)).ToList();
        var refs = await _ledger.ResolveReferencesAsync(rows.Select(e => e.ReferenceId));
        return rows.GroupBy(e => e.ProductId).ToDictionary(g => g.Key, g => g.Select(e => {
            refs.TryGetValue(e.ReferenceId, out var r);
            return new LateEntryDto {
                DocumentNumber = r?.DocumentNumber is { Length: > 0 } n && n != "—" ? n : e.ReferenceType.ToString(),
                Link = r?.Link, ReferenceType = e.ReferenceType.ToString(),
                Qty = e.QtyIn - e.QtyOut, TransactionDate = e.TransactionDate, EnteredAt = e.EnteredAt };
        }).ToList());
    }

    // ── Counts: read ─────────────────────────────────────────────────────────
    public async Task<List<StockCountListDto>> GetListAsync(DateTime? fromLocal, DateTime? toLocal, StockCountStatus? status) =>
        (await _counts.GetListAsync(fromLocal is { } f ? DayStartUtc(f) : null,
                                    toLocal   is { } t ? DayStartUtc(t.AddDays(1)) : null, status))
        .Select(c => Fill(new StockCountListDto(), c)).ToList();

    public Task<int> CountAwaitingReviewAsync() => _counts.CountAwaitingReviewAsync();

    public async Task<StockCountDto?> GetAsync(Guid id, bool withReview)
    {
        var c = await _counts.GetByIdAsync(id);
        if (c == null) return null;
        var dto = Fill(new StockCountDto(), c);
        dto.Notes = c.Notes; dto.ClosedAt = c.ClosedAt; dto.ClosedBy = c.ClosedBy;
        dto.Lines = c.Lines.OrderBy(l => l.Product?.Name).Select(l => new StockCountLineDto {
            Id = l.Id, ProductId = l.ProductId, ProductName = l.Product?.Name ?? "", SKU = l.Product?.SKU ?? "",
            CountedQty = l.CountedQty, SystemQty = l.SystemQty, Note = l.Note, Decision = l.Decision,
            DecisionNote = l.DecisionNote, DecidedBy = l.DecidedBy, DecidedAt = l.DecidedAt,
            AdjustedQty = l.AdjustedQty }).ToList();

        if (withReview && c.Status == StockCountStatus.Submitted && c.SubmittedAt is { } at)
        {
            var pending = dto.Lines.Where(l => l.Decision == StockCountDecision.Pending).ToList();
            var late = await LateEntriesAsync(at, pending.Select(l => l.ProductId),
                c.Lines.Where(l => l.StockAdjustmentId != null).Select(l => l.StockAdjustmentId!.Value));
            foreach (var l in pending)
            {
                var stockAtCount = (await _ledger.GetBalancesBeforeAsync(l.ProductId, at)).GetValueOrDefault(l.ProductId);
                l.CurrentDifference = l.CountedQty!.Value - stockAtCount;
                l.EnteredAfterCount = late.GetValueOrDefault(l.ProductId) ?? new();
            }
        }
        return dto;
    }

    private static T Fill<T>(T d, StockCount c) where T : StockCountListDto
    {
        d.Id = c.Id; d.CountNumber = c.CountNumber; d.Scope = c.Scope; d.Status = c.Status;
        d.CreatedBy = c.CreatedBy; d.CreatedAt = c.CreatedAt; d.SubmittedAt = c.SubmittedAt; d.SubmittedBy = c.SubmittedBy;
        d.LineCount       = c.Lines.Count;
        d.DifferenceCount = c.Lines.Count(l => l.SystemQty.HasValue && l.CountedQty.HasValue && l.CountedQty.Value != l.SystemQty.Value);
        d.PendingCount    = c.Status == StockCountStatus.Submitted ? c.Lines.Count(l => l.Decision == StockCountDecision.Pending) : 0;
        return d;
    }

    // ── Counts: draft ────────────────────────────────────────────────────────
    public async Task<ServiceResult<Guid>> CreateAsync(StockCountScope scope, IEnumerable<Guid>? productIds, string user)
    {
        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<Guid>(_loc["Default branch not found."]);

        var all = await _products.GetAllAsync();
        List<Guid> ids;
        switch (scope)
        {
            case StockCountScope.MovedToday:
                var today = DateTime.Today;
                ids = await _ledger.GetProductsMovedAsync(null, DayStartUtc(today), DayStartUtc(today.AddDays(1)));
                if (ids.Count == 0)
                    return _log.Refuse<Guid>(_loc["No product has moved today. Count all products, or pick the products to count."]);
                break;
            case StockCountScope.All:
                // Active products, plus inactive ones still holding stock (they are on the shelf).
                var stock = await _ledger.GetBalancesBeforeAsync(null, FarFuture);
                ids = all.Where(p => p.IsActive || stock.GetValueOrDefault(p.Id) != 0).Select(p => p.Id).ToList();
                break;
            default:
                var known = all.Select(p => p.Id).ToHashSet();
                ids = (productIds ?? Enumerable.Empty<Guid>()).Where(known.Contains).Distinct().ToList();
                if (ids.Count == 0) return _log.Refuse<Guid>(_loc["Pick at least one product to count."]);
                break;
        }
        if (ids.Count > MaxCountLines)
            return _log.Refuse<Guid>(_loc["Too many lines in one document (at most {0}).", MaxCountLines]);

        var count = new StockCount {
            Id = Guid.NewGuid(), BranchId = branch.Id, Scope = scope, Status = StockCountStatus.Draft,
            CreatedBy = user, CreatedAt = DateTime.UtcNow,
            Lines = ids.Select(pid => new StockCountLine { Id = Guid.NewGuid(), ProductId = pid }).ToList() };
        await _counts.AddAsync(count);
        await _audit.LogAsync(user, "StockCount.Create", $"draft, {scope}, {ids.Count} product(s)");
        _log.LogInformation("Stock count draft {Id} started by {User}: {Scope}, {Lines} product(s)", count.Id, user, scope, ids.Count);
        await _uow.SaveChangesAsync();
        return ServiceResult<Guid>.Ok(count.Id);
    }

    /// <summary>
    /// Loads a draft for change, locked against a concurrent save or submit of the same count.
    /// A draft is its counter's (or Admin's): nobody else fills in or submits someone's count.
    /// </summary>
    private async Task<(StockCount? Count, ServiceResult? Refusal)> DraftForChangeAsync(Guid id, bool isAdmin, string user)
    {
        await _uow.LockAsync(id);
        var c = await _counts.GetByIdAsync(id);
        if (c == null) return (null, _log.Refuse(_loc["Stock count not found."], "StockCount"));
        if (c.Status != StockCountStatus.Draft)
            return (null, _log.Refuse(_loc["{0} is already submitted and can no longer be changed.", c.CountNumber ?? ""], "StockCount"));
        if (!isAdmin && c.CreatedBy != user)
            return (null, _log.Refuse(_loc["Only the person who started this count, or an administrator, can change it."], "StockCount"));
        return (c, null);
    }

    /// <summary>Copies the form's counts and notes onto the draft; null when they are all valid.</summary>
    private LocalizedString? Apply(StockCount c, IEnumerable<StockCountLineInput> inputs, string? notes)
    {
        var byId = c.Lines.ToDictionary(l => l.Id);
        foreach (var i in inputs)
        {
            if (!byId.TryGetValue(i.LineId, out var line)) continue;
            if (i.Qty is < 0) return _loc["{0}: the count cannot be negative.", line.Product?.Name ?? ""];
            if (i.Qty > Limits.MaxQty) return _loc["Quantity is too large (at most {0} per line).", Limits.MaxQty.ToString("N0")];
            line.CountedQty = i.Qty;
            line.Note = Clean(i.Note, 300);
        }
        c.Notes = Clean(notes, 500);
        return null;
    }

    public async Task<ServiceResult> SaveDraftAsync(Guid id, IEnumerable<StockCountLineInput> inputs, string? notes, bool isAdmin, string user)
    {
        var (c, refusal) = await DraftForChangeAsync(id, isAdmin, user);
        if (c == null) return refusal!;
        if (Apply(c, inputs, notes) is { } bad) return _log.Refuse(bad);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddProductsAsync(Guid id, IEnumerable<Guid> productIds, bool isAdmin, string user)
    {
        var (c, refusal) = await DraftForChangeAsync(id, isAdmin, user);
        if (c == null) return refusal!;
        var known = (await _products.GetAllAsync()).Select(p => p.Id).ToHashSet();
        var have  = c.Lines.Select(l => l.ProductId).ToHashSet();
        var add   = productIds.Where(p => known.Contains(p) && !have.Contains(p)).Distinct().ToList();
        if (add.Count == 0) return _log.Refuse(_loc["Those products are already on this count."]);
        if (c.Lines.Count + add.Count > MaxCountLines)
            return _log.Refuse(_loc["Too many lines in one document (at most {0}).", MaxCountLines]);
        // No Id: on a tracked count, a child with a key already set is taken for an existing row
        // (an UPDATE that hits nothing); left empty, EF generates it and inserts.
        foreach (var p in add) c.Lines.Add(new StockCountLine { StockCountId = c.Id, ProductId = p });
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveLineAsync(Guid id, Guid lineId, bool isAdmin, string user)
    {
        var (c, refusal) = await DraftForChangeAsync(id, isAdmin, user);
        if (c == null) return refusal!;
        var line = c.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return _log.Refuse(_loc["Line not found."]);
        if (c.Lines.Count == 1) return _log.Refuse(_loc["A count needs at least one product. Cancel the count instead."]);
        _counts.RemoveLine(line);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // ── Counts: submit ───────────────────────────────────────────────────────
    public async Task<StockCountSubmitResult> SubmitAsync(Guid id, IEnumerable<StockCountLineInput> inputs, string? notes,
        DateTime openedAtUtc, bool isAdmin, string user)
    {
        StockCountSubmitResult Fail(LocalizedString why, List<Guid>? moved = null)
        {
            _log.Refuse(why, "SubmitAsync");
            return new StockCountSubmitResult { Success = false, Error = why.Value, MovedProductIds = moved ?? new() };
        }

        var (c, refusal) = await DraftForChangeAsync(id, isAdmin, user);
        if (c == null) return new StockCountSubmitResult { Success = false, Error = refusal!.Error };
        if (Apply(c, inputs, notes) is { } bad) return Fail(bad);

        var missing = c.Lines.Where(l => l.CountedQty == null).Select(l => l.Product?.Name ?? "").OrderBy(n => n).ToList();
        if (missing.Count > 0)
        {
            await _uow.SaveChangesAsync();   // keep what was entered
            return Fail(_loc["Enter a count for every product before submitting ({0} still empty, e.g. {1}). Count 0 if there is none; remove a product that isn't part of this count.",
                             missing.Count, missing[0]]);
        }

        // Hold these products against sales and purchases while the system stock is read, so the
        // recorded figure is exactly the stock at the count moment.
        var productIds = c.Lines.Select(l => l.ProductId).ToList();
        await _ledger.LockProductsAsync(productIds);

        // A product that moved while the form was open was counted before or after that
        // movement; nobody can tell which, so it must be checked again rather than guessed.
        var now = DateTime.UtcNow;
        var since = openedAtUtc > now ? now : openedAtUtc;
        var moved = await _ledger.GetProductsMovedAsync(productIds, since, null);
        if (moved.Count > 0)
        {
            await _uow.SaveChangesAsync();   // keep what was entered
            var names = c.Lines.Where(l => moved.Contains(l.ProductId)).Select(l => l.Product?.Name ?? "").OrderBy(n => n).ToList();
            return Fail(_loc["Stock of {0} product(s) changed while you were counting (e.g. {1}): a sale or delivery was posted meanwhile. Check those products again, then submit.",
                             names.Count, string.Join(", ", names.Take(3))], moved);
        }

        var stock = await _ledger.GetBalancesBeforeAsync(null, now);
        var differences = 0;
        foreach (var l in c.Lines)
        {
            l.SystemQty = stock.GetValueOrDefault(l.ProductId);
            if (l.CountedQty!.Value == l.SystemQty.Value) { l.Decision = StockCountDecision.Matched; l.DecidedAt = now; l.DecidedBy = user; }
            else { l.Decision = StockCountDecision.Pending; differences++; }
        }
        c.CountNumber = await _counts.NextNumberAsync();
        c.SubmittedAt = now; c.SubmittedBy = user;
        c.Status = differences > 0 ? StockCountStatus.Submitted : StockCountStatus.Completed;
        if (differences == 0) { c.ClosedAt = now; c.ClosedBy = user; }

        await _audit.LogAsync(user, "StockCount.Submit",
            $"{c.CountNumber}: {c.Lines.Count} product(s), {differences} difference(s)");
        _log.LogInformation("Stock count {Number} submitted by {User}: {Lines} product(s), {Differences} difference(s)",
            c.CountNumber, user, c.Lines.Count, differences);
        await _uow.SaveChangesAsync();
        return new StockCountSubmitResult { Success = true };
    }

    // ── Counts: Admin's decision ─────────────────────────────────────────────
    public async Task<ServiceResult> DecideAsync(Guid id, Guid lineId, bool postAdjustment, string? note, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can settle a count difference."]);
        await _uow.LockAsync(id);
        var c = await _counts.GetByIdAsync(id);
        if (c == null) return _log.Refuse(_loc["Stock count not found."]);
        if (c.Status != StockCountStatus.Submitted || c.SubmittedAt is not { } at)
            return _log.Refuse(_loc["{0} has no differences waiting for a decision.", c.CountNumber ?? ""]);
        var line = c.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line == null) return _log.Refuse(_loc["Line not found."]);
        if (line.Decision != StockCountDecision.Pending)
            return _log.Refuse(_loc["{0} on {1} is already settled.", line.Product?.Name ?? "", c.CountNumber ?? ""]);
        note = Clean(note, 400);
        var name = line.Product?.Name ?? "";
        var now = DateTime.UtcNow;

        if (postAdjustment)
        {
            if (await _period.ExistingAsync(c.CountNumber ?? "", PeriodLock.LocalDay(at)) is { } closed)
                return _log.Refuse(closed);
            await _ledger.LockProductsAsync(new[] { line.ProductId });
            // Measured now, not at submit: a document dated before the count but entered since
            // has already moved the stock at that moment.
            var stockAtCount = (await _ledger.GetBalancesBeforeAsync(line.ProductId, at)).GetValueOrDefault(line.ProductId);
            var delta = line.CountedQty!.Value - stockAtCount;
            if (delta == 0)
                return _log.Refuse(_loc["{0}: documents entered since the count already account for the difference. Nothing to post; settle it as \"No adjustment\".", name]);
            var branch = await _branches.GetDefaultAsync();
            if (branch == null) return _log.Refuse(_loc["Default branch not found."]);
            var current = await _ledger.GetCurrentStockAsync(line.ProductId, branch.Id);
            if (delta < 0 && current < -delta)
                return _log.Refuse(_loc["{0}: only {1} in stock now, the count would take out {2}. Count this product again.",
                                        name, current.ToString("N0"), (-delta).ToString("N0")]);

            // At the moving average: a correction neither raises nor lowers the unit cost.
            var cost  = await _ledger.GetCurrentAvgCostAsync(line.ProductId, branch.Id);
            var adjId = Guid.NewGuid();
            await _ledger.AddAsync(new InventoryLedger {
                Id = Guid.NewGuid(), TransactionDate = at, BranchId = branch.Id, ProductId = line.ProductId,
                ReferenceType = ReferenceType.Adjustment, ReferenceId = adjId,
                QtyIn = delta > 0 ? delta : 0, QtyOut = delta < 0 ? -delta : 0,
                UnitCost = cost, TotalCost = Math.Abs(delta) * cost });
            await _adjustments.AddAsync(new StockAdjustment {
                Id = adjId, ProductId = line.ProductId, BranchId = branch.Id, AdjustmentDate = at,
                QtyBefore = stockAtCount, QtyAfter = line.CountedQty.Value,
                Reason = $"Stock opname {c.CountNumber}" + (note is null ? "" : $": {note}"), CreatedBy = user });
            line.Decision = StockCountDecision.Adjusted;
            line.StockAdjustmentId = adjId;
            line.AdjustedQty = delta;
            await _audit.LogAsync(user, "StockCount.Adjust", $"{c.CountNumber} {name}: {delta:+#,0.##;-#,0.##} at {cost:N2}");
            _log.LogInformation("Stock count {Number}: {Product} adjusted {Delta} at {Cost} each by {User}",
                c.CountNumber, name, delta, cost, user);
        }
        else
        {
            if (note is null) return _log.Refuse(_loc["Explain why this difference needs no adjustment."]);
            line.Decision = StockCountDecision.Dismissed;
            await _audit.LogAsync(user, "StockCount.NoAdjustment", $"{c.CountNumber} {name}: {note}");
            _log.LogInformation("Stock count {Number}: {Product} settled without adjustment by {User}", c.CountNumber, name, user);
        }
        line.DecisionNote = note; line.DecidedBy = user; line.DecidedAt = now;

        if (c.Lines.All(l => l.Decision != StockCountDecision.Pending))
        {
            c.Status = StockCountStatus.Completed; c.ClosedAt = now; c.ClosedBy = user;
        }
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CancelAsync(Guid id, string? reason, bool isAdmin, string user)
    {
        await _uow.LockAsync(id);
        var c = await _counts.GetByIdAsync(id);
        if (c == null) return _log.Refuse(_loc["Stock count not found."]);
        reason = Clean(reason, 400);
        switch (c.Status)
        {
            case StockCountStatus.Draft:
                if (!isAdmin && c.CreatedBy != user)
                    return _log.Refuse(_loc["Only the person who started this count, or an administrator, can cancel it."]);
                break;
            case StockCountStatus.Submitted:
                if (!isAdmin) return _log.Refuse(_loc["Only an administrator can cancel a submitted count."]);
                if (c.Lines.Any(l => l.Decision == StockCountDecision.Adjusted))
                    return _log.Refuse(_loc["{0} already has posted adjustments. Settle the remaining lines instead.", c.CountNumber ?? ""]);
                if (reason is null) return _log.Refuse(_loc["Give a reason for cancelling."]);
                break;
            default:
                return _log.Refuse(_loc["{0} is already closed.", c.CountNumber ?? ""]);
        }
        c.Status = StockCountStatus.Cancelled; c.ClosedAt = DateTime.UtcNow; c.ClosedBy = user;
        if (reason != null) c.Notes = Clean($"{c.Notes} [Batal: {reason}]".Trim(), 500);
        await _audit.LogAsync(user, "StockCount.Cancel", $"{c.CountNumber ?? "draft"}{(reason is null ? "" : ": " + reason)}");
        _log.LogInformation("Stock count {Number} cancelled by {User}", c.CountNumber ?? c.Id.ToString(), user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }
}
