using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One person on the bank transfer letter.</summary>
public sealed record BankTransferLine(string Name, string Designation, string Account, decimal Paid);

/// <summary>One line of the month's salary summary.</summary>
public sealed record SummaryLine(string Text, decimal Amount);

/// <summary>The bank transfer letter and the Cr./Dr. summary of a month's salaries, as in the Excel workbook.</summary>
public static class SalaryPrints
{
    public const string BankName = "Bank AL Habib Limited", BankBranch = "Toba Tek Singh Branch";

    public static IDocument BankLetter(string month, string? chequeNo, IReadOnlyList<BankTransferLine> lines, string schoolName, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(10));
            page.Header().PaddingBottom(10).Element(c => PrintKit.Letterhead(c, schoolName, school));
            page.Content().Column(col =>
            {
                col.Item().AlignRight().Text($"Date: {PrintKit.Date(DateTime.Today)}");
                col.Item().Text("The Manager");
                col.Item().Text(BankName);
                col.Item().Text(BankBranch);
                col.Item().PaddingTop(10).Text($"Sub: Transfer Staff Salary for {month}").Bold();
                col.Item().PaddingTop(8).PaddingBottom(8).Text(
                    $"Kindly transfer Chq# {(string.IsNullOrWhiteSpace(chequeNo) ? "____________" : chequeNo.Trim())} as per following details.");
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(1.5f);
                    });
                    IContainer Cell(IContainer c) => c.Border(0.5f).Padding(3);
                    foreach (var head in new[] { "Sr#", "Name", "Desig.", "A/C #", "Paid" })
                        t.Cell().Element(Cell).Background(Colors.Grey.Lighten3).Text(head).Bold();
                    var sr = 0;
                    foreach (var l in lines)
                    {
                        t.Cell().Element(Cell).Text($"{++sr}");
                        t.Cell().Element(Cell).Text(l.Name);
                        t.Cell().Element(Cell).Text(l.Designation);
                        t.Cell().Element(Cell).Text(l.Account);
                        t.Cell().Element(Cell).AlignRight().Text(l.Paid.ToString("N2"));
                    }
                    t.Cell().ColumnSpan(4).Element(Cell).AlignRight().Text("Total").Bold();
                    t.Cell().Element(Cell).AlignRight().Text(lines.Sum(l => l.Paid).ToString("N2")).Bold();
                });
                col.Item().PaddingTop(50).AlignRight().Element(c => PrintKit.Signature(c, "Authorised signature"));
            });
        }));

    public static IDocument Summary(string month, IReadOnlyList<SummaryLine> credit, IReadOnlyList<SummaryLine> debit, string schoolName) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(10));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(t => t.SchoolName(schoolName.ToUpperInvariant(), n => n.FontSize(14).Bold().FontColor(PrintKit.Navy)));
                col.Item().AlignCenter().Text($"Summary ({month})").FontSize(12).Bold();
                col.Item().AlignCenter().Text("Salaries and Wages");
                col.Item().PaddingTop(12).Row(r =>
                {
                    r.RelativeItem().Element(c => Side(c, "Cr.", credit));
                    r.ConstantItem(20);
                    r.RelativeItem().Element(c => Side(c, "Dr.", debit));
                });
                col.Item().PaddingTop(50).Row(r =>
                {
                    foreach (var s in new[] { "Prepared by", "Checked by", "Approved by" })
                        r.RelativeItem().AlignCenter().Element(c => PrintKit.Signature(c, s));
                });
            });
        }));

    private static void Side(IContainer container, string title, IReadOnlyList<SummaryLine> lines) =>
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(1.5f);
            });
            IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
            t.Cell().ColumnSpan(2).Element(Cell).Background(Colors.Grey.Lighten3).Text(title).Bold();
            foreach (var l in lines)
            {
                t.Cell().Element(Cell).Text(l.Text);
                t.Cell().Element(Cell).AlignRight().Text(l.Amount.ToString("N0"));
            }
            t.Cell().Element(Cell).Text("Total").Bold();
            t.Cell().Element(Cell).AlignRight().Text(lines.Sum(l => l.Amount).ToString("N0")).Bold();
        });
}
