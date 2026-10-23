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
/// Tukar barang (HC, 2026-10-22). A customer brings back a faulty unit and takes the same product,
/// new, in its place: one document against the invoice, shaped and guarded like a sales return
/// (reason required, not before the invoice, not in the future or a closed month, capped at what the
/// customer still holds, Staff's flagged for Admin, cancellable), but nothing changes hands. The
/// replacement leaves stock and the faulty unit comes back at the same current average cost
/// (InventoryService.ExchangeStockAsync), so stock, its value and Laba Rugi stay as they were; the
/// invoice, its HPP and its warranty dates stay as posted (HC: keep the original end date).
///
/// Before this, an exchange was entered as Penyesuaian "rusak ganti baru" (7 and 14 Oct): the new
/// unit's cost left stock value without ever reaching Laba Rugi, and nothing tied it to the invoice.
/// </summary>
public class ExchangeService : IExchangeService
{
    private readonly IExchangeRepository       _exchanges;
    private readonly ICustomerReturnRepository _returns;
    private readonly ISaleRepository           _sales;
    private readonly IBranchRepository         _branches;
    private readonly IAuditLogRepository       _audit;
    private readonly InventoryService          _inventory;
    private readonly IUnitOfWork               _uow;
    private readonly PeriodLock                _period;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<ExchangeService> _log;

    public ExchangeService(IExchangeRepository exchanges, ICustomerReturnRepository returns, ISaleRepository sales,
        IBranchRepository branches, IAuditLogRepository audit, InventoryService inventory, IUnitOfWork uow,
        PeriodLock period, IStringLocalizer<SharedResource> loc, ILogger<ExchangeService> log)
    { _exchanges = exchanges; _returns = returns; _sales = sales; _branches = branches; _audit = audit;
      _inventory = inventory; _uow = uow; _period = period; _loc = loc; _log = log; }

    public async Task<ExchangeFormDto?> GetFormAsync(Guid saleId)
    {
        var sale = await _sales.GetByIdWithItemsAsync(saleId);
        if (sale == null) return null;
        var form = new ExchangeFormDto {
            SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, SaleDate = sale.SaleDate,
            CustomerName = sale.Customer?.Name ?? "" };
        if (sale.Status == SaleStatus.Cancelled)
        {
            form.BlockReason = _loc["This invoice is cancelled, so there is nothing to exchange on it."];
            return form;
        }
        var returned  = await _returns.GetReturnedQtyBySaleItemAsync(saleId);
        var exchanged = await _exchanges.GetExchangedQtyBySaleItemAsync(saleId);
        foreach (var i in sale.SaleItems)
            form.Lines.Add(new ExchangeableLineDto {
                SaleItemId = i.Id, ProductId = i.ProductId, ProductName = i.Product?.Name ?? "", SKU = i.Product?.SKU ?? "",
                SoldQty = i.Qty, ReturnedQty = returned.GetValueOrDefault(i.Id)?.Qty ?? 0m,
                ExchangedQty = exchanged.GetValueOrDefault(i.Id),
                InStock = await _inventory.GetCurrentStockAsync(i.ProductId) });
        return form;
    }

