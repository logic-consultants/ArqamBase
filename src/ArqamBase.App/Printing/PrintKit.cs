using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>What the student printouts (certificates, ID cards) need about one student.</summary>
public sealed record StudentPrint(
    int Cc, int? DsNo, string Name, bool IsGirl, string FatherName, DateTime? BirthDate, DateTime? AdmissionDate,
    string ClassName, string SectionName, string SchoolName, DateTime? LeavingDate, string? LeavingReason, string? Remarks,
    string BloodGroup, int? FamilyId, string? Phone, byte[]? Photo)
{
    public string SonOrDaughter => IsGirl ? "D/o" : "S/o";
    public string HeOrShe => IsGirl ? "She" : "He";
    public string HisOrHer => IsGirl ? "her" : "his";
    public string ClassAndSection => SectionName.Length > 0 ? $"{ClassName} ({SectionName})" : ClassName;
}

/// <summary>The school details printed on every printout, from CampusInfo (Setup › School information).</summary>
public sealed record SchoolPrint(string Address, string Phones, byte[] Logo, byte[]? Header, byte[]? Footer)
{
    /// <summary>The saved details; the built-in logo when none is saved.</summary>
    public static SchoolPrint From(ArqamBase.Data.Campus? campus) => new(
        campus?.Address?.Trim() ?? "", campus?.Phones ?? "", campus?.Logo is { Length: > 0 } logo ? logo : PrintKit.Logo,
        campus?.LetterheadHeader is { Length: > 0 } h ? h : null, campus?.LetterheadFooter is { Length: > 0 } f ? f : null);

    /// <summary>Address and phones on one line, e.g. "P-63 Canal Road, Toba Tek Singh  ·  Ph: 046-2516811".</summary>
    public string AddressLine => Phones.Length == 0 ? Address : Address.Length == 0 ? $"Ph: {Phones}" : $"{Address}  ·  Ph: {Phones}";
}

/// <summary>Shared parts of the printouts: licence, logo, school colours and the letterhead.</summary>
public static class PrintKit
{
    public const string Navy = "#2D2775", Yellow = "#F5E62B";
    public const string DefaultSchoolName = "DAR-E-ARQAM SCHOOL";

    private static byte[]? _logo;

    /// <summary>The built-in school logo (from the Access certificate), used until one is saved in School information.</summary>
    public static byte[] Logo
    {
        get
        {
            if (_logo is not null) return _logo;
            using var stream = typeof(PrintKit).Assembly.GetManifestResourceStream("ArqamBase.logo.png")!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return _logo = copy.ToArray();
        }
    }

    /// <summary>
    /// Call once at start. QuestPDF's Community licence is free for organisations with under USD 1M annual revenue.
    /// The printouts use Arial and Times New Roman, which every Windows PC has; the job offer letter also uses Calibri and Constantia.
    /// </summary>
    public static void Init()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var file in new[] { "arial.ttf", "arialbd.ttf", "ariali.ttf", "times.ttf", "timesbd.ttf", "timesi.ttf", "tahoma.ttf", "calibri.ttf", "calibrib.ttf", "constan.ttf" })
        {
            var path = Path.Combine(fonts, file);
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
        }
    }

    public static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy") ?? "";

    /// <summary>Logo, school name, address and phones, and a rule under them (used when no letterhead header picture is saved).</summary>
    public static void Letterhead(IContainer container, string schoolName, SchoolPrint school)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(70).Height(85).Image(school.Logo).FitArea();
                row.RelativeItem().PaddingLeft(14).AlignMiddle().Column(c =>
                {
                    c.Item().Text(schoolName.ToUpperInvariant()).FontSize(20).Bold().FontColor(Navy);
                    c.Item().Text(school.AddressLine).FontSize(11).FontColor(Colors.Grey.Darken2);
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(2).LineColor(Navy);
        });
    }

    /// <summary>A signature line with the title under it.</summary>
    public static void Signature(IContainer container, string title) =>
        container.Width(170).Column(c =>
        {
            c.Item().LineHorizontal(1);
            c.Item().PaddingTop(4).AlignCenter().Text(title).FontSize(11);
        });
}
