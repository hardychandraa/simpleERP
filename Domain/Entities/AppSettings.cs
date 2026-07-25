namespace SimpleERP.Domain.Entities;
/// <summary>Single-row config table. Always Id = "default".</summary>
public class AppSettings {
    public string Id { get; set; } = "default";
    public string AppName { get; set; } = "SimpleERP";
    public string StoreName { get; set; } = "My Store";
    public string? StoreAddress { get; set; }
    public string? StorePhone { get; set; }
    public string StoreFooter { get; set; } = "Thank you for your purchase!";
    public string PrinterName { get; set; } = "";
    public int PaperColumns { get; set; } = 80;
    public bool PrinterEnabled { get; set; } = false;
    /// <summary>
    /// PPN rate as a fraction (0.10 = 10%). Deliberately configurable, not a constant —
    /// Indonesian PPN has moved (10% → 11%) and will again. Confirm the current effective
    /// rate with the tax consultant; 10% is only a seeded default.
    /// </summary>
    public decimal VatRate { get; set; } = 0.10m;
    /// <summary>
    /// Withholding rate deducted from a rebate settlement before it nets against the
    /// payable, as a fraction (0.15 = 15%). Configurable, not a constant: the real
    /// supplier reconciliation sheet shows a consistent 15% but its legal basis (PPh 23
    /// vs 4(2) vs an internal convention) is unconfirmed — see questions.md — so the
    /// rate must stay editable and could legitimately differ by reward type later.
    /// </summary>
    public decimal RebateWithholdingRate { get; set; } = 0.15m;
}