    public async Task<ServiceResult<ExchangeDto>> CreateAsync(CreateExchangeDto dto, string user, bool isAdmin)
    {
        var inputs = (dto.Items ?? new()).Where(i => i.Qty != 0).ToList();
        if (inputs.Count == 0) return _log.Refuse<ExchangeDto>(_loc["Enter a quantity on at least one line."]);
        var reason = SanitiseText(dto.Reason, 500);
        if (reason == null) return _log.Refuse<ExchangeDto>(_loc["Say what was wrong with the unit."]);
        if (inputs.GroupBy(i => i.SourceItemId).Any(g => g.Count() > 1))
            return _log.Refuse<ExchangeDto>(_loc["The same invoice line appears twice — combine it into one row."]);

        // One request at a time per invoice (see IUnitOfWork.LockAsync; security review R1).
        await _uow.LockAsync(dto.SaleId);
        var sale = await _sales.GetByIdWithItemsAsync(dto.SaleId);
        if (sale == null) return _log.Refuse<ExchangeDto>(_loc["Invoice not found."]);
        if (sale.Status == SaleStatus.Cancelled)
            return _log.Refuse<ExchangeDto>(_loc["This invoice is cancelled, so there is nothing to exchange on it."]);
        var branch = await _branches.GetDefaultAsync();
        if (branch == null) return _log.Refuse<ExchangeDto>(_loc["Default branch not found."]);

        var today = DateTime.Now.Date;
        var day = dto.ExchangeDate?.Date ?? today;
        if (day > today) return _log.Refuse<ExchangeDto>(_loc["The exchange date can't be in the future."]);
        var saleDay = PeriodLock.LocalDay(sale.SaleDate);
        if (day < saleDay)
            return _log.Refuse<ExchangeDto>(_loc["The exchange date can't be before the invoice date ({0}).", saleDay.ToString("dd MMM yyyy")]);
        if (await _period.NewDateAsync(day) is { } closed) return _log.Refuse<ExchangeDto>(closed);

        // What the customer still holds of each line: sold less what came back on a return.
        var returned = await _returns.GetReturnedQtyBySaleItemAsync(sale.Id);
        var id = Guid.NewGuid();
        var items = new List<ExchangeItem>();
        foreach (var input in inputs)
        {
            if (input.Qty <= 0) return _log.Refuse<ExchangeDto>(_loc["All quantities must be greater than zero."]);
            if (!Limits.IsWholeUnits(input.Qty)) return _log.Refuse<ExchangeDto>(_loc["Quantities must be whole units (no fractions)."]);
            var line = sale.SaleItems.FirstOrDefault(i => i.Id == input.SourceItemId);
            if (line == null) return _log.Refuse<ExchangeDto>(_loc["A line being exchanged is not on this invoice."]);
            var held = line.Qty - (returned.GetValueOrDefault(line.Id)?.Qty ?? 0m);
            if (input.Qty > held)
                return _log.Refuse<ExchangeDto>(_loc["'{0}': {1} to exchange but the customer only has {2} of it from this invoice.",
                    line.Product?.Name ?? "", input.Qty.ToString("N0"), held.ToString("N0")]);
            items.Add(new ExchangeItem {
                Id = Guid.NewGuid(), ExchangeId = id, SaleItemId = line.Id, ProductId = line.ProductId,
                Qty = input.Qty, Notes = SanitiseText(input.Notes, 500), Product = line.Product });
        }

        var moved = await _inventory.ExchangeStockAsync(
            items.Select(i => new StockMovementLine(i.ProductId, i.Product?.Name ?? "", i.Qty, 0m)).ToList(),
            id, branch.Id, LedgerDate.FromLocalDay(day));
        if (!moved.Success) return ServiceResult<ExchangeDto>.Fail(moved.Error!);

        var x = new Exchange {
            Id = id, ExchangeNumber = await _exchanges.GenerateNumberAsync(), ExchangeDate = day,
            SaleId = sale.Id, BranchId = branch.Id, Reason = reason, Notes = SanitiseText(dto.Notes, 500),
            Status = ReturnStatus.Active, CreatedBy = user, CreatedAt = DateTime.UtcNow,
            // Every Staff exchange waits for Admin, as Staff returns do (R10).
            NeedsReview = !isAdmin, Items = items };
        await _exchanges.AddAsync(x);
        await _audit.LogAsync(user, "Exchange.Create",
            $"{x.ExchangeNumber} | {sale.InvoiceNumber} | " + string.Join(", ", items.Select(i => $"{i.Product?.SKU} x{i.Qty:N0}")) + $" | {reason}");
        _log.LogInformation("Exchange {Number} on {Invoice}: {Units} unit(s) swapped, by {User}",
            x.ExchangeNumber, sale.InvoiceNumber, items.Sum(i => i.Qty), user);
        await _uow.SaveChangesAsync();
        return ServiceResult<ExchangeDto>.Ok((await GetAsync(id))!);
    }

