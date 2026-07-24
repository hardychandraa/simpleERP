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

public enum SaleStatus    { Active = 1, Cancelled = 2 }
public enum ReferenceType { Purchase = 1, Sale = 2, Cancel = 3, Adjustment = 4, Return = 5 }
