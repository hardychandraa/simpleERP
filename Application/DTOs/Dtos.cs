using SimpleERP.Domain.Enums;

namespace SimpleERP.Application.DTOs;

// ── Product ───────────────────────────────────────────────────────────────────
public class ProductDto {
    public Guid    Id                    { get; set; }
    public string  Name                  { get; set; } = "";
    public string  SKU                   { get; set; } = "";
    public decimal UnitPrice             { get; set; }
    public string? Category              { get; set; }
    public int?    DefaultWarrantyMonths { get; set; }
    public int     LowStockThreshold     { get; set; } = 2;
    public bool    IsActive              { get; set; }
    public decimal CurrentStock          { get; set; }
    public decimal AvgCost               { get; set; }
    public bool    IsLow                 => CurrentStock <= LowStockThreshold && CurrentStock > 0;
    public bool    IsOut                 => CurrentStock <= 0;
}
public class CreateProductDto {
    public string  Name                  { get; set; } = "";
    public string  SKU                   { get; set; } = "";
    public decimal UnitPrice             { get; set; }
    public string? Category              { get; set; }
    public int?    DefaultWarrantyMonths { get; set; }
    public int     LowStockThreshold     { get; set; } = 2;
}
public class UpdateProductDto {
    public Guid    Id                    { get; set; }
    public string  Name                  { get; set; } = "";
    public string  SKU                   { get; set; } = "";
    public decimal UnitPrice             { get; set; }
    public string? Category              { get; set; }
    public int?    DefaultWarrantyMonths { get; set; }
    public int     LowStockThreshold     { get; set; } = 2;
    public bool    IsActive              { get; set; }
}

// ── Customer ──────────────────────────────────────────────────────────────────
public class CustomerDto {
    public Guid    Id       { get; set; }
    public string  Name     { get; set; } = "";
    public string? Phone    { get; set; }
    public string? Address  { get; set; }
    public bool    IsActive { get; set; }
    public decimal TotalDue { get; set; }
}
public class CreateCustomerDto {
    public string  Name    { get; set; } = "";
    public string? Phone   { get; set; }
    public string? Address { get; set; }
}
public class UpdateCustomerDto {
    public Guid    Id       { get; set; }
    public string  Name     { get; set; } = "";
    public string? Phone    { get; set; }
    public string? Address  { get; set; }
    public bool    IsActive { get; set; }
}

// ── Inventory ─────────────────────────────────────────────────────────────────
public class StockInDto {
    public Guid    ProductId { get; set; }
    public decimal Qty       { get; set; }
    public decimal UnitCost  { get; set; }
    public string? Notes     { get; set; }
}
public class StockAdjustmentDto {
    public Guid    ProductId  { get; set; }
    public decimal QtyActual  { get; set; }   // what staff physically counted
    public decimal AdjustedQty { get; set; }
    public string  Reason     { get; set; } = "";
}
public class InventoryLedgerDto {
    public Guid     Id              { get; set; }
    public DateTime TransactionDate { get; set; }
    public string   ProductName     { get; set; } = "";
    public string   ReferenceType   { get; set; } = "";
    public decimal  QtyIn           { get; set; }
    public decimal  QtyOut          { get; set; }
    public decimal  UnitCost        { get; set; }
    public decimal  TotalCost       { get; set; }
}
public class StockLevelDto {
    public Guid    ProductId         { get; set; }
    public string  ProductName       { get; set; } = "";
    public string  SKU               { get; set; } = "";
    public decimal CurrentStock      { get; set; }
    public decimal AvgCost           { get; set; }
    public decimal StockValue        { get; set; }
    public int     LowStockThreshold { get; set; } = 2;
    public bool    IsLow             => CurrentStock <= LowStockThreshold && CurrentStock > 0;
    public bool    IsOut             => CurrentStock <= 0;
}

