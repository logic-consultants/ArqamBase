using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>School leaving and bonafide certificates on plain A4, one page each.</summary>
public static class Certificates
{
    /// <summary>The fields of the Access "School Leaving Certificate", with the issue date added.</summary>
    public static IDocument Leaving(StudentPrint s, SchoolPrint school) => Page(s, school, "SCHOOL LEAVING CERTIFICATE", content =>
        content.Column(col =>
        {
            col.Spacing(12);
            Field(col, "Registration no.", s.DsNo?.ToString() ?? "");
            Field(col, "Computer code", s.Cc.ToString());
            Field(col, "Name", s.Name);
            Field(col, "Father's name", s.FatherName);
            Field(col, "Date of birth", PrintKit.Date(s.BirthDate));
            Field(col, "Date of birth (in words)", s.BirthDate is { } b ? DateWords.Of(b) : "");
            Field(col, "Date of admission", PrintKit.Date(s.AdmissionDate));
            Field(col, "Class in which studying", s.ClassAndSection);
            Field(col, "School leaving date", PrintKit.Date(s.LeavingDate));
            Field(col, "Reason for leaving", s.LeavingReason ?? "");
            Field(col, "Remarks", s.Remarks ?? "");
        }), "Incharge Record");

    /// <summary>A standard bonafide (proof of enrolment) certificate.</summary>
    public static IDocument Bonafide(StudentPrint s, SchoolPrint school) => Page(s, school, "BONAFIDE CERTIFICATE", content =>
        content.Column(col =>
        {
            col.Spacing(18);
            col.Item().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(13).LineHeight(1.7f));
                t.Span("This is to certify that ");
                t.Span(s.Name).Bold();
                t.Span($", {(s.IsGirl ? "daughter" : "son")} of ");
                t.Span(s.FatherName).Bold();
                t.Span(", is a bonafide student of ");
                t.SchoolName(s.SchoolName, n => n.Bold());
                t.Span($"{(school.Address.Length > 0 ? ", " + school.Address : "")}. {s.HeOrShe} is studying in class ");
                t.Span(s.ClassAndSection).Bold();
                t.Span(" under computer code ");
                t.Span(s.Cc.ToString()).Bold();
                t.Span(".");
            });
            if (s.BirthDate is { } birth)
                col.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(13).LineHeight(1.7f));
                    t.Span($"According to the school record, {s.HisOrHer} date of birth is ");
                    t.Span($"{PrintKit.Date(birth)} ({DateWords.Of(birth)})").Bold();
                    t.Span(s.AdmissionDate is { } adm ? $" and {s.HeOrShe.ToLowerInvariant()} was admitted on {PrintKit.Date(adm)}." : ".");
                });
            col.Item().Text($"This certificate is issued on the request of {s.HisOrHer} parent or guardian.")
                .FontSize(13).LineHeight(1.7f);
        }), null);

    /// <summary>
    /// The page: the letterhead header picture across the top (or the drawn letterhead), the certificate, the signatures,
    /// and the footer picture across the bottom. The pictures run edge to edge; the text keeps a 2 cm margin.
    /// </summary>
    private static IDocument Page(StudentPrint s, SchoolPrint school, string title, Action<IContainer> body, string? leftSignature) =>
        Document.Create(doc => doc.Page(page =>
        {
            const float margin = 2;
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Times New Roman")).FontSize(12));
            page.Header().Element(c =>
            {
                if (school.Header is { } header) c.Image(header).FitWidth();
                else c.PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(margin, Unit.Centimetre)
                    .Element(x => PrintKit.Letterhead(x, s.SchoolName, school));
            });
            page.Content().PaddingHorizontal(margin, Unit.Centimetre).PaddingTop(30).Column(col =>
            {
                col.Item().AlignCenter().Text(title).FontSize(20).Bold().Underline().LetterSpacing(0.05f);
                col.Item().PaddingTop(20).Row(r =>
                {
                    r.RelativeItem().Text($"CC: {s.Cc}").FontSize(11);
                    r.RelativeItem().AlignRight().Text($"Date of issue: {PrintKit.Date(DateTime.Today)}").FontSize(11);
                });
                col.Item().PaddingTop(30).Element(body);
            });
            page.Footer().Column(col =>
            {
                col.Item().PaddingHorizontal(margin, Unit.Centimetre).PaddingBottom(school.Footer is null ? margin + 0.7f : 0.7f, Unit.Centimetre).Row(r =>
                {
                    if (leftSignature is not null) r.ConstantItem(170).Element(c => PrintKit.Signature(c, leftSignature));
                    r.RelativeItem();
                    r.ConstantItem(170).Element(c => PrintKit.Signature(c, "Principal"));
                });
                if (school.Footer is { } footer) col.Item().Image(footer).FitWidth();
            });
        }));

    private static void Field(ColumnDescriptor col, string label, string value) =>
        col.Item().Row(r =>
        {
            r.ConstantItem(190).Text(label + ":").FontSize(13);
            r.RelativeItem().BorderBottom(0.5f).BorderColor(Colors.Grey.Medium).PaddingBottom(2).Text(value).FontSize(13).Bold();
        });
}
