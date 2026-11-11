using System.Text;
using ArqamBase.App.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One unpaid challan printed on a student's fee challan form.</summary>
public sealed record ChallanLine(string Period, int Tuition, int Exams, int Registration, int Admission, int Prospectus, int Fine, int Other, DateTime? DueDate)
{
    public int Total => Tuition + Exams + Registration + Admission + Prospectus + Fine + Other;
}

/// <summary>
/// A student's fee challan form: every unpaid challan added together, as on the Access challan (RPT_FeeReceipt_Students).
/// <see cref="Message"/> (English or Urdu) is printed on each copy.
/// </summary>
public sealed record ChallanPrint(int Cc, string Name, int? FamilyId, string FatherName, string ClassName, string SectionName, string SchoolName,
    string? AccountNumber, string? CampusDivision, IReadOnlyList<ChallanLine> Lines, string? Message = null)
{
    public int Total => Lines.Sum(l => l.Total);
    public DateTime? DueDate => Lines.Max(l => l.DueDate);
    public string Periods => string.Join(", ", Lines.Select(l => l.Period).Where(p => p.Length > 0));
}

/// <summary>Fee challan forms: one student per landscape A4 sheet, with a school, a bank and a student copy side by side.</summary>
public static class Challans
{
    private static readonly string[] Copies = ["School copy", "Bank copy", "Student copy"];

