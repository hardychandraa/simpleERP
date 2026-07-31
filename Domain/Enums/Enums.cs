namespace SimpleERP.Domain.Enums;

/// <summary>
/// How a transaction settles: paid now, or on credit. Nothing more.
///
/// The specific credit term (TOP 30, TOP 45, …) is master data in
/// <see cref="Entities.PaymentTerm"/>, referenced by Sale.PaymentTermId /
/// Purchase.PaymentTermId. That table is the single source of truth for due dates.
///
/// Values 3–6 were TOP30/TOP45/TOP60/TOP90 — a duplicate of what PaymentTerm already
/// held, which meant a credit sale carried the term in two places that could disagree.
/// They were retired 2026-07-30; the one posted row still using one was migrated to Due
/// (its PaymentTermId already carried the identical term). The numbers stay burnt:
/// never reuse 3–6 for a new meaning, because posted rows store the number.
/// </summary>
public enum PaymentType
{
    Cash = 1,
    /// <summary>On credit. The agreed term, when there is one, is PaymentTermId.</summary>
    Due  = 2
}

public enum SaleStatus     { Active = 1, Cancelled = 2 }
public enum PurchaseStatus { Active = 1, Cancelled = 2 }
/// <summary>Shared by CustomerReturn and SupplierReturn — a return is posted or reversed, never edited.</summary>
public enum ReturnStatus   { Active = 1, Cancelled = 2 }

/// <summary>
/// Which way money moved in a settlement covering several documents at once.
/// <c>Received</c>: a customer paid us against several invoices (AR).
/// <c>Paid</c>: we paid a supplier against several purchases (AP).
/// Append only.
/// </summary>
public enum PaymentBatchDirection { Received = 1, Paid = 2 }

/// <summary>
/// Which direction a note moves money. One entity carries both rather than two
/// near-identical ones — the codebase already has that mistake once
/// (PaymentRecord vs. the orphaned PaymentCollection).
/// <c>Credit</c>: we owe the customer (a sales return, an overcharge we made).
/// <c>Debit</c>: the supplier owes us (a purchase return, an overcharge they made).
/// </summary>
public enum CreditDebitType { Credit = 1, Debit = 2 }

/// <summary>Why a note was issued. Append only.</summary>
public enum CreditNoteCategory {
    Return           = 1,
    PriceDispute     = 2,
    RebateSettlement = 3,
    BillingError     = 4,
    Other            = 5
}

/// <summary>
/// Whether a note still hangs over the receivable/payable. <c>Open</c> notes are what
/// AR and AP reporting must net off; settling records that it was applied to an invoice
/// or refunded in cash. Append only.
/// </summary>
public enum CreditNoteStatus { Open = 1, Settled = 2, Cancelled = 3 }

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
