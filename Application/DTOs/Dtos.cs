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
    /// <summary>
    /// What staff physically counted. The service works out the delta against the ledger.
    /// This was previously shadowed by a second AdjustedQty field that the form bound to
    /// and the service ignored, so every adjustment was read as a count of zero.
    /// </summary>
    public decimal QtyActual  { get; set; }
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

// ── Expenses ──────────────────────────────────────────────────────────────────
public class ExpenseCategoryDto {
    public Guid   Id              { get; set; }
    public string Name            { get; set; } = "";
    public bool   IsActive        { get; set; } = true;
    public bool   IsTaxDeductible { get; set; } = true;
    public int    SortOrder       { get; set; }
    public bool   InUse           { get; set; }
}
public class ExpenseDto {
    public Guid     Id           { get; set; }
    public DateTime ExpenseDate  { get; set; }
    public Guid     CategoryId   { get; set; }
    public string   CategoryName { get; set; } = "";
    public decimal  Amount       { get; set; }
    public string?  Description  { get; set; }
    public string?  ReferenceNo  { get; set; }
    public string   CreatedBy    { get; set; } = "";
}
public class CreateExpenseDto {
    public DateTime ExpenseDate { get; set; }
    public Guid     CategoryId  { get; set; }
    public decimal  Amount      { get; set; }
    public string?  Description { get; set; }
    public string?  ReferenceNo { get; set; }
}
public class UpdateExpenseDto : CreateExpenseDto {
    public Guid Id { get; set; }
}

// ── Payment terms ─────────────────────────────────────────────────────────────
public class PaymentTermDto {
    public Guid   Id        { get; set; }
    public string Name      { get; set; } = "";
    public int    DueDays   { get; set; }
    public bool   IsActive  { get; set; } = true;
    public int    SortOrder { get; set; }
    /// <summary>True when a posted sale references it — the UI hides Delete.</summary>
    public bool   InUse     { get; set; }
}

// ── Suppliers ─────────────────────────────────────────────────────────────────
public class SupplierDto {
    public Guid    Id            { get; set; }
    public string  Name          { get; set; } = "";
    public string? Phone         { get; set; }
    public string? Address       { get; set; }
    /// <summary>NPWP.</summary>
    public string? TaxId         { get; set; }
    public Guid?   PaymentTermId { get; set; }
    /// <summary>Resolved term name for display. Empty when no default term is set.</summary>
    public string  PaymentTermName { get; set; } = "";
    public bool    IsActive      { get; set; } = true;
    public string? Notes         { get; set; }
    /// <summary>True when a posted purchase references it — the UI hides Delete.</summary>
    public bool    InUse         { get; set; }
}

