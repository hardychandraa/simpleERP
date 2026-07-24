using SimpleERP.Domain.Enums;
namespace SimpleERP.Domain.Entities;
public class InventoryLedger {
    public Guid Id { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
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
