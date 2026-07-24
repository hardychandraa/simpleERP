namespace SimpleERP.Domain.Entities;

public class Product
{
    public Guid     Id                    { get; set; }
    public string   Name                  { get; set; } = string.Empty;
    public string   SKU                   { get; set; } = string.Empty;
    public decimal  UnitPrice             { get; set; }
    public string?  Category              { get; set; }
    public int?     DefaultWarrantyMonths { get; set; }
    public int      LowStockThreshold     { get; set; } = 2;
    public bool     IsActive              { get; set; } = true;
    public DateTime CreatedAt             { get; set; } = DateTime.UtcNow;

    public ICollection<InventoryLedger> InventoryLedgers { get; set; } = new List<InventoryLedger>();
}
