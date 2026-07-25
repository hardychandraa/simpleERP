namespace SimpleERP.Domain.Enums;

public enum PaymentType
{
    Cash  = 1,
    Due   = 2,
    TOP30 = 3,
    TOP45 = 4,
    TOP60 = 5,
    TOP90 = 6
}

public enum SaleStatus     { Active = 1, Cancelled = 2 }
public enum PurchaseStatus { Active = 1, Cancelled = 2 }

/// <summary>
/// What triggers a rebate. Append only.
/// <c>Volume</c>: buying a product (optionally past a quantity threshold in the rule's
/// period) earns the reward — the common "Rebate/Support %" case.
/// <c>PriceDrop</c>: the billed unit cost came in below a reference cost, so the
/// supplier owes back the difference (price protection).
/// <c>OnTimePayment</c>: paying a purchase on or before its due date earns the reward;
/// evaluated when a SupplierPayment is recorded, not at purchase time.
/// </summary>
public enum RebateConditionType { Volume = 1, PriceDrop = 2, OnTimePayment = 3 }

/// <summary>
/// How a rebate pays out. Append only. "Support" maps onto PercentDiscount and
/// "Voucher" onto FixedCash — no separate schema needed.
/// <c>PercentDiscount</c>: a percentage of the qualifying purchase value.
/// <c>FixedCash</c>: a flat cash amount.
/// <c>CreditNote</c>: a credit against the payable (same arithmetic as cash here).
/// <c>InKindGoods</c>: free stock — realized as an imputed-cost StockIn, not cash.
/// <c>LuckyDraw</c>: amount genuinely unknowable until the supplier settles, so it
/// accrues at zero and only gets a value at realization.
/// </summary>
public enum RebateRewardType {
    PercentDiscount = 1,
    FixedCash       = 2,
    CreditNote      = 3,
    InKindGoods     = 4,
    LuckyDraw       = 5
}

/// <summary>
/// What an InventoryLedger row was caused by. Values are permanent — append only,
/// never renumber, because posted ledger rows store the number.
///
/// <c>Purchase = 1</c> is the manual one-product-at-a-time Stock In page and stays
/// exactly as it was. <c>PurchaseOrder = 6</c> is the multi-line Purchase document,
/// kept deliberately distinct so rebate volume math and AP reporting only ever sum
/// real supplier-invoice activity, never ad-hoc stock corrections.
///
/// <c>CustomerReturn = 5</c> was previously named <c>Return</c>; it was confirmed
/// never assigned anywhere in the codebase, so it was renamed in place rather than
/// wasting the slot. Nothing to migrate.
/// </summary>
public enum ReferenceType {
    Purchase       = 1,
    Sale           = 2,
    Cancel         = 3,
    Adjustment     = 4,
    CustomerReturn = 5,
    PurchaseOrder  = 6,
    SupplierReturn = 7,
    /// <summary>
    /// Free goods received as a rebate reward. Deliberately distinct from
    /// PurchaseOrder so it never counts as purchase volume (which would let free
    /// goods inflate the very thresholds that earned them), while still flowing
    /// through the normal moving-average costing at zero cost.
    /// </summary>
    RebateInKind   = 8
}
