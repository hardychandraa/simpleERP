using System.Globalization;
using System.Text;
using SimpleERP.Application.DTOs;
using SimpleERP.Domain.Entities;

namespace SimpleERP.Web.Services;

/// <summary>
/// The sales invoice (faktur penjualan) as raw ESC/P for an Epson LX-series dot-matrix
/// printer on 3-ply continuous forms. The printer strikes once and the carbon carries it
/// to every ply, so this produces one copy, never three.
///
/// Split in two so the layout can be checked without a printer: <see cref="RenderPages"/>
/// is pure text, one list of lines per sheet; <see cref="BuildInvoice"/> wraps those
/// pages in the control codes.
///
/// Sheet geometry comes from Settings: <c>PaperColumns</c> (96 = 12 cpi across the 9.5-inch
/// form) and <c>PaperLines</c> (33 = 5.5 inches at 6 lines per inch). The page length is
/// sent to the printer, so each form feed lands on the next perforation rather than
/// advancing the printer's default 11 inches.
///
/// The document text is Indonesian whatever the operator's UI language — it is the
/// customer's document — and numbers always use Indonesian formatting (1.520.000).
/// </summary>
public static class EscpBuilder
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    private const byte ESC = 0x1B;

    /// <summary>One printed line. Bold lines are emphasised on paper; text is unaffected.</summary>
    public readonly record struct PrintLine(string Text, bool Bold = false);

    public static byte[] BuildInvoice(SaleDto sale, AppSettings cfg, string printedBy, DateTime printedAtLocal)
    {
        var cols  = cfg.PaperColumns;
        var pages = RenderPages(sale, cfg, printedBy, printedAtLocal);

        var buf = new List<byte>();
        void Esc(params byte[] b) { buf.Add(ESC); buf.AddRange(b); }

        Esc((byte)'@');                          // reset — also clears any earlier page length
        Esc((byte)'2');                          // 1/6-inch line spacing: 6 lines per inch
        Esc((byte)'C', (byte)cfg.PaperLines);    // page length in lines, from this position
        buf.Add(0x12);                           // cancel condensed
        if (cols == 96) Esc((byte)'M');          // 12 cpi: 96 columns across 8 printable inches
        else            Esc((byte)'P');          // 10 cpi: 80 (narrow) or 132 (wide carriage)

        foreach (var page in pages)
        {
            foreach (var line in page)
            {
                if (line.Bold) Esc((byte)'E');
                buf.AddRange(Encoding.ASCII.GetBytes(line.Text.TrimEnd()));
                if (line.Bold) Esc((byte)'F');
                buf.Add(0x0D); buf.Add(0x0A);
            }
            buf.Add(0x0C);                       // form feed to the top of the next form
        }
        return buf.ToArray();
    }

    /// <summary>
    /// Lays the invoice out as sheets of at most <c>PaperLines</c> lines. Every sheet
    /// repeats the header, so each one stands alone if the pages are separated; only the
    /// last carries the totals and the receiver's signature, and the others say the
    /// invoice continues.
    /// </summary>
    public static List<List<PrintLine>> RenderPages(SaleDto sale, AppSettings cfg,
        string printedBy, DateTime printedAtLocal)
    {
        int W = cfg.PaperColumns;
        int L = cfg.PaperLines;

        // ── Item rows ────────────────────────────────────────────────────────
        // No | Nama Barang | Qty | Sat | Harga | Disc% | Disc.Rp | Jumlah
        const int noW = 3, qtyW = 6, unitW = 5, priceW = 12, pctW = 6, discW = 11, totW = 13;
        int nameW = W - (noW + qtyW + unitW + priceW + pctW + discW + totW) - 7;

        string Row(string no, string name, string qty, string unit, string price,
                   string pct, string disc, string total) =>
            $"{R(no, noW)} {Lft(name, nameW)} {R(qty, qtyW)} {Lft(unit, unitW)} {R(price, priceW)} " +
            $"{R(pct, pctW)} {R(disc, discW)} {R(total, totW)}";

        var blocks = new List<List<PrintLine>>();
        int n = 0;
        foreach (var item in sale.Items)
        {
            n++;
            var lineDiscount = item.DiscountAmount * item.Qty;
            var block = new List<PrintLine> {
                new(Row(n.ToString(), item.ProductName, Qty(item.Qty), item.Unit, Money(item.UnitPrice),
                        item.DiscountPercent is > 0 ? Pct(item.DiscountPercent.Value) : "-",
                        lineDiscount > 0 ? Money(lineDiscount) : "-",
                        Money(item.LineTotal)))
            };
            // Serial numbers and warranty are what a customer comes back with.
            var indent = new string(' ', noW + 1);
            if (!string.IsNullOrWhiteSpace(item.Notes))
                block.Add(new(Lft($"{indent}Ket: {item.Notes.Trim()}", W)));
            if (item.WarrantyExpiry.HasValue)
                block.Add(new(Lft($"{indent}Garansi {item.WarrantyMonths} bln s/d " +
                                  $"{item.WarrantyExpiry.Value.ToLocalTime():dd-MM-yyyy}", W)));
            blocks.Add(block);
        }

        // ── Header (repeated on every sheet) ─────────────────────────────────
        const int rightW = 24, midW = 16;
        int leftW = W - rightW - midW - 4;

        string term = sale.PaymentType == "Cash" ? "Tunai"
                    : sale.PaymentTermDays.HasValue ? $"{sale.PaymentTermDays} hari" : "Kredit";
        string title = sale.Status == "Cancelled" ? "FAKTUR PENJUALAN (BATAL)" : "FAKTUR PENJUALAN";

        List<PrintLine> Header() => new() {
            new(Lft(cfg.StoreName, W - rightW - 1) + " " + R(title, rightW), Bold: true),
            new(Lft(cfg.StoreAddress ?? "", W - rightW - 1) + " " + Lft($"No  : {sale.InvoiceNumber}", rightW)),
            new(Lft(string.IsNullOrWhiteSpace(cfg.StorePhone) ? "" : $"Telp {cfg.StorePhone}", W - rightW - 1)
                + " " + Lft($"Tgl : {sale.SaleDate.ToLocalTime():dd-MM-yyyy}", rightW)),
            new(Lft($"Kepada : {sale.CustomerName}", leftW) + "  "
                + Lft($"Sales : {Dash(sale.SalesPersonCode)}", midW) + "  "
                + Lft($"Gudang      : {Dash(sale.BranchCode)}", rightW)),
            new(Lft($"         {sale.CustomerAddress ?? ""}", leftW) + "  "
                + Lft($"TOP   : {term}", midW) + "  "
                + Lft($"Jatuh Tempo : {(sale.DueDate.HasValue ? sale.DueDate.Value.ToString("dd-MM-yyyy") : "-")}", rightW)),
            new(new string('-', W)),
            new(Row("No", "Nama Barang", "Qty", "Sat", "Harga", "Disc%", "Disc.Rp", "Jumlah"), Bold: true),
            new(new string('-', W)),
        };

        // ── Totals + receiver's signature (last sheet only) ──────────────────
        const int lblW = 14, amtW = 15;
        var totals = new List<(string Label, string Amount, bool Bold)> {
            ("Subtotal", Money(sale.Items.Sum(i => i.LineTotal)), false)
        };
        if (sale.InvoiceDiscountAmount > 0)
            totals.Add((sale.InvoiceDiscountPercent is > 0 ? $"Disc {Pct(sale.InvoiceDiscountPercent.Value)}%" : "Disc",
                        Money(sale.InvoiceDiscountAmount), false));
        totals.Add(("DPP", Money(sale.TaxBase), false));
        totals.Add(($"PPN {Pct(sale.TaxRate * 100m)}%", Money(sale.TaxAmount), false));
        totals.Add(("TOTAL", Money(sale.GrandTotal), true));

        // The signature sits beside the totals: "Penerima," on the second row, the line
        // to sign on on the last, leaving the rows between as signing space.
        var sigW = W - lblW - amtW - 1;
        List<PrintLine> LastFooter()
        {
            var f = new List<PrintLine> { new(new string('-', W)) };
            for (int i = 0; i < totals.Count; i++)
            {
                var left = i == 1 ? "  Penerima," : i == totals.Count - 1 ? "  (__________________)" : "";
                f.Add(new(Lft(left, sigW) + Lft(totals[i].Label, lblW) + " " + R(totals[i].Amount, amtW),
                          totals[i].Bold));
            }
            return f;
        }
        List<PrintLine> ContFooter() => new() {
            new(new string('-', W)),
            new(R("Bersambung ke halaman berikutnya ...", W)),
        };
        PrintLine Bottom(int page, int pages) =>
            new(Lft($"Dicetak: {printedBy} {printedAtLocal:dd-MM-yy HH:mm}", W - 12) + R($"Hal {page}/{pages}", 12));

        // ── Pagination ───────────────────────────────────────────────────────
        // One blank line at the top and one spare at the bottom keep print off the
        // perforation. An item and its sub-lines are never split across sheets.
        int usable   = L - 2;
        int headerH  = Header().Count;
        int contCap  = usable - headerH - ContFooter().Count - 1;
        int lastCap  = usable - headerH - LastFooter().Count - 1;
        if (lastCap < 1) lastCap = 1;
        if (contCap < 1) contCap = 1;

        var sheets = new List<List<List<PrintLine>>> { new() };
        int used = 0;
        foreach (var b in blocks)
        {
            if (used > 0 && used + b.Count > contCap) { sheets.Add(new()); used = 0; }
            sheets[^1].Add(b); used += b.Count;
        }
        // The last sheet also has to fit the totals: move its trailing items onto a new
        // sheet until it does.
        while (sheets[^1].Sum(b => b.Count) > lastCap && sheets[^1].Count > 1)
        {
            var spill = new List<List<PrintLine>>();
            while (sheets[^1].Sum(b => b.Count) > lastCap && sheets[^1].Count > 1)
            {
                spill.Insert(0, sheets[^1][^1]);
                sheets[^1].RemoveAt(sheets[^1].Count - 1);
            }
            sheets.Add(spill);
        }

        var pages = new List<List<PrintLine>>();
        for (int p = 0; p < sheets.Count; p++)
        {
            bool last = p == sheets.Count - 1;
            var page = new List<PrintLine> { new("") };
            page.AddRange(Header());
            foreach (var b in sheets[p]) page.AddRange(b);
            var footer = last ? LastFooter() : ContFooter();
            // Push the footer to the bottom so totals sit in the same place on every invoice.
            while (page.Count + footer.Count + 1 < usable + 1) page.Add(new(""));
            page.AddRange(footer);
            page.Add(Bottom(p + 1, sheets.Count));
            pages.Add(page);
        }
        return pages;
    }

    private static string Money(decimal v) => v.ToString("N0", Id);
    private static string Pct(decimal v)   => v.ToString("0.##", Id);
    private static string Qty(decimal v)   => v.ToString("0.##", Id);
    private static string Dash(string? s)  => string.IsNullOrWhiteSpace(s) ? "-" : s;

    /// <summary>Left-aligned, cut or padded to exactly <paramref name="w"/> characters.</summary>
    private static string Lft(string s, int w) => s.Length > w ? s[..w] : s.PadRight(w);
    /// <summary>Right-aligned, cut or padded to exactly <paramref name="w"/> characters.</summary>
    private static string R(string s, int w)   => s.Length > w ? s[..w] : s.PadLeft(w);
}