// ── Sales ─────────────────────────────────────────────────────────────────────
public class CreateSaleDto {
    public Guid        CustomerId   { get; set; }
    public PaymentType PaymentType  { get; set; }
    public string?     Notes        { get; set; }
    /// <summary>True if the entered prices already include PPN; false to add PPN on top.</summary>
    public bool        IsTaxInclusive { get; set; }
    public List<CreateSaleItemDto> Items { get; set; } = new();
}
public class CreateSaleItemDto {
    public Guid    ProductId      { get; set; }
    public decimal Qty            { get; set; }
    public decimal UnitPrice      { get; set; }
    public decimal DiscountAmount { get; set; }
    public int?    WarrantyMonths { get; set; }
    /// <summary>Free-text line-item notes (serial number, condition, etc.)</summary>
    public string? Notes          { get; set; }
    /// <summary>Required when UnitPrice differs from product master price.</summary>
    public string? PriceReason    { get; set; }
}
public class SaleDto {
    public Guid      Id            { get; set; }
    public string    InvoiceNumber { get; set; } = "";
    public DateTime  SaleDate      { get; set; }
    public string    CustomerName  { get; set; } = "";
    public string?   CustomerPhone { get; set; }
    public string    PaymentType   { get; set; } = "";
    public DateTime? DueDate       { get; set; }
    public decimal   SubTotal      { get; set; }
    public decimal   DiscountTotal { get; set; }
    public decimal   TaxBase       { get; set; }
    /// <summary>PPN rate as a fraction (0.10 = 10%), as applied to this sale.</summary>
    public decimal   TaxRate       { get; set; }
    public decimal   TaxAmount     { get; set; }
    public bool      IsTaxInclusive{ get; set; }
    public decimal   GrandTotal    { get; set; }
    public decimal   AmountPaid    { get; set; }
    public decimal   BalanceDue    => GrandTotal - AmountPaid;
    public string    Status        { get; set; } = "";
    public string?   Notes         { get; set; }
    public string    CreatedBy     { get; set; } = "";
    public List<SaleItemDto>      Items          { get; set; } = new();
    public List<PaymentRecordDto> PaymentHistory { get; set; } = new();
}
public class SaleItemDto {
    public Guid     Id             { get; set; }
    public string   ProductName    { get; set; } = "";
    public string   SKU            { get; set; } = "";
    public decimal  Qty            { get; set; }
    public decimal  UnitPrice      { get; set; }
    public decimal  DiscountAmount { get; set; }
    public decimal  LineTotal      { get; set; }
    public int?     WarrantyMonths { get; set; }
    public DateTime? WarrantyExpiry{ get; set; }
    /// <summary>Free-text notes per line item.</summary>
    public string?  Notes          { get; set; }
    /// <summary>Price override reason (when price differs from product master).</summary>
    public string?  PriceReason    { get; set; }
}
public class SaleListDto {
    public Guid     Id            { get; set; }
    public string   InvoiceNumber { get; set; } = "";
    public DateTime SaleDate      { get; set; }
    public string   CustomerName  { get; set; } = "";
    public string   PaymentType   { get; set; } = "";
    public DateTime? DueDate      { get; set; }
    public decimal  GrandTotal    { get; set; }
    public decimal  AmountPaid    { get; set; }
    public decimal  BalanceDue    => GrandTotal - AmountPaid;
    public string   Status        { get; set; } = "";
    /// <summary>True if DueDate is set and today is past it and balance still owed.</summary>
    public bool     IsOverdue     => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && BalanceDue > 0;
}