// ── Purchases ─────────────────────────────────────────────────────────────────
public class CreatePurchaseDto {
    public Guid        SupplierId  { get; set; }
    /// <summary>The supplier's own invoice/PO number, as printed on their paperwork.</summary>
    public string?     SupplierDocumentNumber { get; set; }
    /// <summary>Date on the supplier's invoice, which is often not today.</summary>
    public DateTime?   PurchaseDate { get; set; }
    public PaymentType PaymentType { get; set; }
    public Guid?       PaymentTermId { get; set; }
    /// <summary>Flat whole-document discount. Ignored when InvoiceDiscountPercent is supplied.</summary>
    public decimal     InvoiceDiscountAmount  { get; set; }
    /// <summary>Whole-document discount as a percentage of the post-line-discount total (0–100).</summary>
    public decimal?    InvoiceDiscountPercent { get; set; }
    public string?     Notes       { get; set; }
    /// <summary>True if the supplier's costs already include PPN; false to add it on top.</summary>
    public bool        IsTaxInclusive { get; set; }
    public List<CreatePurchaseItemDto> Items { get; set; } = new();
}
public class CreatePurchaseItemDto {
    public Guid    ProductId      { get; set; }
    public decimal Qty            { get; set; }
    public decimal UnitCost       { get; set; }
    /// <summary>Flat discount per unit. Ignored when DiscountPercent is supplied.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Discount as a percentage of unit cost (0–100). Takes precedence over DiscountAmount.</summary>
    public decimal? DiscountPercent { get; set; }
    public string? Notes          { get; set; }
}
public class PurchaseDto {
    public Guid      Id             { get; set; }
    public string    PurchaseNumber { get; set; } = "";
    public string?   SupplierDocumentNumber { get; set; }
    public DateTime  PurchaseDate   { get; set; }
    public Guid      SupplierId     { get; set; }
    public string    SupplierName   { get; set; } = "";
    public string?   SupplierPhone  { get; set; }
    public string    PaymentType    { get; set; } = "";
    public string    PaymentTermName{ get; set; } = "";
    public DateTime? DueDate        { get; set; }
    public decimal   SubTotal       { get; set; }
    public decimal   DiscountTotal  { get; set; }
    public decimal   InvoiceDiscountAmount  { get; set; }
    public decimal?  InvoiceDiscountPercent { get; set; }
    public decimal   TaxBase        { get; set; }
    public decimal   TaxRate        { get; set; }
    public decimal   TaxAmount      { get; set; }
    public bool      IsTaxInclusive { get; set; }
    public decimal   GrandTotal     { get; set; }
    public decimal   AmountPaid     { get; set; }
    public decimal   BalanceDue     => GrandTotal - AmountPaid;
    public string    Status         { get; set; } = "";
    public string?   Notes          { get; set; }
    public string    CreatedBy      { get; set; } = "";
    public List<PurchaseItemDto>     Items          { get; set; } = new();
    public List<SupplierPaymentDto>  PaymentHistory { get; set; } = new();
}
public class PurchaseItemDto {
    public Guid     Id             { get; set; }
    public Guid     ProductId      { get; set; }
    public string   ProductName    { get; set; } = "";
    public string   SKU            { get; set; } = "";
    public decimal  Qty            { get; set; }
    public decimal  UnitCost       { get; set; }
    public decimal  DiscountAmount { get; set; }
    public decimal? DiscountPercent{ get; set; }
    public decimal  LineTotal      { get; set; }
    public decimal  AllocatedInvoiceDiscount { get; set; }
    /// <summary>
    /// LineTotal net of the allocated document discount — the real cost of this line.
    /// Any future rebate or margin calculation must read this, never LineTotal alone.
    /// </summary>
    public decimal  NetLineTotal   => LineTotal - AllocatedInvoiceDiscount;
    public string?  Notes          { get; set; }
}
public class PurchaseListDto {
    public Guid     Id             { get; set; }
    public string   PurchaseNumber { get; set; } = "";
    public string?  SupplierDocumentNumber { get; set; }
    public DateTime PurchaseDate   { get; set; }
    public string   SupplierName   { get; set; } = "";
    public string   PaymentType    { get; set; } = "";
    public DateTime? DueDate       { get; set; }
    public decimal  GrandTotal     { get; set; }
    public decimal  AmountPaid     { get; set; }
    public decimal  BalanceDue     => GrandTotal - AmountPaid;
    public string   Status         { get; set; } = "";
    public bool     IsOverdue      => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && BalanceDue > 0;
}
public class RecordSupplierPaymentDto {
    public Guid    PurchaseId { get; set; }
    public decimal Amount     { get; set; }
    public string? Notes      { get; set; }
}
public class SupplierPaymentDto {
    public Guid     Id          { get; set; }
    public DateTime PaymentDate { get; set; }
    public decimal  Amount      { get; set; }
    public string?  Notes       { get; set; }
    public string   CreatedBy   { get; set; } = "";
}
/// <summary>One supplier's outstanding AP, for the payables summary.</summary>
public class DueSupplierDto {
    public Guid    SupplierId    { get; set; }
    public string  SupplierName  { get; set; } = "";
    public string? Phone         { get; set; }
    public int     OpenPurchases { get; set; }
    public decimal TotalDue      { get; set; }
    public bool    HasOverdue    { get; set; }
}

