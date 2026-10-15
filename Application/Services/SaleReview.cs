namespace SimpleERP.Application.Services;

/// <summary>
/// Why a Staff sale was flagged Perlu dicek (HC, 2026-10-14: G4 price floor and discount limit,
/// G5 dated yesterday, G7 salesperson other than the customer's; 2026-10-15: a credit sale over
/// the customer's plafon or to a customer with overdue invoices). Stored on the sale as coded
/// lines ("code|arg|arg"), so the screen can word them in the reader's language; they can name
/// a product sold below cost, so only Admin ever sees them.
/// </summary>
public static class SaleReview
{
    public const string BelowCost   = "below-cost";    // product
    public const string Discount    = "discount";      // product, percent below the master price
    public const string Backdated   = "backdated";     // sale date (yyyy-MM-dd)
    public const string SalesPerson = "salesperson";   // salesperson chosen, customer's own (or "-")
    public const string CreditLimit = "credit-limit";  // plafon, open balance after this sale (HC 2026-10-15)
    public const string Overdue     = "overdue";       // overdue invoices, oldest invoice, its days past due

    public static string Line(string code, params string[] args) =>
        string.Join('|', new[] { code }.Concat(args.Select(a => a.Replace('|', '/').Replace('\n', ' '))));

    public static IEnumerable<string[]> Parse(string? reasons) =>
        (reasons ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|'));
}
