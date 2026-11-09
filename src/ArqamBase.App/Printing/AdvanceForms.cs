using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>What the Advance Request Form needs. Empty names print as lines to write on.</summary>
public sealed record AdvanceFormPrint(
    DateTime? Date, string? Salute, string Name, string FatherName, string SchoolName, decimal Amount, int Installments,
    string? Witness1, string? Witness2, string? Witness3, string? PaidThrough, string? RecommendedBy, string? ApprovedBy);

/// <summary>The Advance Request Form, one A4 page, with the school's wording.</summary>
public static class AdvanceForms
{
    private const string Blank = "______________________";

    public static IDocument Create(AdvanceFormPrint f, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            const float margin = 2;
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(12));
            page.Header().Element(c =>
            {
                if (school.Header is { } header) c.Image(header).FitWidth();
                else c.PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(margin, Unit.Centimetre)
                    .Element(x => PrintKit.Letterhead(x, f.SchoolName, school));
            });
            page.Content().PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(20).Column(col => Body(col, f));
            page.Footer().Element(c =>
            {
                if (school.Footer is { } footer) c.Image(footer).FitWidth();
                else c.Height(margin, Unit.Centimetre);
            });
        }));

    private static void Body(ColumnDescriptor col, AdvanceFormPrint f)
    {
        var title = string.IsNullOrWhiteSpace(f.Salute) ? "Mr./ Miss/Mrs." : f.Salute.Trim();
        col.Item().AlignCenter().Text("ADVANCE REQUEST FORM").FontSize(16).Bold().Underline();
        col.Item().PaddingTop(16).AlignRight().Text($"Date: {(f.Date is null ? Blank : PrintKit.Date(f.Date))}");
        col.Item().PaddingTop(10).Row(r =>
        {
            r.RelativeItem().Text(t => { t.Span("Employee’s Name: "); t.Span(f.Name).Bold(); });
            r.RelativeItem().Text(t => { t.Span("Father / Spouse’s Name: "); t.Span(f.FatherName).Bold(); });
        });
        col.Item().PaddingTop(14).Text(t =>
        {
            t.DefaultTextStyle(x => x.LineHeight(1.5f));
            t.Span($"I {title} ");
            t.Span(f.Name).Bold();
            t.Span(" make commitment to pay ");
            t.Span(f.SchoolName).Bold();
            t.Span(", The sum ");
            t.Span(f.Amount > 0 ? $"Rs {f.Amount:N0}" : Blank).Bold();
            t.Span(". Repayment is to be made in the form of ");
            t.Span(f.Installments > 0 ? $"{f.Installments}" : "______").Bold();
            t.Span(" equal payment. This payment will be deducted from the salary in each month.");
        });
        col.Item().PaddingTop(30).AlignRight().Column(c =>
        {
            c.Item().Text($"Signature of Employee: {Blank}");
            c.Item().PaddingTop(4).AlignRight().Text(f.Name);
        });
        col.Item().PaddingTop(20).Text(t =>
        {
            t.DefaultTextStyle(x => x.LineHeight(1.5f));
            t.Span("We hereby endorse that if ");
            t.Span(f.Name).Bold();
            t.Span(" S/W/D/O: ");
            t.Span(f.FatherName).Bold();
            t.Span(" may not able to return the advance on said date or condition organization has a right to Deduct this amount from SCF/security fund.");
        });
        foreach (var (label, name) in new[] { ("Witness1", f.Witness1), ("Witness2", f.Witness2), ("Witness3", f.Witness3) })
            col.Item().PaddingTop(16).Row(r =>
            {
                r.RelativeItem(3).Text($"{label}: Name {Filled(name)}");
                r.RelativeItem(2).Text($"Signature: {Blank}");
            });
        col.Item().PaddingTop(26).LineHorizontal(1);
        col.Item().PaddingTop(8).AlignCenter().Text("For Office use only").Bold();
        col.Item().PaddingTop(14).Row(r =>
        {
            r.RelativeItem().Text($"Advance paid through: {Filled(f.PaidThrough)}");
            r.RelativeItem().Text($"Recommended by: {Filled(f.RecommendedBy)}");
        });
        col.Item().PaddingTop(16).Row(r =>
        {
            r.RelativeItem().Text($"Approved by: {Filled(f.ApprovedBy)}");
            r.RelativeItem().Text("Official Seal:");
        });
    }

    private static string Filled(string? text) => string.IsNullOrWhiteSpace(text) ? Blank : text.Trim();
}