// ── Returns ───────────────────────────────────────────────────────────────────
/// <summary>One line of a source document that can still be returned, with its cap.</summary>
public class ReturnableLineDto {
    /// <summary>The SaleItem or PurchaseItem this line came from.</summary>
    public Guid    SourceItemId    { get; set; }
    public Guid    ProductId       { get; set; }
    public string  ProductName     { get; set; } = "";
    public string  SKU             { get; set; } = "";
    public decimal OriginalQty     { get; set; }
    public decimal AlreadyReturned { get; set; }
    public decimal ReturnableQty   => OriginalQty - AlreadyReturned;
    /// <summary>Unit price as sold, or unit cost as billed. Display only.</summary>
    public decimal UnitAmount      { get; set; }
    /// <summary>Line total net of both its own discount and its share of the document discount.</summary>
    public decimal NetLineTotal    { get; set; }
    /// <summary>
    /// What one unit is actually credited at — the net line divided by the original
    /// quantity, never UnitAmount, so a discounted document isn't credited back at gross.
    /// </summary>
    public decimal UnitCredit      => OriginalQty == 0 ? 0m : NetLineTotal / OriginalQty;
}
/// <summary>A source document plus its returnable lines — everything the return form needs.</summary>
public class ReturnFormDto {
    public Guid     SourceId         { get; set; }
    public string   DocumentNumber   { get; set; } = "";
    public DateTime DocumentDate     { get; set; }
    public string   CounterpartyName { get; set; } = "";
    /// <summary>Rate from the source document, so the form previews the same tax the server will reverse.</summary>
    public decimal  TaxRate          { get; set; }
    public bool     IsTaxInclusive   { get; set; }
    public bool     NothingLeft      => Lines.Count == 0 || Lines.All(l => l.ReturnableQty <= 0);
    /// <summary>
    /// Set when the document can't be returned against at all (e.g. it's cancelled).
    /// Null means the form is usable. Kept separate from NothingLeft, which is the
    /// ordinary "everything has already gone back" case.
    /// </summary>
    public string?  BlockReason      { get; set; }
    public List<ReturnableLineDto> Lines { get; set; } = new();
}
public class CreateCustomerReturnDto {
    public Guid      SaleId     { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string    Reason     { get; set; } = "";
    public string?   Notes      { get; set; }
    public List<CreateReturnItemDto> Items { get; set; } = new();
}
public class CreateSupplierReturnDto {
    public Guid      PurchaseId { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string    Reason     { get; set; } = "";
    public string?   Notes      { get; set; }
    public List<CreateReturnItemDto> Items { get; set; } = new();
}
public class CreateReturnItemDto {
    /// <summary>The SaleItem or PurchaseItem being returned against.</summary>
    public Guid    SourceItemId { get; set; }
    public decimal Qty          { get; set; }
    public string? Notes        { get; set; }
}
public class CustomerReturnDto {
    public Guid      Id             { get; set; }
    public string    ReturnNumber   { get; set; } = "";
    public DateTime  ReturnDate     { get; set; }
    public Guid      SaleId         { get; set; }
    public string    InvoiceNumber  { get; set; } = "";
    public string    CustomerName   { get; set; } = "";
    public decimal   SubTotal       { get; set; }
    public decimal   TaxBase        { get; set; }
    public decimal   TaxRate        { get; set; }
    public decimal   TaxAmount      { get; set; }
    public bool      IsTaxInclusive { get; set; }
    public decimal   GrandTotal     { get; set; }
    /// <summary>Value put back into stock, at the cost the goods left at.</summary>
    public decimal   CostRestocked  { get; set; }
    public string    Reason         { get; set; } = "";
    public string?   Notes          { get; set; }
    public string    Status         { get; set; } = "";
    public string    CreatedBy      { get; set; } = "";
    /// <summary>The credit note this return generated, if it hasn't been cancelled.</summary>
    public string    CreditNoteNumber { get; set; } = "";
    public Guid?     CreditNoteId   { get; set; }
    public string    CreditNoteStatus { get; set; } = "";
    public List<CustomerReturnItemDto> Items { get; set; } = new();
}
public class CustomerReturnItemDto {
    public Guid    Id           { get; set; }
    public Guid    ProductId    { get; set; }
    public string  ProductName  { get; set; } = "";
    public string  SKU          { get; set; } = "";
    public decimal Qty          { get; set; }
    public decimal UnitPrice    { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal CostAtSale   { get; set; }
    public string? Notes        { get; set; }
}
public class SupplierReturnDto {
    public Guid      Id             { get; set; }
    public string    ReturnNumber   { get; set; } = "";
    public DateTime  ReturnDate     { get; set; }
    public Guid      PurchaseId     { get; set; }
    public string    PurchaseNumber { get; set; } = "";
    public string    SupplierName   { get; set; } = "";
    public decimal   SubTotal       { get; set; }
    public decimal   TaxBase        { get; set; }
    public decimal   TaxRate        { get; set; }
    public decimal   TaxAmount      { get; set; }
    public bool      IsTaxInclusive { get; set; }
    public decimal   GrandTotal     { get; set; }
    /// <summary>Inventory value released, at the moving-average cost the goods left at.</summary>
    public decimal   StockReleased  { get; set; }
    /// <summary>
    /// GrandTotal − StockReleased: what the supplier credits us versus what the goods
    /// were carried at. Non-zero is normal once the moving average has moved, and is
    /// shown rather than hidden.
    /// </summary>
    public decimal   ValueDifference => GrandTotal - StockReleased;
    public string    Reason         { get; set; } = "";
    public string?   Notes          { get; set; }
    public string    Status         { get; set; } = "";
    public string    CreatedBy      { get; set; } = "";
    public string    DebitNoteNumber { get; set; } = "";
    public Guid?     DebitNoteId    { get; set; }
    public string    DebitNoteStatus { get; set; } = "";
    public List<SupplierReturnItemDto> Items { get; set; } = new();
}
public class SupplierReturnItemDto {
    public Guid    Id           { get; set; }
    public Guid    ProductId    { get; set; }
    public string  ProductName  { get; set; } = "";
    public string  SKU          { get; set; } = "";
    public decimal Qty          { get; set; }
    public decimal UnitCost     { get; set; }
    public decimal DebitAmount  { get; set; }
    public decimal CostAtReturn { get; set; }
    public string? Notes        { get; set; }
}
/// <summary>
/// One row on the returns list. Shared by both directions — the two sides differ only
/// in which document they point at, so one shape renders both tables.
/// </summary>
public class ReturnListDto {
    public Guid      Id               { get; set; }
    public string    ReturnNumber     { get; set; } = "";
    public DateTime  ReturnDate       { get; set; }
    public bool      IsCustomerReturn { get; set; }
    /// <summary>Invoice number for a sales return, PO number for a purchase return.</summary>
    public string    SourceDocument   { get; set; } = "";
    public Guid      SourceId         { get; set; }
    /// <summary>Customer or supplier, whichever side this is.</summary>
    public string    CounterpartyName { get; set; } = "";
    public int       LineCount        { get; set; }
    public decimal   GrandTotal       { get; set; }
    public string    Reason           { get; set; } = "";
    public string    Status           { get; set; } = "";
}

// ── Credit / debit notes ──────────────────────────────────────────────────────
public class CreditNoteDto {
    public Guid      Id             { get; set; }
    public string    DocumentNumber { get; set; } = "";
    public string    Type           { get; set; } = "";
    public bool      IsCredit       { get; set; }
    public string    Category       { get; set; } = "";
    public DateTime  NoteDate       { get; set; }
    /// <summary>Customer on a credit note, supplier on a debit note.</summary>
    public string    CounterpartyName { get; set; } = "";
    public decimal   TaxBase        { get; set; }
    public decimal   TaxRate        { get; set; }
    public decimal   TaxAmount      { get; set; }
    public decimal   Amount         { get; set; }
    /// <summary>Source document for display — invoice, PO or return number. Empty when standalone.</summary>
    public string    SourceDocument { get; set; } = "";
    public Guid?     SourceSaleId           { get; set; }
    public Guid?     SourcePurchaseId       { get; set; }
    public Guid?     SourceCustomerReturnId { get; set; }
    public Guid?     SourceSupplierReturnId { get; set; }
    public string    Status         { get; set; } = "";
    public bool      IsOpen         { get; set; }
    public DateTime? SettledDate    { get; set; }
    public string?   SettlementNotes{ get; set; }
    public string    Reason         { get; set; } = "";
    public string?   Notes          { get; set; }
    public string    CreatedBy      { get; set; } = "";
}
public class CreateCreditNoteDto {
    public CreditDebitType    Type     { get; set; } = CreditDebitType.Credit;
    public CreditNoteCategory Category { get; set; } = CreditNoteCategory.Other;
    public DateTime? NoteDate   { get; set; }
    /// <summary>Required for a credit note. Ignored for a debit note.</summary>
    public Guid?     CustomerId { get; set; }
    /// <summary>Required for a debit note. Ignored for a credit note.</summary>
    public Guid?     SupplierId { get; set; }
    /// <summary>Face value as entered.</summary>
    public decimal   Amount     { get; set; }
    /// <summary>True if Amount already includes PPN; false to add it on top.</summary>
    public bool      IsTaxInclusive { get; set; } = true;
    /// <summary>Optional link to the invoice or PO being adjusted. Sets the tax rate when given.</summary>
    public Guid?     SourceSaleId     { get; set; }
    public Guid?     SourcePurchaseId { get; set; }
    public string    Reason     { get; set; } = "";
    public string?   Notes      { get; set; }
}
public class SettleCreditNoteDto {
    public Guid    Id              { get; set; }
    /// <summary>How it was settled — applied to which invoice, or refunded in cash.</summary>
    public string? SettlementNotes { get; set; }
}

// ── Commission ────────────────────────────────────────────────────────────────
public class CommissionRuleDto {
    public Guid    Id            { get; set; }
    public string  Name          { get; set; } = "";
    public Guid?   SalesPersonId { get; set; }
    /// <summary>Empty = all salespeople.</summary>
    public string  SalesPersonName { get; set; } = "";
    public Guid?   ProductId     { get; set; }
    public string  ProductName   { get; set; } = "";
    public string? Category      { get; set; }
    public decimal Rate          { get; set; }
    public int     Priority      { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo   { get; set; }
    public bool    IsActive      { get; set; } = true;
    public bool    InUse         { get; set; }
}
public class CommissionAccrualDto {
    public Guid     Id            { get; set; }
    public DateTime AccrualDate   { get; set; }
    public string   SalesPersonName { get; set; } = "";
    public Guid     SalesPersonId { get; set; }
    public string   RuleName      { get; set; } = "";
    public string   InvoiceNumber { get; set; } = "";
    public Guid     SaleId        { get; set; }
    public string   ProductName   { get; set; } = "";
    public decimal  BaseAmount    { get; set; }
    public decimal  Rate          { get; set; }
    public decimal  Amount        { get; set; }
    public bool     IsPaid        { get; set; }
    public bool     IsVoided      { get; set; }
}
/// <summary>A salesperson row on the payout landing page.</summary>
public class CommissionUnpaidDto {
    public Guid    SalesPersonId   { get; set; }
    public string  SalesPersonName { get; set; } = "";
    public int     AccrualCount    { get; set; }
    public decimal Amount          { get; set; }
}
public class PayoutCommissionDto {
    public Guid    SalesPersonId { get; set; }
    public string? Notes         { get; set; }
}
public class CommissionPayoutDto {
    public Guid     Id            { get; set; }
    public DateTime PayoutDate    { get; set; }
    public string   SalesPersonName { get; set; } = "";
    public decimal  Amount        { get; set; }
    public string?  Notes         { get; set; }
    public int      AccrualCount  { get; set; }
    public string   CreatedBy     { get; set; } = "";
}

// ── Rebates ───────────────────────────────────────────────────────────────────
public class RebateRuleDto {
    public Guid   Id         { get; set; }
    public string Name       { get; set; } = "";
    public Guid   SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public Guid?  ProductId  { get; set; }
    /// <summary>Empty = applies to all of the supplier's products.</summary>
    public string ProductName { get; set; } = "";

