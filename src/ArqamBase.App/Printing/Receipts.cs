using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One challan paid on a receipt.</summary>
public sealed record ReceiptLine(int Cc, string Name, string ClassName, string Period, int Fee, int Fine, int Amount);

/// <summary>A challan of the receipt's students that is still unpaid.</summary>
public sealed record DueLine(int Cc, string Name, string Period, int Amount);

/// <summary>
/// A fee receipt: one receipt number can pay several challans, also of brothers and sisters.
/// <see cref="StillDue"/> lists what the same students still owe on <see cref="DueOn"/>;
/// <see cref="BalanceBefore"/> is given when the receipt is printed as it is saved.
/// </summary>
public sealed record ReceiptPrint(int ReceiptNo, DateTime Date, string Mode, string ReceivedBy, string SchoolName, string? Remarks,
    IReadOnlyList<ReceiptLine> Lines, IReadOnlyList<DueLine> StillDue, DateTime DueOn, int? BalanceBefore,
    byte[]? Slip = null, string? SlipNote = null)
{
    // Slip: the bank / online deposit slip picture (JPEG), printed with the receipt. SlipNote: printed instead when the slip is a PDF.
    public int Total => Lines.Sum(l => l.Amount);
    public int TotalDue => StillDue.Sum(d => d.Amount);
    public bool SeveralStudents => Lines.Select(l => l.Cc).Concat(StillDue.Select(d => d.Cc)).Distinct().Count() > 1;
}

public enum ReceiptPaper { A4, Roll80 }

/// <summary>Fee receipts: on A4 for laser printers (the top half of the sheet), or on an 80 mm thermal roll.</summary>
public static class Receipts
{
    public static IDocument Create(ReceiptPrint r, SchoolPrint school, ReceiptPaper paper) =>
        paper == ReceiptPaper.Roll80 ? Roll(r, school) : A4(r, school);

    // ----- A4: the receipt fills the top half of the sheet, with a cut line under it -----

