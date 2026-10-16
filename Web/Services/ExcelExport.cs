using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;

namespace SimpleERP.Web.Services;

public enum XlKind { Text, Number, Percent, Date }

/// <summary>A column: its header, how its values are formatted, and an optional fixed width.</summary>
public record XlColumn(string Header, XlKind Kind = XlKind.Text, double Width = 0);

/// <summary>One worksheet: columns, data rows (one value per column) and an optional totals row.</summary>
public record XlSheet(string Name, IReadOnlyList<XlColumn> Columns, IEnumerable<object?[]> Rows, object?[]? Totals = null);

/// <summary>
/// Every report's Excel download (HC, 2026-10-15), in one layout: store name, report title,
/// period and print time on top, then a bold header row that stays frozen, filters on it,
/// whole-rupiah number format and a bold totals row. Values are written as values, never as
/// formulas, so a customer name that starts with "=" stays text and the figures are exactly the
/// ones on screen. Real .xlsx (ClosedXML, MIT): a CSV would be split and misread by Excel set to
/// Indonesian, which uses ";" and decimal commas.
/// </summary>
public static class ExcelExport
{
    private const int HeaderRow = 5;

    public static FileContentResult File(string fileName, string store, string title, string subtitle, params XlSheet[] sheets)
    {
        using var wb = new XLWorkbook();
        foreach (var sheet in sheets) Write(wb, sheet, store, title, subtitle);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new FileContentResult(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = Safe(fileName) + ".xlsx"
        };
    }

    private static void Write(XLWorkbook wb, XlSheet sheet, string store, string title, string subtitle)
    {
        var name = new string(sheet.Name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        var cols = sheet.Columns.Count;

        ws.Cell(1, 1).Value = store;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = title;
        ws.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(3, 1).Value = subtitle;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;

        for (var c = 0; c < cols; c++)
        {
            var cell = ws.Cell(HeaderRow, c + 1);
            cell.Value = sheet.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.WrapText = true;
            if (sheet.Columns[c].Kind is XlKind.Number or XlKind.Percent)
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        var row = HeaderRow;
        foreach (var values in sheet.Rows) { row++; Fill(ws, row, sheet.Columns, values); }
        var lastData = row;

        if (sheet.Totals is { } totals)
        {
            row++;
            Fill(ws, row, sheet.Columns, totals);
            var range = ws.Range(row, 1, row, cols);
            range.Style.Font.Bold = true;
            range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        }

        if (lastData > HeaderRow) ws.Range(HeaderRow, 1, lastData, cols).SetAutoFilter();
        ws.SheetView.FreezeRows(HeaderRow);
        for (var c = 0; c < cols; c++)
        {
            var col = ws.Column(c + 1);
            if (sheet.Columns[c].Width > 0) col.Width = sheet.Columns[c].Width;
            else { col.AdjustToContents(HeaderRow + 1, Math.Max(row, HeaderRow + 1)); col.Width = Math.Clamp(col.Width + 2, 8, 45); }
        }
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(HeaderRow, HeaderRow);
    }

    private static void Fill(IXLWorksheet ws, int row, IReadOnlyList<XlColumn> cols, object?[] values)
    {
        for (var c = 0; c < cols.Count && c < values.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            switch (values[c])
            {
                case null: break;
                case decimal d: cell.Value = d; break;
                case int i: cell.Value = i; break;
                case DateTime t: cell.Value = t; break;
                default: cell.Value = values[c]!.ToString(); break;   // text stays text, never a formula
            }
            cell.Style.NumberFormat.Format = cols[c].Kind switch {
                XlKind.Number  => "#,##0",
                XlKind.Percent => "0.0\"%\"",
                XlKind.Date    => "dd/mm/yyyy",
                _              => "@"
            };
        }
    }

    private static string Safe(string s) =>
        new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
}