    public RebateConditionType ConditionType { get; set; }
    public decimal? ThresholdQty      { get; set; }
    public decimal? ThresholdValue    { get; set; }
    public decimal? ReferenceCost     { get; set; }
    public int?     OnTimePaymentDays { get; set; }
    public DateTime? PeriodStart      { get; set; }
    public DateTime? PeriodEnd        { get; set; }

    public RebateRewardType RewardType { get; set; }
    public decimal? RewardRate      { get; set; }
    public decimal? RewardAmount    { get; set; }
    public Guid?    RewardProductId { get; set; }
    public string   RewardProductName { get; set; } = "";
    public decimal? RewardQty       { get; set; }

    public bool IsActive { get; set; } = true;
    public bool InUse    { get; set; }
}
public class RebateAccrualDto {
    public Guid     Id             { get; set; }
    public DateTime AccrualDate    { get; set; }
    public string   RuleName       { get; set; } = "";
    public string   SupplierName   { get; set; } = "";
    public Guid     SupplierId     { get; set; }
    public string   RewardType     { get; set; } = "";
    public string   PurchaseNumber { get; set; } = "";
    public Guid?    PurchaseId     { get; set; }
    public decimal  Qty            { get; set; }
    public decimal  Amount         { get; set; }
    public bool     IsSettled      { get; set; }
    public bool     IsVoided       { get; set; }
}
/// <summary>A supplier row on the rebate-claim landing page.</summary>
public class RebateOutstandingDto {
    public Guid    SupplierId       { get; set; }
    public string  SupplierName     { get; set; } = "";
    public int     CashAccrualCount { get; set; }
    public decimal CashAmount       { get; set; }
    public int     InKindAccrualCount { get; set; }
    public int     LuckyDrawCount   { get; set; }
}
/// <summary>Cash settlement of the outstanding cash accruals for one supplier.</summary>
public class RealizeCashDto {
    public Guid   SupplierId  { get; set; }
    /// <summary>Gross settled amount agreed with the supplier, before withholding.</summary>
    public decimal GrossAmount { get; set; }
    public string? ReferenceId { get; set; }
    public string? Notes       { get; set; }
}
/// <summary>Settlement of a single LuckyDraw accrual, whose value is only now known.</summary>
public class RealizeLuckyDrawDto {
    public Guid    AccrualId   { get; set; }
    public decimal GrossAmount { get; set; }
    public string? ReferenceId { get; set; }
    public string? Notes       { get; set; }
}
public class RealizeInKindDto {
    public Guid    AccrualId { get; set; }
    public string? ReferenceId { get; set; }
    public string? Notes     { get; set; }
}
public class RebateRealizationDto {
    public Guid     Id              { get; set; }
    public DateTime RealizationDate { get; set; }
    public string   SupplierName    { get; set; } = "";
    public string   RewardType      { get; set; } = "";
    public decimal? GrossAmount     { get; set; }
    public decimal? WithholdingRate { get; set; }
    public decimal? WithholdingAmount { get; set; }
    public decimal? NetAmount       { get; set; }
    public string   InKindProductName { get; set; } = "";
    public decimal? InKindQty       { get; set; }
    public string?  ReferenceId     { get; set; }
    public string?  Notes           { get; set; }
    public int      AccrualCount    { get; set; }
}

// ── Sales people ──────────────────────────────────────────────────────────────
public class SalesPersonDto {
    public Guid    Id       { get; set; }
    public string  Name     { get; set; } = "";
    public string? Phone    { get; set; }
    public bool    IsActive { get; set; } = true;
    /// <summary>True when a posted sale credits them — the UI hides Delete.</summary>
    public bool    InUse    { get; set; }
}

// ── Sales ─────────────────────────────────────────────────────────────────────
public class CreateSaleDto {
    public Guid        CustomerId   { get; set; }
    /// <summary>Business date of the sale. Null = today. May be backdated so a day's
    /// paperwork can be entered late (power outage, manual write-up); a future date is
    /// rejected — see SaleService.CreateAsync for why.</summary>
    public DateTime?   SaleDate     { get; set; }
    public PaymentType PaymentType  { get; set; }
    /// <summary>Credit term. Null = open credit with no agreed due date. Ignored for Cash.</summary>
    public Guid?       PaymentTermId { get; set; }
    /// <summary>Who to credit with the sale. Null = unattributed (walk-in).</summary>
    public Guid?       SalesPersonId { get; set; }
    /// <summary>Flat whole-invoice discount. Ignored when InvoiceDiscountPercent is supplied.</summary>
    public decimal     InvoiceDiscountAmount  { get; set; }
    /// <summary>Whole-invoice discount as a percentage of the post-line-discount total (0–100).</summary>
    public decimal?    InvoiceDiscountPercent { get; set; }
    public string?     Notes        { get; set; }
    /// <summary>True if the entered prices already include PPN; false to add PPN on top.</summary>
    public bool        IsTaxInclusive { get; set; }
    public List<CreateSaleItemDto> Items { get; set; } = new();
}
public class CreateSaleItemDto {
    public Guid    ProductId      { get; set; }
    public decimal Qty            { get; set; }
    public decimal UnitPrice      { get; set; }
    /// <summary>Flat discount per unit. Ignored when DiscountPercent is supplied.</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>Discount as a percentage of unit price (0–100). Takes precedence over DiscountAmount.</summary>
    public decimal? DiscountPercent{ get; set; }
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
    /// <summary>Term name, e.g. "TOP 30". Empty for Cash or open credit.</summary>
    public string    PaymentTermName { get; set; } = "";
    /// <summary>Credited salesperson. Empty when the sale is unattributed.</summary>
    public string    SalesPersonName { get; set; } = "";
    public DateTime? DueDate       { get; set; }
    public decimal   SubTotal      { get; set; }
    public decimal   DiscountTotal { get; set; }
    public decimal   InvoiceDiscountAmount  { get; set; }
    public decimal?  InvoiceDiscountPercent { get; set; }
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
    /// <summary>Percent as entered, when given that way. Null = flat amount.</summary>
    public decimal? DiscountPercent{ get; set; }
    public decimal  LineTotal      { get; set; }
    /// <summary>This line's share of the invoice-level discount.</summary>
    public decimal  AllocatedInvoiceDiscount { get; set; }
    /// <summary>LineTotal net of the allocated invoice discount — the real revenue for this line.</summary>
    public decimal  NetLineTotal   => LineTotal - AllocatedInvoiceDiscount;
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
    public string   SalesPersonName { get; set; } = "";
    public string   PaymentType   { get; set; } = "";
    /// <summary>Term name, e.g. "TOP 30". Empty for Cash and for open credit with no agreed term.</summary>
    public string   PaymentTermName { get; set; } = "";
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

// ── Statements & multi-document settlement ────────────────────────────────────
/// <summary>One open document on a statement of account, with what's still owed on it.</summary>
public class StatementLineDto {
    public Guid      DocumentId     { get; set; }
    /// <summary>Invoice number (AR) or PO number (AP).</summary>
    public string    DocumentNumber { get; set; } = "";
    /// <summary>The counterparty's own reference, when there is one. AP only.</summary>
    public string?   TheirReference { get; set; }
    public DateTime  DocumentDate   { get; set; }
    public DateTime? DueDate        { get; set; }
    public decimal   GrandTotal     { get; set; }
    public decimal   AmountPaid     { get; set; }
    public decimal   BalanceDue     => GrandTotal - AmountPaid;
    public bool      IsOverdue      => DueDate.HasValue && DueDate.Value.Date < DateTime.UtcNow.Date && BalanceDue > 0;
}
/// <summary>An open credit/debit note that can be netted off this settlement.</summary>
public class StatementNoteDto {
    public Guid     CreditNoteId   { get; set; }
    public string   DocumentNumber { get; set; } = "";
    public DateTime NoteDate       { get; set; }
    public string   Category       { get; set; } = "";
    public decimal  Amount         { get; set; }
    public string   Reason         { get; set; } = "";
}
/// <summary>
/// A counterparty's statement of account: every open document, plus the notes that reduce
/// what actually changes hands. Shared by both directions — a customer statement and a
/// supplier statement differ only in which documents they list.
/// </summary>
public class PaymentStatementDto {
    public Guid     CounterpartyId   { get; set; }
    public string   CounterpartyName { get; set; } = "";
    public string?  Phone            { get; set; }
    public List<StatementLineDto> Lines     { get; set; } = new();
    /// <summary>Open credit notes (AR) or debit notes (AP) held against this counterparty.</summary>
    public List<StatementNoteDto> OpenNotes { get; set; } = new();
    /// <summary>Total still owed across every listed document, before any notes.</summary>
    public decimal  TotalDue         => Lines.Sum(l => l.BalanceDue);
    public decimal  OpenNotesTotal   => OpenNotes.Sum(n => n.Amount);
    /// <summary>
    /// Supplier statements only. Outstanding cash rebate for this supplier — shown so the
    /// figure isn't invisible, but deliberately NOT settled here: rebate reconciles on its
    /// own periodic cadence against the supplier's own sheet, not against a PO payment run.
    /// </summary>
    public decimal? OutstandingRebateAmount { get; set; }
    public bool     NothingOpen      => Lines.Count == 0 && OpenNotes.Count == 0;
}
public class RecordSaleBatchPaymentDto {
    public Guid    CustomerId { get; set; }
    public string? Notes      { get; set; }
    public List<SaleBatchPaymentLineDto> Lines { get; set; } = new();
    /// <summary>Open credit notes to net off this settlement. Applied in full or not at all.</summary>
    public List<Guid> ApplyCreditNoteIds { get; set; } = new();
}
public class SaleBatchPaymentLineDto {
    public Guid    SaleId { get; set; }
    public decimal Amount { get; set; }
}
public class RecordSupplierBatchPaymentDto {
    public Guid    SupplierId { get; set; }
    public string? Notes      { get; set; }
    public List<SupplierBatchPaymentLineDto> Lines { get; set; } = new();
    /// <summary>Open debit notes to net off this settlement. Applied in full or not at all.</summary>
    public List<Guid> ApplyCreditNoteIds { get; set; } = new();
}
public class SupplierBatchPaymentLineDto {
    public Guid    PurchaseId { get; set; }
    public decimal Amount     { get; set; }
}
public class PaymentBatchDto {
    public Guid      Id                 { get; set; }
    public string    BatchNumber        { get; set; } = "";
    public string    Direction          { get; set; } = "";
    public bool      IsReceived         { get; set; }
    public DateTime  BatchDate          { get; set; }
    public string    CounterpartyName   { get; set; } = "";
    public decimal   GrossAmount        { get; set; }
    public decimal   NotesAppliedAmount { get; set; }
    /// <summary>The cash that actually changed hands: gross less any notes netted off.</summary>
    public decimal   NetAmount          { get; set; }
    public int       DocumentCount      { get; set; }
    public int       NoteCount          { get; set; }
    public string?   Notes              { get; set; }
    public string    CreatedBy          { get; set; } = "";
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
/// Commercial P&amp;L for a period, complete as of Step 9: Penjualan → HPP →
/// Laba Kotor → Biaya Usaha + Komisi → Laba Usaha → rebate income and purchase-return
/// variance → <see cref="NetProfit"/>. Every figure is accrual-basis and excludes
/// cancelled documents. <see cref="IsComplete"/> stays as the honest signal for any
/// section that still has no data source.
/// </summary>
public class ProfitAndLossDto {
    public DateTime From          { get; set; }
    public DateTime To            { get; set; }
    public int      InvoiceCount  { get; set; }

