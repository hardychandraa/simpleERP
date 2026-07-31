namespace SimpleERP.Web.Services;

/// <summary>
/// Builds the resource key for an enum's display name.
///
/// The Application layer flattens most enums to a raw <c>.ToString()</c> when mapping
/// entities onto DTOs (so <c>SaleDto.Status</c> is already the string <c>"Active"</c>, not
/// a <c>SaleStatus</c>), which means Razor never sees the enum and can't re-derive it.
/// Rather than reworking every DTO mapping — working code, and a change that would ripple
/// through all 14 services — this reconstructs a lookup key from the raw name the DTO
/// already carries:
///
/// <code>@Localizer[EnumDisplay.Key("PaymentType", Model.Sale.PaymentType)]  // -> "PaymentType_Cash"</code>
///
/// The enum type name is passed explicitly because the raw value alone is ambiguous:
/// <c>"Cancelled"</c> belongs to SaleStatus, PurchaseStatus, ReturnStatus and
/// CreditNoteStatus alike, and keeping them as separate keys lets one of them diverge
/// later (e.g. if "Cancelled" ever needs different wording for a return than for a sale)
/// without disturbing the others.
///
/// Works equally for a still-typed enum — <c>EnumDisplay.Key(nameof(RebateConditionType), rule.ConditionType)</c>
/// via the generic overload — so pages that kept the real enum don't need a different idiom.
/// </summary>
public static class EnumDisplay
{
    /// <summary>Key from an enum type name and the raw member name the DTO already carries.</summary>
    public static string Key(string enumTypeName, string? rawValue) =>
        string.IsNullOrWhiteSpace(rawValue) ? string.Empty : $"{enumTypeName}_{rawValue}";

    /// <summary>Key from a still-typed enum value, for the pages that never flattened it.</summary>
    public static string Key<TEnum>(TEnum value) where TEnum : struct, Enum =>
        $"{typeof(TEnum).Name}_{value}";
}
