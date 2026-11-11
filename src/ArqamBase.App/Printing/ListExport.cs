using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>A grid's rows as shown (current filters, sort and column order), for Excel and PDF.</summary>
public sealed class ListExport
{
    public required string Title { get; init; }
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<string[]> Rows { get; init; }
    /// <summary>Relative column widths, from the grid.</summary>
    public required IReadOnlyList<float> Widths { get; init; }

    /// <summary>The visible columns and rows of a grid, with each cell as the user sees it.</summary>
    public static ListExport From(DataGridView grid, string title)
    {
        var columns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var rows = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Visible)
            .Select(r => columns.Select(c => r.Cells[c.Index].FormattedValue?.ToString() ?? "").ToArray()).ToList();
        return new ListExport
        {
            Title = title,
            Headers = columns.Select(c => c.HeaderText).ToList(),
            Rows = rows,
            Widths = columns.Select(c => (float)c.Width).ToList(),
        };
    }

    public void SaveExcel(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(SheetName(Title));
        for (var c = 0; c < Headers.Count; c++) sheet.Cell(1, c + 1).Value = Headers[c];
        for (var r = 0; r < Rows.Count; r++)
            for (var c = 0; c < Headers.Count; c++)
            {
                var cell = sheet.Cell(r + 2, c + 1);
                cell.Value = CellValue(Rows[r][c]);
                if (cell.DataType == XLDataType.DateTime) cell.Style.DateFormat.Format = "dd/mm/yyyy";
            }
        var header = sheet.Range(1, 1, 1, Math.Max(1, Headers.Count));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        sheet.SheetView.FreezeRows(1);
        if (Rows.Count > 0) sheet.Range(1, 1, Rows.Count + 1, Headers.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, Math.Min(Rows.Count + 1, 500));
        book.SaveAs(path);
    }

    /// <summary>Numbers and dates go into Excel as such (so they sort and add up); phone numbers and codes with a leading 0 stay text.</summary>
    private static XLCellValue CellValue(string text)
    {
        if (text.Length == 0) return Blank.Value;
        if (!text.StartsWith('0') && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var n))
            return n;
        if (DateTime.TryParseExact(text, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var d)) return d;
        return text;
    }

    private static string SheetName(string title) =>
        new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()) is { Length: > 0 } s ? s[..Math.Min(31, s.Length)] : "List";

    /// <summary>The list on A4 landscape, with the title, date and row count on each page.</summary>
    public IDocument ToPdf() =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(8));
            page.Header().PaddingBottom(6).Row(r =>
            {
                r.RelativeItem().Text(Title).FontSize(13).Bold().FontColor(PrintKit.Navy);
                r.RelativeItem().AlignRight().AlignBottom().Text($"{Rows.Count} records · {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(Colors.Grey.Darken1);
            });
            page.Content().Table(t =>
            {
                t.ColumnsDefinition(c => { foreach (var w in Widths) c.RelativeColumn(w); });
                t.Header(h =>
                {
                    foreach (var head in Headers)
                        h.Cell().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).Padding(3).Text(head).Bold();
                });
                foreach (var row in Rows)
                    foreach (var cell in row)
                        t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(cell);
            });
            page.Footer().AlignRight().Text(t =>
            {
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        }));
}