    /// <summary>Penjualan before returns — net of PPN. See SalesPeriodTotals for why.</summary>
    public decimal  Revenue       { get; set; }
    /// <summary>Retur Penjualan, ex-PPN — the credit given back to customers.</summary>
    public decimal  SalesReturns  { get; set; }
    public int      SalesReturnCount { get; set; }
    /// <summary>Penjualan Bersih — what was actually sold and kept.</summary>
    public decimal  NetRevenue    => Revenue - SalesReturns;

    /// <summary>HPP before returns — from each line's cost snapshot at sale time.</summary>
    public decimal  Cogs          { get; set; }
    /// <summary>
    /// Cost of returned goods put back into stock. Reverses at the same
    /// <c>CostAtSale</c> the original sale expensed, so a full return of an invoice
    /// nets its margin to exactly zero rather than leaving a cost residual.
    /// </summary>
    public decimal  SalesReturnCogs { get; set; }
    public decimal  NetCogs       => Cogs - SalesReturnCogs;

    public decimal  GrossProfit   => NetRevenue - NetCogs;
    public decimal  GrossMarginPct=> NetRevenue == 0 ? 0 : GrossProfit / NetRevenue * 100m;

    /// <summary>Biaya Usaha, broken down by category.</summary>
    public List<ProfitAndLossExpenseLineDto> ExpenseLines { get; set; } = new();
    public decimal  OperatingExpenses => ExpenseLines.Sum(l => l.Amount);