    private static IDocument A4(ReceiptPrint r, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(9));
            page.Content().Column(col =>
            {
                col.Item().MinHeight(148.5f, Unit.Millimetre).Column(half =>
                {
                    half.Item().Element(c =>
                    {
                        if (school.Header is { } header) c.Image(header).FitWidth();
                        else c.PaddingHorizontal(14, Unit.Millimetre).PaddingTop(10, Unit.Millimetre).Element(x => DrawnHeader(x, r, school));
                    });
                    half.Item().PaddingHorizontal(14, Unit.Millimetre).PaddingVertical(6, Unit.Millimetre).Element(c => A4Body(c, r));
                });
                col.Item().PaddingHorizontal(8, Unit.Millimetre).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium).LineDashPattern([4f, 4f]);

                // The bottom half: the bank / online deposit slip.
                if (r.Slip is { } slip)
                    col.Item().PaddingHorizontal(14, Unit.Millimetre).PaddingTop(6, Unit.Millimetre).Column(s =>
                    {
                        s.Item().Text($"Bank / online deposit slip · receipt {r.ReceiptNo} · {PrintKit.Date(r.Date)}").FontSize(9).Bold();
                        s.Item().PaddingTop(4).Height(122, Unit.Millimetre).AlignCenter().AlignMiddle().Image(slip).FitArea();
                    });
                else if (r.SlipNote is { } note)
                    col.Item().PaddingHorizontal(14, Unit.Millimetre).PaddingTop(6, Unit.Millimetre).Text(note).FontSize(9).Italic();
            });
        }));

    private static void DrawnHeader(IContainer container, ReceiptPrint r, SchoolPrint school) =>
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(38).Height(46).Image(school.Logo).FitArea();
                row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(c =>
                {
                    c.Item().Text(t => t.SchoolName(r.SchoolName.ToUpperInvariant(), n => n.FontSize(13).Bold().FontColor(PrintKit.Navy)));
                    c.Item().Text(school.AddressLine).FontSize(8).FontColor(Colors.Grey.Darken2);
                });
            });
            col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(PrintKit.Navy);
        });

    private static void A4Body(IContainer container, ReceiptPrint r) =>
        container.Column(col =>
        {
            col.Item().PaddingBottom(6).AlignCenter().Text("FEE RECEIPT").FontSize(14).Bold().LetterSpacing(0.05f);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("Receipt no.: ").FontColor(Colors.Grey.Darken1); t.Span(r.ReceiptNo.ToString()).Bold(); });
                row.RelativeItem().AlignCenter().Text(t => { t.Span("Mode: ").FontColor(Colors.Grey.Darken1); t.Span(r.Mode); });
                row.RelativeItem().AlignRight().Text(t => { t.Span("Date: ").FontColor(Colors.Grey.Darken1); t.Span(PrintKit.Date(r.Date)).Bold(); });
            });
            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(40);
                    c.RelativeColumn(2.6f);
                    c.RelativeColumn(2.2f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(1.3f);
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
            col.Item().PaddingTop(4).Text(t =>
            {
                t.Span("In words: ").FontColor(Colors.Grey.Darken1);
                t.Span($"Rupees {DateWords.Number(r.Total)} only").Italic();
            });
            if (!string.IsNullOrWhiteSpace(r.Remarks))
                col.Item().PaddingTop(2).Text(t => { t.Span("Remarks: ").FontColor(Colors.Grey.Darken1); t.Span(r.Remarks); });

            // What is still owed.
            col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(6).Column(due =>
            {
                if (r.BalanceBefore is int before)
                    due.Item().Text(t =>
                    {
                        t.Span("Balance before: ").FontColor(Colors.Grey.Darken1); t.Span($"Rs {before:N0}");
                        t.Span("     Paid: ").FontColor(Colors.Grey.Darken1); t.Span($"Rs {r.Total:N0}");
                        t.Span("     Still due: ").FontColor(Colors.Grey.Darken1); t.Span($"Rs {r.TotalDue:N0}").Bold();
                    });
                due.Item().PaddingTop(r.BalanceBefore is null ? 0 : 3).Text(DueText(r)).FontSize(8.5f);
            });

            col.Item().PaddingTop(16).Row(row =>
            {
                row.RelativeItem().AlignBottom().Text($"Received by: {r.ReceivedBy}").FontSize(8).FontColor(Colors.Grey.Darken1);
                row.ConstantItem(170).Element(c => PrintKit.Signature(c, "Signature"));
            });
        });

    /// <summary>"Still due on 01/11/2026: NOV/26 Rs 3,150 · … Total Rs 6,300", or "Nothing else is due".</summary>
    private static string DueText(ReceiptPrint r)
    {
        if (r.StillDue.Count == 0) return $"Nothing else is due on {PrintKit.Date(r.DueOn)}.";
        var items = r.StillDue.Select(d => $"{(r.SeveralStudents ? d.Name + " " : "")}{d.Period} Rs {d.Amount:N0}");
        return $"Still due on {PrintKit.Date(r.DueOn)}: {string.Join("  ·  ", items)}.  Total Rs {r.TotalDue:N0}";
    }

    // ----- 80 mm thermal roll: one narrow column, as long as it needs to be -----

    private static IDocument Roll(ReceiptPrint r, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(80, Unit.Millimetre);
            page.MarginHorizontal(4, Unit.Millimetre);
            page.MarginVertical(5, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(8).FontColor(Colors.Black));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Width(16, Unit.Millimetre).Height(19, Unit.Millimetre).Image(school.Logo).FitArea();
                col.Item().PaddingTop(2).AlignCenter().Text(t => t.SchoolName(r.SchoolName.ToUpperInvariant(), n => n.FontSize(9).Bold()));
                if (school.AddressLine.Length > 0) col.Item().AlignCenter().Text(school.AddressLine).FontSize(7);
                col.Item().PaddingVertical(4).LineHorizontal(0.75f);
                col.Item().AlignCenter().Text("FEE RECEIPT").FontSize(11).Bold();
                col.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Text($"Receipt no. {r.ReceiptNo}").Bold();
                    row.RelativeItem().AlignRight().Text(PrintKit.Date(r.Date)).Bold();
                });
                col.Item().Text($"Mode: {r.Mode}");
                col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineDashPattern([2f, 2f]);

                foreach (var student in r.Lines.GroupBy(l => (l.Cc, l.Name, l.ClassName)))
                {
                    col.Item().PaddingTop(2).Text($"{student.Key.Cc}  {student.Key.Name}").Bold();
                    if (student.Key.ClassName.Length > 0) col.Item().Text(student.Key.ClassName).FontSize(7);
                    foreach (var l in student)
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(l.Fine == 0 ? l.Period : $"{l.Period} (fine {l.Fine:N0})");
                            row.AutoItem().Text(l.Amount.ToString("N0"));
                        });
                }
                col.Item().PaddingVertical(3).LineHorizontal(0.5f);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text("Total received").Bold().FontSize(9);
                    row.AutoItem().Text($"Rs {r.Total:N0}").Bold().FontSize(10);
                });
                col.Item().PaddingTop(2).Text($"Rupees {DateWords.Number(r.Total)} only").Italic().FontSize(7);
                if (!string.IsNullOrWhiteSpace(r.Remarks)) col.Item().PaddingTop(2).Text($"Remarks: {r.Remarks}").FontSize(7);

                col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineDashPattern([2f, 2f]);
                if (r.BalanceBefore is int before)
                {
                    Amount(col, "Balance before", before);
                    Amount(col, "Paid", r.Total);
                }
                if (r.StillDue.Count == 0)
                    col.Item().Text($"Nothing else is due on {PrintKit.Date(r.DueOn)}.");
                else
                {
                    col.Item().Text($"Still due on {PrintKit.Date(r.DueOn)}:").Bold();
                    foreach (var d in r.StillDue)
                        Amount(col, r.SeveralStudents ? $"{d.Period}  {d.Name}" : d.Period, d.Amount, small: true);
                    Amount(col, "Total due", r.TotalDue, bold: true);
                }

                if (r.Slip is { } slip)
                {
                    col.Item().PaddingVertical(3).LineHorizontal(0.5f).LineDashPattern([2f, 2f]);
                    col.Item().Text("Bank / online deposit slip:").Bold();
                    col.Item().PaddingTop(2).MaxHeight(110, Unit.Millimetre).AlignCenter().Image(slip).FitArea();
                }
                else if (r.SlipNote is { } note)
                    col.Item().PaddingTop(4).Text(note).FontSize(7).Italic();

                col.Item().PaddingTop(10).Text($"Received by: {r.ReceivedBy}").FontSize(7);
                col.Item().PaddingTop(14).LineHorizontal(0.5f);
                col.Item().AlignCenter().Text("Signature").FontSize(7);
                col.Item().PaddingTop(6).AlignCenter().Text("Thank you").FontSize(8);
            });
        }));

    private static void Amount(ColumnDescriptor col, string label, int amount, bool bold = false, bool small = false) =>
        col.Item().Row(row =>
        {
            var l = row.RelativeItem().Text(label).FontSize(small ? 7.5f : 8);
            var a = row.AutoItem().Text($"Rs {amount:N0}").FontSize(small ? 7.5f : 8);
            if (bold) { l.Bold(); a.Bold(); }
        });
}
