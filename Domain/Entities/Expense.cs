namespace SimpleERP.Domain.Entities;

/// <summary>
/// A single operating expense. Deliberately simple and append-only in spirit:
/// this is the data source for the Biaya Usaha section of the P&amp;L, which had no
/// home in the system before and was reconstructed by hand once a year.
/// </summary>
public class Expense {
    public Guid     Id                { get; set; }
    public DateTime ExpenseDate       { get; set; }
    public Guid     ExpenseCategoryId { get; set; }
    public decimal  Amount            { get; set; }
    /// <summary>What it was for — free text, shown in listings.</summary>
    public string?  Description       { get; set; }
    /// <summary>Receipt/invoice number, so an entry can be traced to paper.</summary>
    public string?  ReferenceNo       { get; set; }
    public string   CreatedBy         { get; set; } = "staff";
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;

    public ExpenseCategory? Category  { get; set; }
}
