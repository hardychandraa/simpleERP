namespace SimpleERP.Application.Services;

/// <summary>
/// Shared money arithmetic for the Step 8 documents (returns and credit/debit notes).
///
/// SaleService and PurchaseService each keep their own inline copy of the tax split,
/// because there the branches are woven into a longer posting routine; this is the
/// extracted form for the newer documents that only need the split itself. If the two
/// ever diverge, the rule below is the authoritative one.
/// </summary>
internal static class MoneyMath
{
    /// <summary>
    /// Splits a net figure into taxable base + tax on a document's own basis.
    ///
    /// The inclusive branch derives the tax by <em>subtraction</em> rather than by a
    /// second rounding, which is what guarantees base + tax == total exactly at any rate.
    /// The exclusive branch rounds the tax once and adds it.
    /// </summary>
    /// <param name="net">
    /// The figure as quoted: already tax-inclusive when <paramref name="taxInclusive"/>
    /// is true, otherwise the pre-tax amount.
    /// </param>
    /// <param name="rate">Rate as a fraction (0.10 = 10%). Zero or less means no tax.</param>
    public static (decimal TaxBase, decimal TaxAmount, decimal GrandTotal) SplitTax(
        decimal net, decimal rate, bool taxInclusive)
    {
        if (rate <= 0m) return (net, 0m, net);

        if (taxInclusive)
        {
            var taxBase = Math.Round(net / (1m + rate), 2, MidpointRounding.AwayFromZero);
            return (taxBase, net - taxBase, net);
        }

        var tax = Math.Round(net * rate, 2, MidpointRounding.AwayFromZero);
        return (net, tax, net + tax);
    }
}
