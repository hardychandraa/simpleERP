namespace SimpleERP.Domain.Entities;

/// <summary>Immutable audit trail. Never update or delete rows.</summary>
public class AuditLog
{
    public long     Id        { get; set; }          // auto-increment for ordering
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string   User      { get; set; } = "staff";
    public string   Action    { get; set; } = string.Empty;   // e.g. "Sale.Create"
    public string?  Detail    { get; set; }                   // e.g. invoice number
    public string?  IpAddress { get; set; }
}
