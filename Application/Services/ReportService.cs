using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class ReportService : IReportService
{
    private readonly ISaleRepository          _sales;
    private readonly IAuditLogRepository      _audit;
    private readonly IPaymentRecordRepository _payments;

    public ReportService(ISaleRepository sales, IAuditLogRepository audit,
                         IPaymentRecordRepository payments)
    { _sales=sales; _audit=audit; _payments=payments; }

    public async Task<EndOfDayDto> GetEndOfDayAsync(DateTime date)
    {
        // `date` is the business's local calendar day, as picked on the report screen.
        // Sale/payment timestamps are stored in UTC (DateTime.UtcNow), and every screen
        // renders them back through .ToLocalTime() — so "today" means the local day, and
        // the query bounds are that local midnight-to-midnight converted to UTC.
        //
        // Do NOT call .Date after converting: truncating the converted instant back to
        // midnight discards the UTC offset and shifts the whole window by a day (at UTC+7
        // this reported 07:00-yesterday to 07:00-today, so sales made after local midnight
        // were missing while the previous evening's were counted).
        var localMidnight = DateTime.SpecifyKind(date.Date, DateTimeKind.Local);
        var from = localMidnight.ToUniversalTime();
        var to   = localMidnight.AddDays(1).ToUniversalTime();

        var allToday   = await _sales.GetAllAsync(from, to);
        var todaySales = allToday.Where(s => s.Status == Domain.Enums.SaleStatus.Active).ToList();
        var cancelled  = allToday.Count(s => s.Status == Domain.Enums.SaleStatus.Cancelled);

        // All-time outstanding balance
        var allDue = await _sales.GetDueSalesAsync();
        var totalOutstanding = allDue.Sum(s => s.GrandTotal - s.AmountPaid);

        var cashSales   = todaySales.Where(s => s.PaymentType == Domain.Enums.PaymentType.Cash).ToList();
        var creditSales = todaySales.Where(s => s.PaymentType != Domain.Enums.PaymentType.Cash).ToList();

        // Collections banked today against credit sales — the sale itself may be from any earlier day,
        // so this is queried by payment date, not by sale date.
        var todayPayments = await _payments.GetByDateRangeAsync(from, to);

        return new EndOfDayDto {
            Date              = date,
            TotalSales        = todaySales.Count,
            CashSales         = cashSales.Count,
            DueSales          = creditSales.Count,
            CancelledSales    = cancelled,
            CashRevenue       = cashSales.Sum(s => s.GrandTotal),
            DueRevenue        = creditSales.Sum(s => s.GrandTotal),
            PaymentsCollected = todayPayments.Sum(p => p.Amount),
            OutstandingDueTotal = totalOutstanding,
            SalesList = todaySales.Select(s => new SaleListDto {
                Id = s.Id, InvoiceNumber = s.InvoiceNumber, SaleDate = s.SaleDate,
                CustomerName = s.Customer?.Name ?? "",
                PaymentType = s.PaymentType.ToString(),
                DueDate = s.DueDate,
                GrandTotal = s.GrandTotal, AmountPaid = s.AmountPaid,
                Status = s.Status.ToString()
            }).ToList(),
            PaymentsList = todayPayments.Select(p => new EndOfDayPaymentDto {
                InvoiceNumber = p.Sale?.InvoiceNumber   ?? "",
                CustomerName  = p.Sale?.Customer?.Name  ?? "",
                Amount        = p.Amount
            }).ToList()
        };
    }

    public async Task<List<WarrantyItemDto>> GetWarrantiesAsync(string? search = null, bool activeOnly = true)
    {
        // Fetch recent sales; for warranty purposes look back 5 years max
        var from = DateTime.UtcNow.AddYears(-5);
        var sales = await _sales.GetAllAsync(from, null);

        var items = new List<WarrantyItemDto>();
        foreach (var sale in sales.Where(s => s.Status == Domain.Enums.SaleStatus.Active))
        foreach (var item in sale.SaleItems.Where(i => i.WarrantyExpiry.HasValue))
        {
            if (activeOnly && item.WarrantyExpiry!.Value < DateTime.UtcNow) continue;
            items.Add(new WarrantyItemDto {
                InvoiceNumber = sale.InvoiceNumber,
                SaleId        = sale.Id,
                SaleDate      = sale.SaleDate,
                CustomerName  = sale.Customer?.Name ?? "",
                CustomerPhone = sale.Customer?.Phone,
                ProductName   = item.Product?.Name ?? "",
                SKU           = item.Product?.SKU  ?? "",
                WarrantyMonths = item.WarrantyMonths,
                WarrantyExpiry = item.WarrantyExpiry!.Value
            });
        }

        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(w =>
                w.CustomerName.Contains(search,  StringComparison.OrdinalIgnoreCase) ||
                w.ProductName.Contains(search,   StringComparison.OrdinalIgnoreCase) ||
                w.InvoiceNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (w.CustomerPhone ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        return items.OrderBy(w => w.WarrantyExpiry).ToList();
    }

    public async Task<List<AuditLogDto>> GetAuditLogAsync(int count = 100)
    {
        var logs = await _audit.GetRecentAsync(count);
        return logs.Select(l => new AuditLogDto {
            Id = l.Id, Timestamp = l.Timestamp, User = l.User,
            Action = l.Action, Detail = l.Detail
        }).ToList();
    }
}
