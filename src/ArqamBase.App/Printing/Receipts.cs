using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One challan paid on a receipt.</summary>
public sealed record ReceiptLine(int Cc, string Name, string ClassName, string Period, int Fee, int Fine, int Amount);

/// <summary>A fee receipt: one receipt number can pay several challans, also of brothers and sisters.</summary>
public sealed record ReceiptPrint(int ReceiptNo, DateTime Date, string Mode, string ReceivedBy, string SchoolName, string? Remarks,
    IReadOnlyList<ReceiptLine> Lines)
{
    public int Total => Lines.Sum(l => l.Amount);
}

/// <summary>Fee receipts on A5, the school letterhead at the top.</summary>
public static class Receipts
{
    public static IDocument Create(ReceiptPrint r, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
            page.Header().Element(c =>
            {
                if (school.Header is { } header) c.Image(header).FitWidth();
                else c.Row(row =>
                {
                    row.ConstantItem(38).Height(46).Image(school.Logo).FitArea();
                    row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(col =>
                    {
                        col.Item().Text(r.SchoolName.ToUpperInvariant()).FontSize(12).Bold().FontColor(PrintKit.Navy);
                        col.Item().Text(school.AddressLine).FontSize(8).FontColor(Colors.Grey.Darken2);
                    });
                });
            });
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().LineHorizontal(1.5f).LineColor(PrintKit.Navy);
                col.Item().PaddingVertical(8).AlignCenter().Text("FEE RECEIPT").FontSize(14).Bold().LetterSpacing(0.05f);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(t => { t.Span("Receipt no.: ").FontColor(Colors.Grey.Darken1); t.Span(r.ReceiptNo.ToString()).Bold(); });
                    row.RelativeItem().AlignCenter().Text(t => { t.Span("Mode: ").FontColor(Colors.Grey.Darken1); t.Span(r.Mode); });
                    row.RelativeItem().AlignRight().Text(t => { t.Span("Date: ").FontColor(Colors.Grey.Darken1); t.Span(PrintKit.Date(r.Date)).Bold(); });
                });
                col.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(34);
                        c.RelativeColumn(2.6f);
                        c.RelativeColumn(2.2f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.1f);
                        c.RelativeColumn(0.8f);
                        c.RelativeColumn(1.4f);
                    });
                    table.Header(h =>
                    {
                        foreach (var (text, right) in new[] { ("CC", false), ("Student", false), ("Class", false), ("Period", false), ("Fee", true), ("Fine", true), ("Amount", true) })
                        {
                            var cell = h.Cell().Background(Colors.Grey.Lighten3).BorderBottom(0.5f).Padding(3);
                            (right ? cell.AlignRight() : cell).Text(text).Bold();
                        }
                    });
                    foreach (var l in r.Lines)
                    {
                        IContainer Cell() => table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten1).Padding(3);
                        Cell().Text(l.Cc.ToString());
                        Cell().Text(l.Name);
                        Cell().Text(l.ClassName);
                        Cell().Text(l.Period);
                        Cell().AlignRight().Text(l.Fee.ToString("N0"));
                        Cell().AlignRight().Text(l.Fine == 0 ? "" : l.Fine.ToString("N0"));
                        Cell().AlignRight().Text(l.Amount.ToString("N0"));
                    }
                    table.Cell().ColumnSpan(5).PaddingTop(5).AlignRight().AlignMiddle().Text("Total received").Bold();
                    table.Cell().ColumnSpan(2).PaddingTop(5).AlignRight().Text($"Rs {r.Total:N0}").Bold().FontSize(11);
                });
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span("In words: ").FontColor(Colors.Grey.Darken1);
                    t.Span($"Rupees {DateWords.Number(r.Total)} only").Italic();
                });
                if (!string.IsNullOrWhiteSpace(r.Remarks))
                    col.Item().PaddingTop(4).Text(t => { t.Span("Remarks: ").FontColor(Colors.Grey.Darken1); t.Span(r.Remarks); });
            });
            page.Footer().Column(col =>
            {
                col.Item().PaddingBottom(4).Row(row =>
                {
                    row.RelativeItem().AlignBottom().Text($"Received by: {r.ReceivedBy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(170).Element(c => PrintKit.Signature(c, "Signature"));
                });
                if (school.Footer is { } footer) col.Item().Image(footer).FitWidth();
            });
        }));
}
