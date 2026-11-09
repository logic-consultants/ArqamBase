using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>A column of a printed report. Sum columns get group and grand totals.</summary>
public sealed record ReportColumn(string Header, float Width, bool Number = false, bool Sum = false);

/// <summary>
/// One printed row: its cells as shown, the values of the sum columns (by column index), and its group at each level
/// (e.g. class, then section).
/// </summary>
public sealed record ReportLine(string[] Cells, IReadOnlyDictionary<int, decimal> Sums, string[] Groups);

/// <summary>
/// A report printed as a table with group headings, a total under each group and a grand total
/// (the layout of the Access unpaid and daily fee reports). Lines must be in group order.
/// </summary>
public sealed class GroupedReport
{
    public required string Title { get; init; }
    /// <summary>The filters, e.g. "Class: 5 · All unpaid challans".</summary>
    public string Subtitle { get; init; } = "";
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<ReportLine> Lines { get; init; }
    /// <summary>The heading of each group level, e.g. "Class", "Section".</summary>
    public IReadOnlyList<string> GroupNames { get; init; } = [];
    /// <summary>A Sr# column, counted from 1 in each innermost group.</summary>
    public bool SerialNumbers { get; init; } = true;
    public bool Landscape { get; init; }
    /// <summary>Signature lines under the grand total, e.g. "Prepared by", "Accountant", "Manager".</summary>
    public IReadOnlyList<string> Signatures { get; init; } = [];
    /// <summary>Lines of a summary box under the grand total, e.g. ("Total students", "835").</summary>
    public IReadOnlyList<(string Label, string Value)> Summary { get; init; } = [];
    public string SchoolName { get; init; } = PrintKit.DefaultSchoolName;
    public byte[] Logo { get; init; } = PrintKit.Logo;

    private int Levels => GroupNames.Count;
    private int FirstSum => Columns.Select((c, i) => (c, i)).FirstOrDefault(x => x.c.Sum, (null!, Columns.Count)).Item2;
    private uint SpanBeforeSums => (uint)(FirstSum + (SerialNumbers ? 1 : 0));
    private uint AllColumns => (uint)(Columns.Count + (SerialNumbers ? 1 : 0));

    public IDocument ToPdf() =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(8));
            page.Header().PaddingBottom(6).Column(col =>
            {
                col.Item().Row(r =>
                {
                    r.ConstantItem(30).Height(36).Image(Logo).FitArea();
                    r.RelativeItem().PaddingLeft(8).AlignMiddle().Column(c =>
                    {
                        c.Item().Text(SchoolName.ToUpperInvariant()).FontSize(12).Bold().FontColor(PrintKit.Navy);
                        c.Item().Text(Title).FontSize(11).SemiBold();
                    });
                    r.RelativeItem().AlignRight().AlignBottom().Column(c =>
                    {
                        if (Subtitle.Length > 0) c.Item().AlignRight().Text(Subtitle).FontColor(Colors.Grey.Darken2);
                    });
                });
                col.Item().PaddingTop(4).LineHorizontal(1).LineColor(PrintKit.Navy);
            });
            page.Content().Column(col =>
            {
                col.Item().Table(Table);
                if (Summary.Count > 0)
                    col.Item().PaddingTop(12).AlignRight().Width(230).Border(0.75f).BorderColor(PrintKit.Navy).Padding(6).Column(c =>
                    {
                        c.Item().PaddingBottom(3).Text("Summary").FontSize(10).Bold().FontColor(PrintKit.Navy);
                        foreach (var (label, value) in Summary)
                            c.Item().PaddingVertical(1).Row(r =>
                            {
                                r.RelativeItem().Text(label).FontSize(9);
                                r.AutoItem().Text(value).FontSize(9).Bold();
                            });
                    });
                if (Signatures.Count > 0)
                    col.Item().PaddingTop(45).Row(r =>
                    {
                        foreach (var s in Signatures) r.RelativeItem().AlignCenter().Element(c => PrintKit.Signature(c, s));
                    });
            });
            page.Footer().Row(r =>
            {
                r.RelativeItem().Text($"Printed {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(Colors.Grey.Darken1);
                r.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }));

    private void Table(TableDescriptor t)
    {
        t.ColumnsDefinition(c =>
        {
            if (SerialNumbers) c.ConstantColumn(26);
            foreach (var col in Columns) c.RelativeColumn(col.Width);
        });
        t.Header(h =>
        {
            if (SerialNumbers) h.Cell().Element(HeaderCell).Text("Sr#").Bold();
            foreach (var col in Columns)
            {
                var cell = h.Cell().Element(HeaderCell);
                (col.Number ? cell.AlignRight() : cell).Text(col.Header).Bold();
            }
        });

        var open = new string?[Levels];
        var sums = new decimal[Levels + 1, Columns.Count];
        var counts = new int[Levels + 1];
        var serial = 0;
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            var change = Levels;
            for (var l = 0; l < Levels; l++)
                if (i == 0 || line.Groups[l] != open[l]) { change = l; break; }
            if (i > 0)
                for (var l = Levels - 1; l >= change; l--) Subtotal(t, l, open[l]!, sums, counts);
            for (var l = change; l < Levels; l++)
            {
                open[l] = line.Groups[l];
                t.Cell().ColumnSpan(AllColumns).Background(l == 0 ? Colors.Grey.Lighten2 : Colors.Grey.Lighten4)
                    .PaddingVertical(3).PaddingLeft(3 + l * 10).Text($"{GroupNames[l]}: {line.Groups[l]}").Bold();
                for (var c = 0; c < Columns.Count; c++) sums[l, c] = 0;
                counts[l] = 0;
            }
            if (change < Levels) serial = 0;

            if (SerialNumbers) t.Cell().Element(BodyCell).Text((++serial).ToString());
            for (var c = 0; c < Columns.Count; c++)
            {
                var cell = t.Cell().Element(BodyCell);
                (Columns[c].Number ? cell.AlignRight() : cell).Text(line.Cells[c]);
            }
            foreach (var (c, v) in line.Sums)
                for (var l = 0; l <= Levels; l++) sums[l, c] += v;
            for (var l = 0; l <= Levels; l++) counts[l]++;
        }
        if (Lines.Count > 0)
            for (var l = Levels - 1; l >= 0; l--) Subtotal(t, l, open[l]!, sums, counts);
        TotalRow(t, $"Grand total ({counts[Levels]:N0})", Levels, sums, grand: true);
    }

    private void Subtotal(TableDescriptor t, int level, string value, decimal[,] sums, int[] counts) =>
        TotalRow(t, $"Total {GroupNames[level]} {value} ({counts[level]:N0})", level, sums, grand: false);

    private void TotalRow(TableDescriptor t, string label, int level, decimal[,] sums, bool grand)
    {
        IContainer Style(IContainer c) => (grand ? c.BorderTop(1).BorderBottom(1) : c.BorderTop(0.5f)).PaddingVertical(3).PaddingHorizontal(3);
        t.Cell().ColumnSpan(SpanBeforeSums).Element(Style).AlignRight().Text(label).Bold();
        for (var c = FirstSum; c < Columns.Count; c++)
        {
            var cell = t.Cell().Element(Style).AlignRight();
            if (Columns[c].Sum) cell.Text(sums[level, c].ToString("N0")).Bold();
        }
    }

    private static IContainer HeaderCell(IContainer c) => c.Background(Colors.Grey.Lighten3).BorderBottom(0.5f).Padding(3);
    private static IContainer BodyCell(IContainer c) => c.BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(3);
}
