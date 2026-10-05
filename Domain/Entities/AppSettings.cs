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
    public int PaperColumns { get; set; } = 96;
    /// <summary>
    /// Printable lines per sheet, sent to the printer as its page length so each invoice
    /// starts at the top of the next form. 33 = a 5.5-inch form at 6 lines per inch (the
    /// 9.5 × 5.5 inch, roughly half-A4 continuous form).
    /// </summary>
    public int PaperLines { get; set; } = 33;
    public bool PrinterEnabled { get; set; } = false;
    /// <summary>
    /// PPN rate as a fraction (0.11 = 11%). Deliberately configurable, not a constant —
    /// Indonesian PPN has moved (10% → 11%) and will again. Confirmed with the tax
    /// consultant 2026-07-31; the DPP ("nilai lain") mechanism is still being checked —
    /// see questions.md.
    /// </summary>
    public decimal VatRate { get; set; } = 0.11m;
    /// <summary>
    /// Withholding rate deducted from a rebate settlement before it nets against the
    /// payable, as a fraction (0.15 = 15%). Configurable, not a constant: the real
    /// supplier reconciliation sheet shows a consistent 15% but its legal basis (PPh 23
    /// vs 4(2) vs an internal convention) is unconfirmed — see questions.md — so the
    /// rate must stay editable and could legitimately differ by reward type later.
    /// </summary>
    public decimal RebateWithholdingRate { get; set; } = 0.15m;
}
