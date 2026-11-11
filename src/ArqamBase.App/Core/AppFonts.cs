using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ArqamBase.App.Core;

/// <summary>
/// The fonts that come with the app (the Fonts folder next to ArqamBase.exe): Revue-Thin for the school name,
/// Lato, Oswald and Playfair Display (SIL Open Font License), and Jameel Noori Nastaleeq for Urdu.
/// They are loaded for this program only, so nothing has to be installed on the PC.
/// </summary>
public static class AppFonts
{
    public const string SchoolNameDefault = "Revue-Thin", Urdu = "Jameel Noori Nastaleeq";

    /// <summary>Left-to-right fonts offered for the app's text and for printouts, when present on this PC or in the Fonts folder.</summary>
    private static readonly string[] TextFonts =
        ["Segoe UI", "Calibri", "Arial", "Tahoma", "Verdana", "Microsoft Sans Serif", "Cambria",
         "Times New Roman", "Constantia", "Lato", "Oswald", "Playfair Display"];

    private static readonly PrivateFontCollection Bundled = new();
    private static readonly List<byte[]> Files = [];
    private static bool _loaded;

    /// <summary>Loads the bundled fonts. Call once at start, before any font is created.</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var folder = Path.Combine(AppContext.BaseDirectory, "Fonts");
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.GetFiles(folder, "*.ttf"))
        {
            try
            {
                Bundled.AddFontFile(path);                // for GDI+ (Font objects)
                AddFontResourceEx(path, FrPrivate, 0);    // for GDI text drawing (TextRenderer, Krypton), this program only
                Files.Add(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException) { }
        }
    }

    /// <summary>The bundled font files, for registering with the PDF printouts.</summary>
    public static IReadOnlyList<byte[]> BundledFiles => Files;

    public static bool Has(string name) => Family(name) is not null;

    /// <summary>The font family by name: installed on Windows or bundled with the app; null when neither.</summary>
    public static FontFamily? Family(string name) =>
        Bundled.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? FontFamily.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fonts users can choose for the app's text and the printouts (those available here).</summary>
    public static IReadOnlyList<string> TextChoices => TextFonts.Where(Has).ToList();

    /// <summary>Fonts for the school name: Revue-Thin first, then the text fonts.</summary>
    public static IReadOnlyList<string> SchoolNameChoices => new[] { SchoolNameDefault }.Concat(TextFonts).Where(Has).Distinct().ToList();

    /// <summary>A font for Urdu text: Jameel Noori Nastaleeq at a size that reads like <paramref name="like"/>; that font when Jameel is missing.</summary>
    public static Font UrduFont(Font like) =>
        Family(Urdu) is { } urdu ? new Font(urdu, like.SizeInPoints * 1.4f, FontStyle.Regular) : like;

    public static bool HasUrdu(string? text) => text?.Any(c => c is >= '؀' and <= 'ۿ') == true;

    private const uint FrPrivate = 0x10;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
}
