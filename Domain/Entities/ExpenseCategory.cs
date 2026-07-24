namespace SimpleERP.Domain.Entities;

/// <summary>
/// Operating-expense categories (Biaya Usaha), seeded from the categories the
/// accountant already uses on the annual statement so the P&amp;L lines up with it
/// without a translation step.
/// </summary>
public class ExpenseCategory {
    public Guid   Id       { get; set; }
    public string Name     { get; set; } = string.Empty;
    public bool   IsActive { get; set; } = true;
    /// <summary>
    /// False for categories the tax consultant adds back as a fiscal correction
    /// (e.g. tax penalties). Informational only — SimpleERP produces the
    /// commercial P&amp;L and does not compute fiscal profit itself; this just lets
    /// the report surface the non-deductible total so the correction is visible
    /// rather than being rediscovered at year end.
    /// </summary>
    public bool   IsTaxDeductible { get; set; } = true;
    public int    SortOrder{ get; set; }
}
