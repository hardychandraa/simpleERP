namespace SimpleERP.Domain.Entities;

/// <summary>
/// Editable credit terms (TOP 30, COD, …) — the single source of truth for how long a
/// credit transaction has to pay, and the only input to its due date.
///
/// Master data rather than an enum because adding a term the business agrees with a
/// customer or supplier shouldn't require a code change and redeploy. The hardcoded
/// PaymentType.TOP30–TOP90 members that used to duplicate this were retired on
/// 2026-07-30 — see PaymentType for why those numbers stay burnt.
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
