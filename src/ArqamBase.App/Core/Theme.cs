using Krypton.Toolkit;
using Microsoft.Win32;

namespace ArqamBase.App.Core;

/// <summary>Applies Light, Dark, Follow Windows or Office 2007 Blue to the whole app.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }
    public static bool IsOffice2007 { get; private set; }

    /// <summary>Icon and highlight colour: Access-style maroon, or Office 2007 navy in that theme.</summary>
    public static Color Accent => IsOffice2007 ? Color.FromArgb(0x15, 0x42, 0x8B)
        : IsDark ? Color.FromArgb(0xF0, 0x8A, 0x8C) : Color.FromArgb(0xA4, 0x37, 0x3A);
    public static Color Muted => IsDark ? Color.FromArgb(0xB8, 0xB8, 0xB8) : Color.FromArgb(0x59, 0x59, 0x59);
    public static Color Warning => IsDark ? Color.FromArgb(0xF3, 0xC2, 0x7A) : Color.FromArgb(0x8A, 0x4B, 0x00);
    public static Color Good => IsDark ? Color.FromArgb(0x8F, 0xD6, 0xA0) : Color.FromArgb(0x1E, 0x6B, 0x34);
    public static Color Text => IsDark ? Color.FromArgb(0xEE, 0xEE, 0xEE) : Color.FromArgb(0x1F, 0x1F, 0x1F);
    /// <summary>Background of the white boxes (cards) that group fields, and their border.</summary>
    public static Color CardBack => IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : IsOffice2007 ? Color.FromArgb(0xFA, 0xFC, 0xFF) : Color.White;
    public static Color CardBorder => IsDark ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0xD9, 0xD9, 0xD9);
    /// <summary>Background of the selected item in a list: a light tint of the accent.</summary>
    public static Color Selected => IsOffice2007 ? Color.FromArgb(0xD5, 0xE3, 0xF5)
        : IsDark ? Color.FromArgb(0x4A, 0x2C, 0x2D) : Color.FromArgb(0xF7, 0xDE, 0xDF);

    public static event EventHandler? Changed;

    // ----- Font -----

    /// <summary>Text sizes users can choose, in points. Kept small so screens keep their layout.</summary>
    public const int MinFontSize = 10, MaxFontSize = 18, DefaultFontSize = 10;
    public const string DefaultFontName = "Segoe UI";

    /// <summary>Plain, easy-to-read fonts users can choose from (only those installed on this computer).</summary>
    public static IReadOnlyList<string> FontChoices { get; } =
        new[] { "Segoe UI", "Calibri", "Arial", "Tahoma", "Verdana", "Microsoft Sans Serif", "Cambria" }
            .Where(name => FontFamily.Families.Any(f => f.Name == name)).ToList();

    /// <summary>The app's text font, as chosen in Settings.</summary>
    public static Font BaseFont { get; private set; } = new(DefaultFontName, DefaultFontSize);
    private static readonly Dictionary<(float, FontStyle), Font> ScaledFonts = [];

    /// <summary>The app font at a relative size (e.g. 0.85 for captions), cached.</summary>
    public static Font Scaled(float factor, FontStyle style = FontStyle.Regular)
    {
        if (!ScaledFonts.TryGetValue((factor, style), out var font))
            ScaledFonts[(factor, style)] = font = new Font(BaseFont.FontFamily, BaseFont.SizeInPoints * factor, style);
        return font;
    }

    /// <summary>Height of a one-line grid or list row for the current font.</summary>
    public static int RowHeight => BaseFont.Height + 10;

    public static int ClampFontSize(int size) => Math.Clamp(size, MinFontSize, MaxFontSize);

    private static void SetFont(string? name, int size)
    {
        name = name is not null && FontChoices.Contains(name) ? name : DefaultFontName;
        size = ClampFontSize(size);
        if (BaseFont.Name == name && (int)BaseFont.SizeInPoints == size) return;
        BaseFont = new Font(name, size);
        ScaledFonts.Clear(); // old fonts may still be on screen, so they are left for the garbage collector
    }

    public static void Apply(KryptonManager manager, UserSettings settings)
    {
        SetFont(settings.FontName, settings.FontSize);
        manager.BaseFont = BaseFont;
        Apply(manager, settings.Theme);
    }

    private static void Apply(KryptonManager manager, ThemeChoice choice)
    {
        IsOffice2007 = choice == ThemeChoice.Office2007Blue;
        IsDark = choice switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.FollowWindows => WindowsUsesDarkMode(),
            _ => false
        };
        // Office 2007 Blue: blue ribbon, buttons glow gold/orange on hover and press.
        manager.GlobalPaletteMode = IsOffice2007 ? PaletteMode.Office2007Blue
            : IsDark ? PaletteMode.Microsoft365BlackDarkMode : PaletteMode.Microsoft365White;
        Icons.ClearCache();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool WindowsUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    public static string Label(ThemeChoice c) => c switch
    {
        ThemeChoice.FollowWindows => "Follow Windows",
        ThemeChoice.Office2007Blue => "Office 2007 Blue",
        _ => c.ToString()
    };
}
