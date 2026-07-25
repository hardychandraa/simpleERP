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
    SupplierReturn = 7
}
