using SimpleERP.Domain.Enums;
namespace SimpleERP.Domain.Entities;
public class InventoryLedger {
    public Guid Id { get; set; }
    /// <summary>
    /// When the movement happened in business terms: the document's own date (a sale, purchase
    /// or return backdated to 1 Oct is dated 1 Oct), so the stock card reads by date like a
    /// paper kartu stok. Today's documents keep the real clock time.
    /// </summary>
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// When the row was written (UTC), strictly increasing across rows (the repository stamps
    /// it). The moving-average cost is "the latest stock-in row", and that has to mean the
    /// latest *posted*: each row's UnitCost was computed from the rows before it in posting
    /// order, so ordering by a backdated TransactionDate would pick a stale average.
    /// </summary>
    public DateTime EnteredAt { get; set; } = DateTime.UtcNow;
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public ReferenceType ReferenceType { get; set; }
    public Guid ReferenceId { get; set; }
    public decimal QtyIn { get; set; }
    public decimal QtyOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public Product? Product { get; set; }
    public Branch? Branch { get; set; }
}
