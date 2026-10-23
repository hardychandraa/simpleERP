using SimpleERP.Domain.Enums;

namespace SimpleERP.Domain.Entities;

/// <summary>
/// Tukar barang (HC, 2026-10-22): a customer brings back a faulty unit and takes the same
/// product, new, in its place. One document against one invoice, like a sales return, but
/// nothing changes hands: no money, no credit note, no PPN, no commission. The replacement
/// leaves stock and the faulty unit comes back, both at the current average cost, so stock
/// and its value end where they started and Laba Rugi is untouched. The invoice, its HPP and
/// its warranty dates stay exactly as posted (HC: keep the original end date).
///
/// The faulty unit then sits in stock until it goes back to the supplier (Retur Pembelian,
/// whose debit note the supplier's replacement Goods In is paid with) or is written off.
/// A different product is not an exchange: that is a return plus a new sale.
/// </summary>
public class Exchange
{
    public Guid     Id             { get; set; }
    /// <summary>Our own number, TKR-yyMM-nnnn, unique.</summary>
    public string   ExchangeNumber { get; set; } = string.Empty;
    /// <summary>The local day the customer brought the unit in (a calendar day, as ReturnDate).</summary>
    public DateTime ExchangeDate   { get; set; }
    public Guid     SaleId         { get; set; }
    public Guid     BranchId       { get; set; }
    /// <summary>What was wrong. Required, as on a return.</summary>
    public string   Reason         { get; set; } = string.Empty;
    public string?  Notes          { get; set; }
    public ReturnStatus Status     { get; set; } = ReturnStatus.Active;
    public string   CreatedBy      { get; set; } = string.Empty;
    public DateTime CreatedAt      { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Every Staff exchange waits for Admin's check, as Staff returns do (R10): a fake one
    /// puts a "faulty unit" into stock that never came back and hides a missing one.
    /// </summary>
    public bool      NeedsReview { get; set; }
    public string?   ReviewedBy  { get; set; }
    public DateTime? ReviewedAt  { get; set; }

    public Sale?   Sale   { get; set; }
    public Branch? Branch { get; set; }
    public ICollection<ExchangeItem> Items { get; set; } = new List<ExchangeItem>();
}

/// <summary>One invoice line exchanged: how many units, and the serial numbers in the note.</summary>
public class ExchangeItem
{
    public Guid    Id         { get; set; }
    public Guid    ExchangeId { get; set; }
    public Guid    SaleItemId { get; set; }
    public Guid    ProductId  { get; set; }
    public decimal Qty        { get; set; }
    /// <summary>E.g. the faulty unit's and the replacement's serial numbers.</summary>
    public string? Notes      { get; set; }

    public Exchange? Exchange { get; set; }
    public SaleItem? SaleItem { get; set; }
    public Product?  Product  { get; set; }
}