    /// <summary>
    /// Komisi Penjualan accrued in the period. A selling expense, kept off
    /// <see cref="ExpenseLines"/> because it is generated by the commission engine,
    /// not entered as an <c>Expense</c> against a category.
    /// </summary>
    public decimal  CommissionExpense { get; set; }
    public int      CommissionAccrualCount { get; set; }

    /// <summary>Laba Usaha — gross profit less operating expenses and commission.</summary>
    public decimal  OperatingProfit   => GrossProfit - OperatingExpenses - CommissionExpense;
    /// <summary>
    /// Expenses in categories the consultant adds back as a fiscal correction.
    /// Memo only — SimpleERP reports commercial profit, not fiscal profit.
    /// </summary>
    public decimal  NonDeductibleExpenses => ExpenseLines.Where(l => !l.IsTaxDeductible).Sum(l => l.Amount);

    // ── Pendapatan / Beban Luar Usaha ────────────────────────────────────────
    /// <summary>
    /// Pendapatan Rebat, gross of the 15% withholding (which is a prepaid tax credit,
    /// not a cost of earning the rebate). Recognised when it accrues, plus LuckyDraw
    /// settled in the period — that reward type accrues zero because its value can't
    /// be known until it's drawn, so settlement is the first point it can be booked.
    /// </summary>
    public decimal  RebateIncome        { get; set; }
    public decimal  RebateAccrued       { get; set; }
    public decimal  RebateLuckyDrawRealized { get; set; }
    public int      RebateAccrualCount  { get; set; }
    /// <summary>
    /// In-kind rebate accruals in the period. Count only, deliberately no amount:
    /// free goods enter stock at cost and show up as margin when sold, so booking
    /// them as income here as well would count the same benefit twice.
    /// </summary>
    public int      RebateInKindCount   { get; set; }