    public async Task<ServiceResult> CancelAsync(Guid id, string user)
    {
        await _uow.LockAsync(id);
        var x = await _exchanges.GetByIdWithItemsAsync(id);
        if (x == null) return _log.Refuse(_loc["Exchange not found."]);
        if (x.Status == ReturnStatus.Cancelled) return _log.Refuse(_loc["This exchange is already cancelled."]);
        if (await _period.ExistingAsync(x.ExchangeNumber, x.ExchangeDate.Date) is { } closed) return _log.Refuse(closed);
        // Undone today: the replacement comes back and the faulty unit leaves, at today's average.
        var moved = await _inventory.ExchangeStockAsync(
            x.Items.Select(i => new StockMovementLine(i.ProductId, i.Product?.Name ?? "", i.Qty, 0m)).ToList(),
            x.Id, x.BranchId, DateTime.UtcNow, cancel: true);
        if (!moved.Success) return _log.Refuse(moved.Error!);
        x.Status = ReturnStatus.Cancelled;
        _exchanges.Update(x);
        await _audit.LogAsync(user, "Exchange.Cancel", $"{x.ExchangeNumber} | {x.Sale?.InvoiceNumber}");
        _log.LogInformation("Exchange {Number} cancelled by {User}", x.ExchangeNumber, user);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> MarkReviewedAsync(Guid id, bool isAdmin, string user)
    {
        if (!isAdmin) return _log.Refuse(_loc["Only an administrator can mark an exchange as checked."]);
        await _uow.LockAsync(id);
        var x = await _exchanges.GetByIdWithItemsAsync(id);
        if (x == null) return _log.Refuse(_loc["Exchange not found."]);
        if (!x.NeedsReview) return _log.Refuse(_loc["{0} is already checked.", x.ExchangeNumber]);
        x.NeedsReview = false; x.ReviewedBy = user; x.ReviewedAt = DateTime.UtcNow;
        await _audit.LogAsync(user, "Exchange.Review", x.ExchangeNumber);
        await _uow.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ExchangeDto?> GetAsync(Guid id)
    {
        var x = await _exchanges.GetByIdWithItemsAsync(id);
        return x == null ? null : Map(x, x.Sale?.InvoiceNumber ?? "", x.Sale?.Customer?.Name ?? "");
    }

    public async Task<List<ExchangeDto>> GetForSaleAsync(Guid saleId) =>
        (await _exchanges.GetBySaleAsync(saleId)).Select(x => Map(x, "", "")).ToList();

    public async Task<List<ExchangeDto>> GetAllAsync(DateTime? from = null, DateTime? to = null, string? search = null) =>
        (await _exchanges.GetAllAsync(from, to, search))
            .Select(x => Map(x, x.Sale?.InvoiceNumber ?? "", x.Sale?.Customer?.Name ?? "")).ToList();

    public Task<int> CountNeedingReviewAsync() => _exchanges.CountNeedingReviewAsync();

    private static ExchangeDto Map(Exchange x, string invoice, string customer) => new() {
        Id = x.Id, ExchangeNumber = x.ExchangeNumber, ExchangeDate = x.ExchangeDate, SaleId = x.SaleId,
        InvoiceNumber = invoice, CustomerName = customer, Reason = x.Reason, Notes = x.Notes,
        Status = x.Status.ToString(), CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt,
        NeedsReview = x.NeedsReview, ReviewedBy = x.ReviewedBy, ReviewedAt = x.ReviewedAt,
        Items = x.Items.Select(i => new ExchangeItemDto {
            SaleItemId = i.SaleItemId, ProductName = i.Product?.Name ?? "", SKU = i.Product?.SKU ?? "",
            Qty = i.Qty, Notes = i.Notes }).ToList() };

    private static string? SanitiseText(string? s, int maxLen)
    {
        s = TextClean.StripControl(s);
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > maxLen ? s[..maxLen] : s;
    }
}
