using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>Student ID cards at bank-card size (85.6 × 54 mm), ten to an A4 page, with cutting borders.</summary>
public static class IdCards
{
    private const float CardWidth = 85.6f, CardHeight = 54f; // millimetres
    private const int PerRow = 2, PerPage = 10;

    public static IDocument Create(IReadOnlyList<StudentPrint> students, SchoolPrint school) =>
        Document.Create(doc =>
        {
            foreach (var chunk in students.Chunk(PerPage))
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(12, Unit.Millimetre);
                    page.MarginHorizontal(15, Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(7));
                    page.Content().AlignCenter().Column(col =>
                    {
                        foreach (var pair in chunk.Chunk(PerRow))
                            col.Item().Row(row =>
                            {
                                foreach (var s in pair)
                                    row.ConstantItem(CardWidth, Unit.Millimetre).Height(CardHeight, Unit.Millimetre).Element(c => Card(c, s, school));
                            });
                    });
                });
        });

    private static void Card(IContainer container, StudentPrint s, SchoolPrint school)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Medium).Column(col =>
        {
            // Band: logo, school name, "Student ID card".
            col.Item().Height(13, Unit.Millimetre).Background(PrintKit.Navy).PaddingHorizontal(5).PaddingVertical(4).Row(r =>
            {
                r.ConstantItem(26).Image(school.Logo).FitArea();
                r.RelativeItem().PaddingLeft(6).AlignMiddle().Column(c =>
                {
                    c.Item().Text(s.SchoolName.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Colors.White).ClampLines(2);
                    c.Item().Text("STUDENT ID CARD").FontSize(6).Bold().FontColor(PrintKit.Yellow).LetterSpacing(0.1f);
                });
            });

            col.Item().Height(33, Unit.Millimetre).PaddingHorizontal(6).PaddingVertical(5).Row(r =>
            {
                // Photo, passport proportions.
                r.ConstantItem(21, Unit.Millimetre).Height(26, Unit.Millimetre).Border(0.5f).BorderColor(Colors.Grey.Medium).Element(photo =>
                {
                    if (s.Photo is { } bytes) photo.Image(bytes).FitArea();
                    else photo.AlignCenter().AlignMiddle().Text("Photo").FontColor(Colors.Grey.Medium);
                });
                r.RelativeItem().PaddingLeft(8).Column(c =>
                {
                    c.Spacing(1.5f);
                    c.Item().Text(s.Name).FontSize(9.5f).Bold().FontColor(PrintKit.Navy).ClampLines(1);
                    c.Item().Text($"{s.SonOrDaughter} {s.FatherName}").FontSize(7.5f).ClampLines(1);
                    c.Item().PaddingTop(3).Element(x => Line(x, "CC", s.Cc.ToString()));
                    c.Item().Element(x => Line(x, "Class", s.ClassAndSection));
                    c.Item().Element(x => Line(x, "Family", s.FamilyId?.ToString() ?? ""));
                    if (s.BloodGroup.Length > 0) c.Item().Element(x => Line(x, "Blood group", s.BloodGroup));
                    if (!string.IsNullOrWhiteSpace(s.Phone)) c.Item().Element(x => Line(x, "Contact", Core.NumberFormat.Phone(s.Phone)));
                });
            });

            col.Item().Height(7, Unit.Millimetre).Background(PrintKit.Yellow).AlignCenter().AlignMiddle().Column(c =>
            {
                c.Item().AlignCenter().Text(school.AddressLine).FontSize(5.5f).FontColor(PrintKit.Navy).ClampLines(1);
                c.Item().AlignCenter().Text("If found, please return to the school").FontSize(5).FontColor(PrintKit.Navy);
            });
        });
    }

    private static void Line(IContainer container, string label, string value) =>
        container.Text(t =>
        {
            t.Span(label + ": ").FontColor(Colors.Grey.Darken1);
            t.Span(value).SemiBold();
        });
}
