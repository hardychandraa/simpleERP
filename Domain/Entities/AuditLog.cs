namespace SimpleERP.Domain.Entities;

/// <summary>Immutable audit trail. Never update or delete rows.</summary>
public class AuditLog
{
    public long     Id        { get; set; }          // auto-increment for ordering
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// The signed-in username that caused the event. Rows written before access control
    /// existed carry the literal "staff" — every account was anonymous then, so those
    /// entries genuinely cannot be attributed to a person.
    /// </summary>
    public string   User      { get; set; } = string.Empty;
    public string   Action    { get; set; } = string.Empty;   // e.g. "Sale.Create"
    public string?  Detail    { get; set; }                   // e.g. invoice number
    public string?  IpAddress { get; set; }
}