// ── Payments ──────────────────────────────────────────────────────────────────
public class RecordPaymentDto {
    public Guid    SaleId  { get; set; }
    public decimal Amount  { get; set; }
    public string? Notes   { get; set; }
}
public class PaymentRecordDto {
    public Guid     Id          { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "";
}
public class PaymentCollectionDto {
    public Guid     Id          { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "";
}

// ── Reports ───────────────────────────────────────────────────────────────────
public class EndOfDayDto {
    public DateTime Date               { get; set; }
    public int      TotalSales         { get; set; }
    public int      CashSales          { get; set; }
    public int      DueSales           { get; set; }
    public int      CancelledSales     { get; set; }
    public decimal  CashRevenue        { get; set; }
    public decimal  DueRevenue         { get; set; }
    public decimal  TotalRevenue       => CashRevenue + DueRevenue;
    /// <summary>Payments collected today against credit sales (any sale date).</summary>
    public decimal  PaymentsCollected  { get; set; }
    /// <summary>Actual cash in hand today: cash-sale revenue + credit collections.</summary>
    public decimal  TotalCashIn        => CashRevenue + PaymentsCollected;
    /// <summary>All unpaid Due balance across all time.</summary>
    public decimal  OutstandingDueTotal{ get; set; }
    public List<SaleListDto>        SalesList    { get; set; } = new();
    public List<EndOfDayPaymentDto> PaymentsList { get; set; } = new();
}
public class EndOfDayPaymentDto {
    public string  InvoiceNumber { get; set; } = "";
    public string  CustomerName  { get; set; } = "";
    public decimal Amount        { get; set; }
}
public class WarrantyItemDto {
    public string   InvoiceNumber  { get; set; } = "";
    public Guid     SaleId         { get; set; }
    public DateTime SaleDate       { get; set; }
    public string   CustomerName   { get; set; } = "";
    public string?  CustomerPhone  { get; set; }
    public string   ProductName    { get; set; } = "";
    public string   SKU            { get; set; } = "";
    public int?     WarrantyMonths { get; set; }
    public DateTime WarrantyExpiry { get; set; }
    public bool     IsExpired      => WarrantyExpiry < DateTime.UtcNow;
    public int      DaysRemaining  => (int)(WarrantyExpiry - DateTime.UtcNow).TotalDays;
}
public class DueCustomerDto {
    public Guid     CustomerId    { get; set; }
    public string   CustomerName  { get; set; } = "";
    public string?  Phone         { get; set; }
    public int      OpenInvoices  { get; set; }
    public decimal  TotalDue      { get; set; }
    public bool     HasOverdue    { get; set; }
}

// ── Financial reports ─────────────────────────────────────────────────────────
/// <summary>
/// Commercial P&amp;L for a period. Currently covers the trading section only
/// (Penjualan → HPP → Laba Kotor); rebate income, operating expenses and
/// commission arrive with their respective modules. <see cref="IsComplete"/>
/// reports whether the bottom line is trustworthy yet.
/// </summary>
public class ProfitAndLossDto {
    public DateTime From          { get; set; }
    public DateTime To            { get; set; }
    public int      InvoiceCount  { get; set; }

    /// <summary>Penjualan — net of PPN. See SalesPeriodTotals for why.</summary>
    public decimal  Revenue       { get; set; }
    /// <summary>HPP — from each line's cost snapshot at sale time.</summary>
    public decimal  Cogs          { get; set; }
    public decimal  GrossProfit   => Revenue - Cogs;
    public decimal  GrossMarginPct=> Revenue == 0 ? 0 : GrossProfit / Revenue * 100m;

    /// <summary>PPN collected. A memo line: a liability owed on, not income.</summary>
    public decimal  TaxCollected  { get; set; }
    /// <summary>Tax-inclusive invoiced total, for tying back to cash and AR.</summary>
    public decimal  GrossSales    { get; set; }

    /// <summary>Sections that have no data source yet, shown so the gap is visible
    /// in the report rather than silently omitted.</summary>
    public List<string> PendingSections { get; set; } = new();
    public bool     IsComplete    => PendingSections.Count == 0;
}

// ── Audit ─────────────────────────────────────────────────────────────────────
public class AuditLogDto {
    public long     Id        { get; set; }
    public DateTime Timestamp { get; set; }
    public string   User      { get; set; } = "";
    public string   Action    { get; set; } = "";
    public string?  Detail    { get; set; }
}

// ── Settings ──────────────────────────────────────────────────────────────────
public class AppSettingsDto {
    public string AppName        { get; set; } = "SimpleERP";
    public string StoreName      { get; set; } = "My Store";
    public string? StoreAddress  { get; set; }
    public string? StorePhone    { get; set; }
    public string  StoreFooter   { get; set; } = "Thank you for your purchase!";
    public string  PrinterName   { get; set; } = "";
    public int     PaperColumns  { get; set; } = 80;
    public bool    PrinterEnabled{ get; set; } = false;
    /// <summary>PPN rate entered as a percentage (10 = 10%). Stored as a fraction on the entity.</summary>
    public decimal VatRatePercent{ get; set; } = 10m;
}

// ── Result wrappers ───────────────────────────────────────────────────────────
public class ServiceResult {
    public bool    Success { get; private set; }
    public string? Error   { get; private set; }
    public static ServiceResult Ok()              => new() { Success = true };
    public static ServiceResult Fail(string err)  => new() { Success = false, Error = err };
}
public class ServiceResult<T> {
    public bool    Success { get; private set; }
    public string? Error   { get; private set; }
    public T?      Data    { get; private set; }
    public static ServiceResult<T> Ok(T data)     => new() { Success = true,  Data = data };
    public static ServiceResult<T> Fail(string e) => new() { Success = false, Error = e   };
}