    /// <summary>
    /// Selisih Retur Pembelian — what the supplier credited back, less the inventory
    /// value the goods actually left at. Non-zero because the moving-average cost
    /// drifts from the price originally billed; it has to land somewhere, or inventory
    /// and payables stop reconciling. Positive = credited more than the goods were
    /// carried at.
    /// </summary>
    public decimal  PurchaseReturnVariance { get; set; }
    public decimal  PurchaseReturnCredit   { get; set; }
    public decimal  PurchaseReturnStockValue { get; set; }
    public int      PurchaseReturnCount    { get; set; }

    /// <summary>Laba Bersih — the bottom line, now that every module feeds this report.</summary>
    public decimal  NetProfit => OperatingProfit + RebateIncome + PurchaseReturnVariance;
    public decimal  NetMarginPct => NetRevenue == 0 ? 0 : NetProfit / NetRevenue * 100m;

    // ── PPN memo ─────────────────────────────────────────────────────────────
    /// <summary>PPN collected on sales. A memo line: a liability owed on, not income.</summary>
    public decimal  TaxCollected  { get; set; }
    /// <summary>PPN reversed out by sales returns, reducing what's owed.</summary>
    public decimal  TaxOnSalesReturns { get; set; }
    /// <summary>PPN paid on purchases (input tax) — reclaimable, so never a cost.</summary>
    public decimal  TaxPaid       { get; set; }
    /// <summary>PPN reversed out by purchase returns, reducing what's reclaimable.</summary>
    public decimal  TaxOnPurchaseReturns { get; set; }
    /// <summary>
    /// Net PPN position for the period: output tax kept, less input tax reclaimable.
    /// Positive = owed to the government. Indicative only — the monthly SPT Masa is
    /// the consultant's filing, and this doesn't model carry-forward credits.
    /// </summary>
    public decimal  NetVatPosition => (TaxCollected - TaxOnSalesReturns)
                                    - (TaxPaid - TaxOnPurchaseReturns);
    /// <summary>Tax-inclusive invoiced total, for tying back to cash and AR.</summary>
    public decimal  GrossSales    { get; set; }

