namespace SimpleERP.Domain.Entities;

/// <summary>
/// A person credited with a sale, for attribution and (from Step 7) commission.
///
/// Deliberately decoupled from login/authentication, which stays out of scope: the
/// people who close deals are not necessarily the people who type the invoice, so
/// tying attribution to a future user account would have recorded the wrong name.
/// Sale.SalesPersonId stays nullable — a sale with nobody to credit is normal.
/// </summary>
public class SalesPerson {
    public Guid    Id       { get; set; }
    public string  Name     { get; set; } = string.Empty;
    public string? Phone    { get; set; }
    /// <summary>Deactivated people stay on the sales they closed but drop off new ones.</summary>
    public bool    IsActive { get; set; } = true;
}
