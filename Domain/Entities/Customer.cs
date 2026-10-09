namespace SimpleERP.Domain.Entities;
public class Customer {
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    /// <summary>NPWP, as printed on the customer's tax card. Optional; same field name as Supplier.TaxId.</summary>
    public string? TaxId { get; set; }
    /// <summary>
    /// NIK from the KTP. Since 2024 an individual's NIK serves as their NPWP, so a customer may
    /// have a company NPWP, a NIK, or both (HC, 2026-10-09). Optional.
    /// </summary>
    public string? NationalId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Defaults pre-filled on a new sale for this customer (owner's request, 2026-10-06).
    // Only defaults: each sale stores its own salesperson, term and line discounts, so
    // changing them here never touches a document already posted. Mirrors Supplier.PaymentTermId.
    /// <summary>Who usually handles this customer. Null = none pre-selected.</summary>
    public Guid? SalesPersonId { get; set; }
    /// <summary>Usual credit term. Null = a cash customer.</summary>
    public Guid? PaymentTermId { get; set; }
    /// <summary>Usual discount per item, in percent (2 = 2%). Null = no discount.</summary>
    public decimal? DefaultDiscountPercent { get; set; }

    public SalesPerson? SalesPerson { get; set; }
    public PaymentTerm? PaymentTerm { get; set; }
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
