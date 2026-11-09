using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One child on a defaulter letter: what is unpaid.</summary>
public sealed record DefaulterChild(int Cc, string Name, string ClassName, string Periods, int Months, int Total);

/// <summary>One letter: a family and its children's dues.</summary>
public sealed record DefaulterLetter(int? FamilyId, string FatherName, string? Address, IReadOnlyList<DefaulterChild> Children);

/// <summary>
/// The letters to families with unpaid fees, one A4 page each (the Access UnpaidLetter): the letterhead, the family,
/// the children's unpaid periods and amounts, and the Principal's signature. A struck-off notice gives a last date.
/// </summary>
public static class DefaulterLetters
{
    public static IDocument Create(IReadOnlyList<DefaulterLetter> letters, string schoolName, SchoolPrint school, DateTime? lastDate) =>
        Document.Create(doc =>
        {
            foreach (var letter in letters)
                doc.Page(page =>
                {
                    const float margin = 2;
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(12));
                    page.Header().Element(c =>
                    {
                        if (school.Header is { } header) c.Image(header).FitWidth();
                        else c.PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(margin, Unit.Centimetre)
                            .Element(x => PrintKit.Letterhead(x, schoolName, school));
                    });
                    page.Content().PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(24).Element(c => Body(c, letter, lastDate));
                    page.Footer().Column(col =>
                    {
                        col.Item().PaddingHorizontal(margin, Unit.Centimetre).PaddingBottom(school.Footer is null ? margin + 0.7f : 0.7f, Unit.Centimetre)
                            .Row(r =>
                            {
                                r.ConstantItem(170).Element(c => PrintKit.Signature(c, "Accounts office"));
                                r.RelativeItem();
                                r.ConstantItem(170).Element(c => PrintKit.Signature(c, "Principal"));
                            });
                        if (school.Footer is { } footer) col.Item().Image(footer).FitWidth();
                    });
                });
        });

    private static void Body(IContainer container, DefaulterLetter letter, DateTime? lastDate) =>
        container.Column(col =>
        {
            col.Item().AlignCenter().Text(lastDate is null ? "FEE REMINDER" : "STRUCK-OFF NOTICE").FontSize(16).Bold().Underline();
            col.Item().PaddingTop(16).Row(r =>
            {
                r.RelativeItem().Text(letter.FamilyId is int id ? $"Family ID: {id}" : "");
                r.RelativeItem().AlignRight().Text($"Date: {PrintKit.Date(DateTime.Today)}");
            });
            col.Item().PaddingTop(16).Text(t =>
            {
                t.Span("Respected Mr. ");
                t.Span(letter.FatherName).Bold();
                t.Span(",");
            });
            if (!string.IsNullOrWhiteSpace(letter.Address)) col.Item().Text(letter.Address.Trim()).FontSize(11).FontColor(Colors.Grey.Darken2);
            col.Item().PaddingTop(12).Text("Assalam o Alaikum!");
            col.Item().PaddingTop(8).Text(lastDate is null
                ? "This letter is to inform you that you have not submitted the dues of your following children."
                : "Despite our reminders, the dues of your following children are still unpaid.").LineHeight(1.4f);

            var total = letter.Children.Sum(c => c.Total);
            col.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.ConstantColumn(50);
                    c.RelativeColumn(3);
                    c.RelativeColumn(1.4f);
                    c.RelativeColumn(3);
                    c.ConstantColumn(46);
                    c.ConstantColumn(70);
                });
                IContainer Head(IContainer c) => c.Background(Colors.Grey.Lighten3).Border(0.5f).Padding(4);
                IContainer Cell(IContainer c) => c.Border(0.5f).Padding(4);
                table.Header(h =>
                {
                    foreach (var (text, right) in new[] { ("Sr#", false), ("CC", false), ("Name", false), ("Class", false), ("Unpaid periods", false), ("Months", true), ("Amount", true) })
                    {
                        var cell = h.Cell().Element(Head);
                        (right ? cell.AlignRight() : cell).Text(text).FontSize(10).Bold();
                    }
                });
                var i = 0;
                foreach (var child in letter.Children)
                {
                    table.Cell().Element(Cell).Text($"{++i}").FontSize(10);
                    table.Cell().Element(Cell).Text($"{child.Cc}").FontSize(10);
                    table.Cell().Element(Cell).Text(child.Name).FontSize(10);
                    table.Cell().Element(Cell).Text(child.ClassName).FontSize(10);
                    table.Cell().Element(Cell).Text(child.Periods).FontSize(10);
                    table.Cell().Element(Cell).AlignRight().Text($"{child.Months}").FontSize(10);
                    table.Cell().Element(Cell).AlignRight().Text(child.Total.ToString("N0")).FontSize(10);
                }
                table.Cell().ColumnSpan(5).Element(Cell).AlignRight().Text("Total").FontSize(10).Bold();
                table.Cell().Element(Cell).AlignRight().Text($"{letter.Children.Sum(c => c.Months)}").FontSize(10).Bold();
                table.Cell().Element(Cell).AlignRight().Text($"Rs {total:N0}").FontSize(10).Bold();
            });

            col.Item().PaddingTop(14).Text(lastDate is { } last
                ? $"You are requested to clear the dues of Rs {total:N0} by {PrintKit.Date(last)}. Otherwise the names of the children will be struck off the school rolls."
                : $"You are requested to submit the dues of Rs {total:N0} at your earliest ease. If you have already paid, please ignore this letter.")
                .LineHeight(1.4f);
            col.Item().PaddingTop(10).Text("JazakAllah.");
        });
}
