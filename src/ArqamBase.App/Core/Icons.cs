using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ArqamBase.App.Core;

/// <summary>Ribbon and menu icons drawn from the Windows icon font, tinted with the theme accent.</summary>
public static class Icons
{
    public const char Exit = '', Flag = '', FontBigger = '', FontSmaller = '';
    public const char Add ='', Save = '', Delete = '', Refresh = '', Undo = '',
        Filter = '', Sort = '', Clear = '', Search = '', Print = '', Document = '',
        Home = '', Person = '', People = '', School = '', Up = '', Money = '',
        Ledger = '', Calendar = '', Check = '', Chat = '', Mail = '', Lock = '',
        ThemeIcon = '', Settings = '', Grid = '', Menu = '', IdCard = '', Chart = '',
        Send = '', Receipt = '', Book = '', Excel = '', Calculator = '', SortUp = '',
        SortDown = '';

    private static readonly Dictionary<(char, int, int), Bitmap> Cache = [];
    private static readonly string FontName = PickFont();

    public static Bitmap Get(char glyph, int size = 32, Color? color = null)
    {
        var c = color ?? Theme.Accent;
        var key = (glyph, size, c.ToArgb());
        if (Cache.TryGetValue(key, out var bmp))
            return bmp;

        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font(FontName, size * 0.8f, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(c))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var text = glyph.ToString();
            var sz = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            g.DrawString(text, font, brush, (size - sz.Width) / 2f, (size - sz.Height) / 2f, StringFormat.GenericTypographic);
        }
        Cache[key] = bmp;
        return bmp;
    }

    public static Bitmap Small(char glyph) => Get(glyph, 16);
    public static Bitmap Large(char glyph) => Get(glyph, 32);

    public static void ClearCache() => Cache.Clear();

    private static string PickFont()
    {
        using var families = new InstalledFontCollection();
        return families.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