    public static IDocument Create(IReadOnlyList<ChallanPrint> challans, SchoolPrint school, string bank, int lateFee) =>
        Document.Create(doc =>
        {
            foreach (var c in challans)
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(6, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(8.5f));
                    page.Content().Row(row =>
                    {
                        for (var i = 0; i < Copies.Length; i++)
                        {
                            if (i > 0)
                                row.ConstantItem(6, Unit.Millimetre).AlignCenter()
                                    .LineVertical(0.5f).LineColor(Colors.Grey.Medium).LineDashPattern([4f, 4f]);
                            var copy = Copies[i];
                            row.RelativeItem().Element(x => Copy(x, c, copy, school, bank, lateFee));
                        }
                    });
                });
        });

    private static void Copy(IContainer container, ChallanPrint c, string copy, SchoolPrint school, string bank, int lateFee) =>
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(6).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(30).Height(36).Image(school.Logo).FitArea();
                row.RelativeItem().PaddingLeft(5).Column(h =>
                {
                    h.Item().Text(t => t.SchoolName(c.SchoolName.ToUpperInvariant(), n => n.FontSize(11).Bold().FontColor(PrintKit.Navy)));
                    h.Item().Text("Fee Challan Form").FontSize(9.5f).Bold();
                    h.Item().Text(school.AddressLine).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                });
            });
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Credited at: {bank}").FontSize(7.5f);
                row.AutoItem().Border(0.75f).PaddingHorizontal(4).PaddingVertical(1).Text(copy).FontSize(7.5f).Bold();
            });
            col.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("A/C #: ").FontColor(Colors.Grey.Darken1); t.Span(c.AccountNumber ?? "").Bold(); });
                row.AutoItem().Text(t => { t.Span("R#: ").FontColor(Colors.Grey.Darken1); t.Span("__________"); });
            });
            if (!string.IsNullOrWhiteSpace(c.CampusDivision)) col.Item().Text(c.CampusDivision).FontSize(7.5f);
            col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(PrintKit.Navy);

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.ConstantColumn(72); d.RelativeColumn(); });
                void Field(string caption, string value, bool bold = false)
                {
                    t.Cell().PaddingVertical(1).Text(caption).FontColor(Colors.Grey.Darken1);
                    var text = t.Cell().PaddingVertical(1).Text(value);
                    if (bold) text.Bold();
                }
                Field("Name:", c.Name, bold: true);
                Field("Computer code:", c.Cc.ToString(), bold: true);
                Field("Family ID:", c.FamilyId?.ToString() ?? "");
                Field("Family head:", c.FatherName);
                Field("Class:", c.SectionName.Length > 0 ? $"{c.ClassName} · {c.SectionName}" : c.ClassName);
            });

            col.Item().PaddingTop(5).Background(Colors.Grey.Lighten3).PaddingHorizontal(4).PaddingVertical(2).Text("Fee details").Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(); d.ConstantColumn(60); });
                void Amount(string caption, int value, bool bold = false, bool always = false)
                {
                    if (value == 0 && !always) return;
                    var left = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingHorizontal(4).PaddingVertical(2).Text(caption);
                    var right = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingHorizontal(4).PaddingVertical(2).AlignRight().Text($"{value:N0}");
                    if (bold) { left.Bold(); right.Bold(); }
                }
                t.Cell().ColumnSpan(2).PaddingHorizontal(4).PaddingVertical(2).Text(x => { x.Span("Periods: ").FontColor(Colors.Grey.Darken1); x.Span(c.Periods); });
                Amount("Tuition fee", c.Lines.Sum(l => l.Tuition), always: true);
                Amount("Exams / Annual Fund", c.Lines.Sum(l => l.Exams));
                Amount("Registration fee", c.Lines.Sum(l => l.Registration));
                Amount("Admission fee", c.Lines.Sum(l => l.Admission));
                Amount("Prospectus fee", c.Lines.Sum(l => l.Prospectus));
                Amount("Other / summer tasks", c.Lines.Sum(l => l.Other));
                Amount("Fine / late fee", c.Lines.Sum(l => l.Fine));
                Amount("Total receivable", c.Total, bold: true, always: true);
            });
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("Due date: ").FontColor(Colors.Grey.Darken1); t.Span(PrintKit.Date(c.DueDate)).Bold(); });
                row.RelativeItem().AlignRight().Text(t => { t.Span("After due date: ").FontColor(Colors.Grey.Darken1); t.Span($"Rs {c.Total + lateFee:N0}").Bold(); });
            });
            col.Item().PaddingTop(6).AlignCenter().Height(24).Width(150).Svg(Code39($"*{c.Cc}*"));
            col.Item().AlignCenter().Text(c.Cc.ToString()).FontSize(7);
            if (c.DueDate is { } due)
                col.Item().PaddingTop(4).AlignCenter().Text($"This voucher is valid only in the month of {due:MMMM yyyy}.").FontSize(7.5f).Italic();
            if (!string.IsNullOrWhiteSpace(c.Message))
                col.Item().PaddingTop(6).Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).Element(x =>
                        c.Message.TrimStart()[0] is >= '؀' and <= 'ۿ' ? x.AlignRight() : x) // a message in Urdu reads from the right
                    .Text(c.Message).FontFamily(AppFonts.HasUrdu(c.Message) ? [AppFonts.Urdu, "Arial"] : ["Arial", "Tahoma"]).FontSize(8.5f);
            col.Item().PaddingTop(18).Row(row =>
            {
                row.RelativeItem().Column(s => { s.Item().PaddingRight(8).LineHorizontal(0.5f); s.Item().Text("Deposited by").FontSize(7.5f); });
                row.RelativeItem().Column(s => { s.Item().PaddingLeft(8).LineHorizontal(0.5f); s.Item().PaddingLeft(8).Text("Received by").FontSize(7.5f); });
            });
            col.Item().PaddingTop(6).AlignCenter().Text("Dues once paid are not refundable in any case.").FontSize(7).FontColor(Colors.Grey.Darken2);
        });

    // Code 39 bar patterns (bar, space, bar, …; w = wide) for the characters a CC barcode needs.
    private static readonly Dictionary<char, string> Code39Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn", ['4'] = "nnnwwnnnw",
        ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw", ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['*'] = "nwnnwnwnn",
    };

    /// <summary>A Code 39 barcode as SVG (scanners read the CC from it, as from the Access challan's barcode font).</summary>
    private static string Code39(string text)
    {
        const int narrow = 2, wide = 5, height = 40;
        var bars = new StringBuilder();
        var x = 0;
        foreach (var ch in text)
        {
            if (!Code39Patterns.TryGetValue(ch, out var pattern)) continue;
            for (var i = 0; i < pattern.Length; i++)
            {
                var w = pattern[i] == 'w' ? wide : narrow;
                if (i % 2 == 0) bars.Append($"<rect x=\"{x}\" y=\"0\" width=\"{w}\" height=\"{height}\"/>");
                x += w;
            }
            x += narrow; // gap between characters
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {x} {height}\" preserveAspectRatio=\"none\"><g fill=\"#000\">{bars}</g></svg>";
    }
}
