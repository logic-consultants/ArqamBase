using System.Text.RegularExpressions;
using ArqamBase.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>What the Job Offer Letter says about one person. Anything empty prints as a line to write on.</summary>
public sealed record JobOfferDetails(
    DateTime? Date, string Name, string? FatherName, string? Designation, DateTime? JoiningDate, string? Caste, DateTime? BirthDate,
    string? Religion, string? LastDegree, string? Cnic, string? Address, string? Phone, string? Cell, string? Emergency,
    decimal? Basic, decimal? StPerk, decimal? Fuel, decimal? Scf, decimal? Security)
{
    public static readonly JobOfferDetails Blank = new(null, "", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    /// <summary>What is paid each month: the earnings less the SCF and security deductions.</summary>
    public decimal? Total => Basic is null && StPerk is null && Fuel is null ? null : (Basic ?? 0) + (StPerk ?? 0) + (Fuel ?? 0) - (Scf ?? 0) - (Security ?? 0);

    /// <summary>The designation on the Job card, or else the post.</summary>
    public static JobOfferDetails From(StaffMember s, string? post, DateTime date) => new(
        date, Tidy($"{s.Salute} {s.Name}"), s.FatherName, string.IsNullOrWhiteSpace(s.Designation) ? post : s.Designation, s.DateHired, s.Caste, s.BirthDate,
        s.Religion, s.Education, s.Cnic, s.Address, s.HomePhone, s.CellNo, s.EmergencyNo,
        s.CurrentSalary ?? s.Salary, s.StPerk, s.FuelAllowance, s.Scf, s.SecDeduction);

    private static string Tidy(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The Job Offer Letter: the school's layout on A4, with wording from a <see cref="JobOfferTemplate"/>.</summary>
public static partial class JobOffers
{
    private const float BodyLineHeight = 1.5f;
    private const string RuleBlue = "#4472C4";

    /// <summary>The fields that can be typed in the wording.</summary>
    public const string FieldList = "{Name}  {Designation}  {Date}  {JoiningDate}  {Salary}  {SCF}  {Security}";

    /// <summary>The school's own wording, used until an Admin changes it.</summary>
    public static JobOfferTemplate Sample() => new()
    {
        Heading = "DAR-E-ARQAM SCHOOLS",
        CampusLine = "Toba Tek Singh Campus",
        Quote = "“Keep your pledges; you should be accountable for your pledges” (Al.Quran)",
        Title = "Job Offer Letter",
        Intro = "We are pleased to offer you a job as {Designation} on the following terms and conditions:",
        Terms = SampleTerms.ReplaceLineEndings("\r\n"), // text boxes break lines only on CRLF
        Signatory = "Director",
        Acceptance = "I hereby accept the above-mentioned terms and conditions, I will follow the rules and regulations of the organization and will perform my duties to my maximum ability, skill and knowledge.",
    };

    private const string SampleTerms = """
        1. You are bound to follow all time-to-time instructions and fulfill the assignments given by the management and remain with us for minimum of two years, and your one-month salary will be kept in the form of security. If you resign during two years of agreement, you cannot get your security & experience certificate.
        2. You are appointed on temporary basis unless your services are confirmed by the management. Your confirmation is subject to your performance during six months of probationary period. Moreover, during this period, you won’t be entitled to any leave.
        3. After six months of probationary period, maximum one leave per month is allowed. If you do not avail this chance, one day salary will be rewarded you as incentive. In case of second leave without approval, absent will be marked that means two days deduction. In case of four absents/leaves, salary of one Sunday will not be rewarded.
        4. Short leave is not allowed and Principal does not have authority in this regard. Only director can allow you in case of emergency & two short leaves are considered as one full day leave.
        5. You are bound to come fifteen minutes earlier before the school timing and stay fifteen minutes later after the school timing. Being late for four days one day salary will be deducted.
        6. You will not be entitled to the salary of winter & summer vacation in the first year of your job but in the later year.
        7. Rs.100 is monthly Staff Contribution Fund deduction that will be returned in double figures on leaving according to rules.
        8. You must dress up according to Islamic values & it is compulsory for female staff members to wear gown on coming and going out from the school.
        9. Your mobile phone must be switched-off during school premises & you are not allowed to give tuition to the students of your own campus.
        10. Your entitlement to annual increment will be at least after one year of your job and that will be due from the beginning of the new session (April).
        11. Management can transfer you in other campuses within city & send you to other cities for attending seminars or workshops and inspection.
        12. In case of your resignation, you are under obligation to give one-month prior notice but if you resign
        i- Before two years, without one-month prior notice, you will lose last month salary + Security + double figure of SC fund + Experience certificate.
        ii- Before two years, with one-month prior notice, last salary will be awarded & nothing else.
        iii- After two years, without one-month prior notice, only security & single figure of S.C fund will be rewarded & nothing else.
        iv- After two years, with one-month prior notice, last salary +security+ double S.C fund + experience certificate will be awarded.
        13. Management can terminate your services without explaining any reason with one-month prior notice and then you are not eligible for double S.C fund as well as for experience certificate also.
        14. During Covid Lock downs, only those teachers will get their salary who are teaching online classes and have access to proper internet and required equipment. If someone opt work from will get 50% of basic salary. No allowances will be paid.
        """;

    public static IDocument Create(JobOfferTemplate text, JobOfferDetails d, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Times New Roman")).FontSize(12));
            page.Content().PaddingLeft(72).PaddingRight(27).PaddingTop(32).Column(col => Body(col, text, d, school));
            page.Footer().PaddingLeft(52).PaddingRight(25).PaddingBottom(26).Column(col =>
            {
                col.Item().LineHorizontal(2).LineColor(RuleBlue);
                col.Item().PaddingTop(6).PaddingHorizontal(30).Row(r =>
                {
                    r.RelativeItem().Text(text.Title.ToUpperInvariant()).FontFamily("Calibri").FontSize(10);
                    r.RelativeItem().AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Calibri")).FontSize(10));
                        t.Span("Page ");
                        t.CurrentPageNumber().Bold();
                        t.Span(" of ");
                        t.TotalPages().Bold();
                    });
                });
            });
        }));

    private static void Body(ColumnDescriptor col, JobOfferTemplate text, JobOfferDetails d, SchoolPrint school)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(46).PaddingLeft(-11).Height(56).AlignTop().Image(school.Logo).FitArea();
            r.RelativeItem().PaddingTop(10).AlignCenter().Column(c =>
            {
                c.Item().AlignCenter().Text(text.Heading).FontFamily("Calibri").FontSize(36).LineHeight(0.9f);
                c.Item().AlignCenter().Text(text.CampusLine).FontFamily("Constantia").FontSize(12);
                c.Item().AlignCenter().Text(text.Quote).FontSize(10).Bold();
            });
        });
        col.Item().PaddingTop(14).Row(r =>
        {
            r.RelativeItem().Text(t => { t.Span("Date:").Bold(); Field(t, d.Date is null ? null : PrintKit.Date(d.Date), 10); });
            r.RelativeItem(2).PaddingLeft(62).Text(text.Title).FontSize(14).Bold().Underline();
            r.RelativeItem();
        });
        col.Item().PaddingTop(26).Row(r =>
        {
            r.ConstantItem(180).Text("Dear:").Bold();
            r.RelativeItem().Text(t => Field(t, d.Name, 34));
        });
        col.Item().PaddingTop(6).Text(t =>
        {
            t.DefaultTextStyle(x => x.LineHeight(BodyLineHeight));
            Wording(t, text.Intro, d, underlineValues: true);
        });
        foreach (var line in text.Terms.Replace("\r", "").Split('\n'))
            col.Item().Text(t =>
            {
                t.DefaultTextStyle(x => x.LineHeight(BodyLineHeight));
                t.Justify();
                Wording(t, line, d, underlineValues: false);
            });

        col.Item().PaddingTop(44).AlignRight().PaddingRight(16).Text(text.Signatory).Bold();
        col.Item().PaddingTop(10).ShowEntire().Column(c => Details(c, d));
        col.Item().PaddingTop(22).ShowEntire().Column(c => SalaryTable(c, d));
        col.Item().PaddingTop(28).Text(t =>
        {
            t.DefaultTextStyle(x => x.LineHeight(BodyLineHeight));
            Wording(t, text.Acceptance, d, underlineValues: false);
        });
        col.Item().PaddingTop(40).Text("Signature (of the selected Candidate)").Bold();
        col.Item().PaddingTop(60).ShowEntire().Column(c =>
        {
            c.Item().Text("(Office Use only)").Bold();
            c.Item().Text("Resigned/Left/Terminated__________________ School Leaving Date___________________").Bold();
        });
    }

    private static void Details(ColumnDescriptor c, JobOfferDetails d)
    {
        void Line(params (string Label, string? Value, int Blank, float Width)[] cells) =>
            c.Item().PaddingBottom(5).Row(r =>
            {
                foreach (var cell in cells)
                    r.RelativeItem(cell.Width).Text(t => { t.Span(cell.Label); Field(t, cell.Value, cell.Blank); });
            });

        Line(("I can join you from: ", d.JoiningDate is null ? null : PrintKit.Date(d.JoiningDate), 12, 1));
        Line(("Name: ", d.Name, 18, 1), ("Father’s Name: ", d.FatherName, 18, 1));
        Line(("Cast: ", d.Caste, 8, 3), ("Date of Birth: ", d.BirthDate is null ? null : PrintKit.Date(d.BirthDate), 12, 4), ("Religion: ", d.Religion, 12, 3));
        Line(("Last Degree: ", d.LastDegree, 14, 1), ("CNIC NO: ", d.Cnic, 18, 1));
        c.Item().PaddingBottom(5).Text(t => { t.Span("Address: "); Field(t, d.Address, 60); });
        Line(("Ph: ", d.Phone, 14, 3), ("Cell#: ", d.Cell, 16, 4), ("Emergency#: ", d.Emergency, 12, 4));
    }

    private static void SalaryTable(ColumnDescriptor c, JobOfferDetails d)
    {
        c.Item().Background(Colors.Black).AlignCenter().Text("Salary Structure").FontColor(Colors.White);
        c.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                foreach (var width in new[] { 1.1f, 1.15f, 1.35f, 1, 1, 1.1f }) cols.RelativeColumn(width);
            });
            static IContainer Cell(IContainer x) => x.Border(0.75f).AlignCenter().AlignMiddle();
            foreach (var title in new[] { "Basic", "ST Perk", "Fuel Allowance", "SCF", "Security", "Total" })
                table.Cell().Element(Cell).Text(title).Bold();
            foreach (var value in new[] { d.Basic, d.StPerk, d.Fuel, d.Scf, d.Security, d.Total })
                table.Cell().MinHeight(16).Element(Cell).Text(Money(value));
        });
    }

    private static string Money(decimal? value) => value is null or 0 ? "" : $"{value:#,0.##}";

    /// <summary>A value underlined, or a line to write on when there is none.</summary>
    private static void Field(TextDescriptor t, string? value, int blankLength)
    {
        if (string.IsNullOrWhiteSpace(value)) t.Span(new string('_', blankLength));
        else t.Span(value.Trim()).Underline();
    }

    /// <summary>Wording with its {fields} filled in; a field with no value becomes a line, and an unknown one prints as typed.</summary>
    private static void Wording(TextDescriptor t, string text, JobOfferDetails d, bool underlineValues)
    {
        var at = 0;
        foreach (Match m in Placeholder().Matches(text))
        {
            t.Span(text[at..m.Index]);
            at = m.Index + m.Length;
            var known = FieldValue(m.Groups[1].Value, d, out var value);
            if (!known) t.Span(m.Value);
            else if (underlineValues || string.IsNullOrEmpty(value)) Field(t, value, 16);
            else t.Span(value);
        }
        t.Span(text[at..]);
    }

    private static bool FieldValue(string name, JobOfferDetails d, out string? value)
    {
        (var known, value) = name.ToLowerInvariant() switch
        {
            "name" => (true, d.Name),
            "designation" => (true, d.Designation),
            "date" => (true, d.Date is null ? null : PrintKit.Date(d.Date)),
            "joiningdate" => (true, d.JoiningDate is null ? null : PrintKit.Date(d.JoiningDate)),
            "salary" => (true, Money(d.Basic)),
            "scf" => (true, Money(d.Scf)),
            "security" => (true, Money(d.Security)),
            _ => (false, (string?)null),
        };
        return known;
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
