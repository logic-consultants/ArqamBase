using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>One line of a printed voucher.</summary>
public sealed record VoucherPrintLine(int No, string AccountNo, string Account, string? Description, string? BillNo, string? ChequeNo,
    DateTime? ChequeDate, decimal Debit, decimal Credit);

/// <summary>A voucher on A4: its lines with debit and credit totals and signature lines.</summary>
public static class VoucherPrints
{
    public static IDocument Create(string type, int voucherNo, DateTime date, int? folio, string? narration, IReadOnlyList<VoucherPrintLine> lines,
        string yearName, string schoolName, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(9));
            page.Header().PaddingBottom(6).Element(c => PrintKit.Letterhead(c, schoolName, school));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text($"{type} voucher").FontSize(13).Bold().FontColor(PrintKit.Navy);
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Voucher no.: ").Bold(); t.Span($"{voucherNo}"); });
                    r.RelativeItem().Text(t => { t.Span("Date: ").Bold(); t.Span(date.ToString("ddd dd/MM/yyyy")); });
                    r.RelativeItem().Text(t => { t.Span("Folio: ").Bold(); t.Span(folio?.ToString() ?? ""); });
                    r.RelativeItem().AlignRight().Text(t => { t.Span("Year: ").Bold(); t.Span(yearName); });
                });
                if (!string.IsNullOrWhiteSpace(narration))
                    col.Item().PaddingTop(2).Text(t => { t.Span("Narration: ").Bold(); t.Span(narration.Trim()); });

                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(22);
                        c.ConstantColumn(38);
                        c.RelativeColumn(3);
                        c.RelativeColumn(4);
                        c.ConstantColumn(40);
                        c.ConstantColumn(48);
                        c.ConstantColumn(52);
                        c.ConstantColumn(62);
                        c.ConstantColumn(62);
                    });
                    table.Header(h =>
                    {
                        foreach (var (text, right) in new[] { ("#", false), ("Acc. no.", false), ("Account", false), ("Description", false), ("Bill no.", false),
                                     ("Chq no.", false), ("Chq date", false), ("Debit (D)", true), ("Credit (W)", true) })
                        {
                            var cell = h.Cell().Background(Colors.Grey.Lighten3).BorderBottom(0.75f).Padding(3);
                            (right ? cell.AlignRight() : cell).Text(text).Bold();
                        }
                    });
                    foreach (var l in lines)
                    {
                        Cell(table).Text($"{l.No}");
                        Cell(table).Text(l.AccountNo);
                        Cell(table).Text(l.Account);
                        Cell(table).Text(l.Description ?? "");
                        Cell(table).Text(l.BillNo ?? "");
                        Cell(table).Text(l.ChequeNo ?? "");
                        Cell(table).Text(PrintKit.Date(l.ChequeDate));
                        Cell(table).AlignRight().Text(Amount(l.Debit));
                        Cell(table).AlignRight().Text(Amount(l.Credit));
                    }
                    table.Cell().ColumnSpan(7).BorderTop(0.75f).Padding(3).Text($"Total · {lines.Count} lines").Bold();
                    table.Cell().BorderTop(0.75f).Padding(3).AlignRight().Text(Amount(lines.Sum(l => l.Debit))).Bold();
                    table.Cell().BorderTop(0.75f).Padding(3).AlignRight().Text(Amount(lines.Sum(l => l.Credit))).Bold();
                });
            });
            page.Footer().Column(f =>
            {
                f.Item().PaddingTop(30).Row(r =>
                {
                    r.RelativeItem().Element(c => PrintKit.Signature(c, "Prepared by"));
                    r.RelativeItem().AlignCenter().Element(c => PrintKit.Signature(c, "Checked by"));
                    r.RelativeItem().AlignRight().Element(c => PrintKit.Signature(c, "Approved by"));
                });
                f.Item().PaddingTop(6).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }));

    /// <summary>
    /// A daily cash voucher as the old Access cash book, on A4 landscape: receipts (credit lines, b/f first) on the left and payments
    /// (debit lines, c/f last) on the right, each with a running total, then the two totals under a double rule.
    /// </summary>
    public static IDocument CashBook(DateTime date, int? folio, IReadOnlyList<VoucherPrintLine> lines) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Times New Roman")).FontSize(8.5f));
            var receipts = lines.Where(l => l.Credit != 0).Select(l => (l, l.Credit)).ToList();
            var payments = lines.Where(l => l.Debit != 0).Select(l => (l, l.Debit)).ToList();
            page.Header().AlignCenter().Text("Cash Book").FontSize(20).Bold().FontColor(Colors.Grey.Darken1);
            page.Content().Column(col =>
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Element(c => CashSide(c, "Receipts", "VR#:", "Description", date, folio, receipts));
                    r.ConstantItem(6);
                    r.RelativeItem().Element(c => CashSide(c, "Payments", "VR:", "Descripton", date, folio, payments));
                });
                col.Item().PaddingTop(24).LineHorizontal(1.2f);
                col.Item().PaddingTop(2).LineHorizontal(1.2f);
                col.Item().PaddingTop(10).Row(r =>
                {
                    r.RelativeItem().AlignRight().PaddingRight(30).Element(c => CashTotal(c, "Total Receipts :", receipts.Sum(x => x.Item2)));
                    r.ConstantItem(6);
                    r.RelativeItem().AlignRight().Element(c => CashTotal(c, "Total Payments :", payments.Sum(x => x.Item2)));
                });
            });
            page.Footer().Column(f =>
            {
                f.Item().PaddingTop(30).Row(r =>
                {
                    r.RelativeItem().Element(c => PrintKit.Signature(c, "Prepared by"));
                    r.RelativeItem().AlignCenter().Element(c => PrintKit.Signature(c, "Approved by"));
                    r.RelativeItem().AlignRight().Element(c => PrintKit.Signature(c, "Checked by"));
                });
                f.Item().PaddingTop(6).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }));

    private static void CashSide(IContainer container, string title, string vr, string description, DateTime date, int? folio,
        IReadOnlyList<(VoucherPrintLine Line, decimal Amount)> rows) => container.Column(col =>
    {
        col.Item().Border(0.75f).PaddingHorizontal(3).PaddingVertical(1).Row(r =>
        {
            r.RelativeItem(2).AlignBottom().Text(t => { t.Span("Date:   ").Bold().FontSize(8); t.Span(date.ToString("dd/MM/yyyy")).FontSize(7.5f); });
            r.RelativeItem(3).AlignCenter().Text(title).FontSize(14).Bold();
            r.RelativeItem(2).AlignRight().AlignBottom().Text(t => { t.Span("Folio:      ").Bold().FontSize(8); t.Span(folio?.ToString() ?? "").FontSize(7.5f); });
        });
        col.Item().PaddingTop(4).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(24);
                c.RelativeColumn(3);
                c.ConstantColumn(25);
                c.RelativeColumn(4);
                c.ConstantColumn(44);
                c.ConstantColumn(46);
            });
            table.Header(h =>
            {
                foreach (var (text, align) in new[] { (vr, 0), ("Title", 1), ("Folio", 0), (description, 1), ("Amount", 1), ("Total", 1) })
                {
                    var cell = h.Cell().Border(0.75f).PaddingHorizontal(2).PaddingVertical(1);
                    (align == 1 ? cell.AlignCenter() : cell).Text(text).Bold().FontSize(8.5f);
                }
            });
            decimal total = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var (l, amount) = rows[i];
                total += amount;
                var shade = i % 2 == 1 ? Colors.Grey.Lighten3 : Colors.White;
                IContainer Cell() => table.Cell().Border(0.75f).Background(shade).PaddingHorizontal(2).PaddingVertical(1);
                Cell().Text(l.BillNo ?? "");
                Cell().Text(l.Account);
                Cell().Text(int.TryParse(l.AccountNo, out var n) ? n.ToString("0000") : l.AccountNo);
                Cell().Text(l.Description ?? "");
                Cell().AlignRight().Text(amount.ToString("#,##0.##"));
                Cell().AlignRight().Text(total.ToString("#,##0.##"));
            }
        });
    });

    private static void CashTotal(IContainer container, string label, decimal amount) => container.Text(t =>
    {
        t.DefaultTextStyle(s => s.Bold().FontColor(Colors.Grey.Darken1));
        t.Span(label + "     ");
        t.Span(amount.ToString("#,##0.##")).FontColor(Colors.Black);
    });

    private static IContainer Cell(TableDescriptor table) => table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(3);

    private static string Amount(decimal amount) => amount == 0 ? "" : amount.ToString("#,##0.##");
}
