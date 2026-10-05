namespace SimpleERP.Infrastructure.Data;

/// <summary>
/// The last number issued under one document prefix (<c>INV-202610</c>, <c>DN-202610</c>,
/// <c>STL-P-202610</c>…). A persistence detail of number generation, not a business
/// concept, which is why it lives here rather than in Domain. One row per prefix, so a
/// new month starts a new row and the sequence restarts at 0001 on its own.
/// </summary>
public class DocumentSequence
{
    public string Prefix     { get; set; } = string.Empty;
    public int    LastNumber { get; set; }
}