    /// <summary>Sections that have no data source yet, shown so the gap is visible
    /// in the report rather than silently omitted.</summary>
    public List<string> PendingSections { get; set; } = new();
    public bool     IsComplete    => PendingSections.Count == 0;
}
public class ProfitAndLossExpenseLineDto {
    public string  CategoryName    { get; set; } = "";
    public bool    IsTaxDeductible { get; set; } = true;
    public int     EntryCount      { get; set; }
    public decimal Amount          { get; set; }
}

/// <summary>
/// Where the business stands right now — a point-in-time position, not a period
/// report, so it takes no date range.
///
/// Deliberately <em>not</em> called a balance sheet (Neraca): a real one needs
/// capital, retained earnings and a proven debit=credit identity, which requires the
/// double-entry GL this project decided against. This shows the five real balances
/// the app can derive and prove from source documents, and says plainly that it
/// doesn't balance to zero because it isn't trying to.
/// </summary>
public class PositionSummaryDto {
    public DateTime AsOf { get; set; }

    // ── What the business holds ──────────────────────────────────────────────
    /// <summary>Stock on hand at the latest stock-in cost. Matches the Inventory screen.</summary>
    public decimal InventoryValue     { get; set; }
    public int     ProductsInStock    { get; set; }

    /// <summary>Customer invoices still owing, tax-inclusive, before netting credit notes.</summary>
    public decimal ReceivablesGross   { get; set; }
    public int     OpenInvoices       { get; set; }
    public decimal ReceivablesOverdue { get; set; }
    /// <summary>
    /// Open credit notes owed back to customers. Posted invoices are never edited, so
    /// AR is "invoice balances minus still-Open credit notes" in aggregate. Note this
    /// nets across the whole ledger, not invoice by invoice — per-invoice application
    /// is a known gap.
    /// </summary>
    public decimal OpenCreditNotes    { get; set; }
    public decimal ReceivablesNet     => ReceivablesGross - OpenCreditNotes;

    /// <summary>
    /// Rebate earned but not yet claimed from suppliers — a real asset that sat
    /// untracked before Step 6. Gross of withholding, cash-valued rewards only.
    /// </summary>
    public decimal UnclaimedRebate    { get; set; }
    public int     UnclaimedRebateCount { get; set; }
    /// <summary>In-kind and lucky-draw accruals outstanding — carried as counts, since neither has a cash value yet.</summary>
    public int     RebateInKindOutstanding    { get; set; }
    public int     RebateLuckyDrawOutstanding { get; set; }

    public decimal TotalAssets => InventoryValue + ReceivablesNet + UnclaimedRebate;

    // ── What the business owes ───────────────────────────────────────────────
    /// <summary>Supplier invoices still owing, tax-inclusive, before netting debit notes.</summary>
    public decimal PayablesGross      { get; set; }
    public int     OpenPurchases      { get; set; }
    public decimal PayablesOverdue    { get; set; }
    /// <summary>Open debit notes owed back by suppliers, netting off AP the same way.</summary>
    public decimal OpenDebitNotes     { get; set; }
    public decimal PayablesNet        => PayablesGross - OpenDebitNotes;

    /// <summary>Commission earned by salespeople but not yet paid out.</summary>
    public decimal CommissionPayable  { get; set; }
    public int     CommissionPayableCount { get; set; }

    public decimal TotalLiabilities => PayablesNet + CommissionPayable;

    /// <summary>
    /// Assets less liabilities, over the five lines this report can actually derive.
    /// Not equity: cash at bank, fixed assets, capital and tax positions are all
    /// outside the app, so this is a working-position indicator, not a net worth.
    /// </summary>
    public decimal NetPosition => TotalAssets - TotalLiabilities;
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
    /// <summary>Rebate withholding rate as a percentage (15 = 15%). Stored as a fraction on the entity.</summary>
    public decimal RebateWithholdingPercent { get; set; } = 15m;
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
