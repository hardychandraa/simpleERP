namespace SimpleERP.Web.Pages.Purchases;

/// <summary>
/// One settleable row on a statement. The rows are fixed by what's currently open, so the
/// form binds an indexed list directly — same shape as the return forms, no JSON blob.
/// Rows left at zero are dropped before the service is called.
/// </summary>
public class StatementLineInput
{
    public Guid    DocumentId { get; set; }
    public decimal Amount     { get; set; }
}
