using System.Text;
using SimpleERP.Application.DTOs;

namespace SimpleERP.Web.Services;

public static class EscpBuilder
{
    private static byte[] INIT()       => new byte[]{ 0x1B, (byte)'@' };
    private static byte[] PICA()       => new byte[]{ 0x1B, (byte)'P' };
    private static byte[] ELITE()      => new byte[]{ 0x1B, (byte)'M' };
    private static byte[] BOLD_ON()    => new byte[]{ 0x1B, (byte)'E' };
    private static byte[] BOLD_OFF()   => new byte[]{ 0x1B, (byte)'F' };
    private static byte[] DBL_ON()     => new byte[]{ 0x1B, (byte)'G' };
    private static byte[] DBL_OFF()    => new byte[]{ 0x1B, (byte)'H' };
    private static byte[] FF()         => new byte[]{ 0x0C };

    public static byte[] BuildInvoice(SaleDto sale, AppSettingsDto cfg)
        => Build(sale, cfg.StoreName, cfg.StoreAddress, cfg.StorePhone,
                 cfg.StoreFooter, cfg.PaperColumns);

    public static byte[] BuildInvoice(SaleDto sale, Domain.Entities.AppSettings cfg)
        => Build(sale, cfg.StoreName, cfg.StoreAddress, cfg.StorePhone,
                 cfg.StoreFooter ?? "", cfg.PaperColumns);

    private static byte[] Build(SaleDto sale,
        string storeName, string? storeAddr, string? storePhone,
        string footer, int paperColumns)
    {
        int cols = paperColumns > 80 ? 132 : 80;
        var buf  = new List<byte[]>();
        void Ln(string text = "") => buf.Add(Encoding.ASCII.GetBytes(text + "\r\n"));
        void Sep(char c = '-')    => Ln(new string(c, cols));
        string Center(string s)   => s.Length >= cols ? s : s.PadLeft((cols + s.Length) / 2);
        string Trunc(string s, int w) => s.Length > w ? s[..w] : s;

        buf.Add(INIT());
        buf.Add(cols > 80 ? PICA() : ELITE());

        buf.Add(BOLD_ON()); buf.Add(DBL_ON());
        Ln(Center(storeName));
        buf.Add(BOLD_OFF()); buf.Add(DBL_OFF());
        if (!string.IsNullOrEmpty(storeAddr))  Ln(Center(storeAddr));
        if (!string.IsNullOrEmpty(storePhone)) Ln(Center("Tel: " + storePhone));

        Sep('=');
        Ln($"Invoice : {sale.InvoiceNumber}");
        Ln($"Date    : {sale.SaleDate.ToLocalTime():dd-MMM-yyyy  HH:mm}");
        if (sale.DueDate.HasValue)
            Ln($"Due     : {sale.DueDate.Value:dd-MMM-yyyy}  ({sale.PaymentType})");
        Ln($"Customer: {sale.CustomerName}");
        if (!string.IsNullOrEmpty(sale.CustomerPhone)) Ln($"Phone   : {sale.CustomerPhone}");
        Ln($"Payment : {sale.PaymentType}");
        if (sale.Status == "Cancelled") { buf.Add(BOLD_ON()); Ln("*** CANCELLED ***"); buf.Add(BOLD_OFF()); }
        Sep('-');

        int qW=4, pW=10, tW=10, nW=cols-qW-pW-tW-3;
        buf.Add(BOLD_ON());
        Ln($"{"QTY".PadLeft(qW)} {"DESCRIPTION".PadRight(nW)} {"PRICE".PadLeft(pW)} {"TOTAL".PadLeft(tW)}");
        buf.Add(BOLD_OFF());
        Sep('-');

        foreach (var item in sale.Items) {
            string name = Trunc(item.ProductName, nW).PadRight(nW);
            Ln($"{item.Qty.ToString("N0").PadLeft(qW)} {name} {item.UnitPrice.ToString("N0").PadLeft(pW)} {item.LineTotal.ToString("N0").PadLeft(tW)}");
            if (item.DiscountAmount > 0)      Ln($"  Disc: -{item.DiscountAmount:N0}");
            if (!string.IsNullOrEmpty(item.Notes))       Ln($"  Note: {Trunc(item.Notes, cols - 8)}");
            if (!string.IsNullOrEmpty(item.PriceReason)) Ln($"  Adj : {Trunc(item.PriceReason, cols - 8)}");
            if (item.WarrantyExpiry.HasValue)  Ln($"  Warranty: {item.WarrantyExpiry.Value.ToLocalTime():dd-MMM-yyyy}");
        }
        Sep('-');

        int lblW = cols - tW - 1;
        Ln($"{"SUBTOTAL :".PadLeft(lblW)} {sale.SubTotal.ToString("N0").PadLeft(tW)}");
        if (sale.DiscountTotal > 0)
            Ln($"{"DISCOUNT :".PadLeft(lblW)} {("-"+sale.DiscountTotal.ToString("N0")).PadLeft(tW)}");
        buf.Add(BOLD_ON()); buf.Add(DBL_ON());
        Ln($"{"GRAND TOTAL :".PadLeft(lblW)} {sale.GrandTotal.ToString("N0").PadLeft(tW)}");
        buf.Add(BOLD_OFF()); buf.Add(DBL_OFF());
        Ln($"{"PAID :".PadLeft(lblW)} {sale.AmountPaid.ToString("N0").PadLeft(tW)}");
        if (sale.BalanceDue > 0) { buf.Add(BOLD_ON()); Ln($"{"BALANCE DUE :".PadLeft(lblW)} {sale.BalanceDue.ToString("N0").PadLeft(tW)}"); buf.Add(BOLD_OFF()); }
        Sep('=');
        Ln(); Ln(Center(footer)); Ln(); Ln(); Ln();
        buf.Add(FF());

        var result = new List<byte>();
        foreach (var b in buf) result.AddRange(b);
        return result.ToArray();
    }
}
