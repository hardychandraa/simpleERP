namespace SimpleERP.Domain.Entities;
public class Branch {
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Short warehouse code printed on the invoice (e.g. "GD1"). Optional.</summary>
    public string? Code { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
