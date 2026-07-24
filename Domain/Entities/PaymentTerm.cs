namespace SimpleERP.Domain.Entities;

/// <summary>
/// Editable credit terms (TOP 30, COD, …), replacing the hardcoded
/// PaymentType.TOP30–TOP90 enum members for new transactions.
///
/// Master data rather than an enum because adding a term the business agrees with
/// a customer or supplier shouldn't require a code change and redeploy. The old
/// enum members are deliberately left in place for rows that already use them —
/// see PaymentType.
/// </summary>
public class PaymentTerm {
    public Guid   Id       { get; set; }
    /// <summary>Display name, e.g. "TOP 30" or "COD".</summary>
    public string Name     { get; set; } = string.Empty;
    /// <summary>Days from transaction date until payment falls due. 0 = due same day (COD).</summary>
    public int    DueDays  { get; set; }
    /// <summary>Deactivated terms stay selectable on existing rows but not on new ones.</summary>
    public bool   IsActive { get; set; } = true;
    /// <summary>Controls dropdown ordering; ties break by Name.</summary>
    public int    SortOrder{ get; set; }
}
