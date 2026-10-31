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

    public static event EventHandler? Changed;

    public static void Apply(KryptonManager manager, ThemeChoice choice)
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
