namespace SimpleERP.Web.Pages.Returns;

/// <summary>
/// One row of a return form. The lines are fixed by the source document, so the form
/// binds an indexed list directly rather than posting a JSON blob the way Sales/Purchases
/// Create has to — there are no rows to add or remove, only quantities to fill in.
/// Rows left at zero are dropped before the service is called.
/// </summary>
public class ReturnLineInput
{
    public Guid    SourceItemId { get; set; }
    public decimal Qty          { get; set; }
    public string? Notes        { get; set; }
}
