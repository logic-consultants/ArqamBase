using Krypton.Toolkit;
using Microsoft.Win32;

namespace ArqamBase.App.Core;

/// <summary>
/// The colours of one theme (from the Office 365-inspired theme sheet): primary (ribbon highlights, navigation pane, main buttons),
/// accent (hover), background (behind the cards), surface (cards, grids), border and text.
/// </summary>
public sealed record ThemeColors(Color Primary, Color Accent, Color Background, Color Surface, Color Border, Color Text, bool Dark);

/// <summary>
/// Applies the chosen theme to the whole app. Blue, Silver, Black and Green are flat (matte) themes built from
/// <see cref="ThemeColors"/> with a coloured navigation pane; Classic 2007 Blue is Krypton's glossy Office 2007 look.
/// </summary>
public static class Theme
{
    private static readonly Dictionary<ThemeChoice, ThemeColors> Palettes = new()
    {
        [ThemeChoice.Blue] = new(Hex("#0F6CBD"), Hex("#5B9BD5"), Hex("#F5F8FF"), Hex("#FFFFFF"), Hex("#D6E2F1"), Hex("#1F2A44"), false),
        [ThemeChoice.Silver] = new(Hex("#6B7280"), Hex("#9CA3AF"), Hex("#F8F9FB"), Hex("#FFFFFF"), Hex("#E2E8F0"), Hex("#111827"), false),
        [ThemeChoice.Black] = new(Hex("#0F172A"), Hex("#334155"), Hex("#0B0F19"), Hex("#1F2937"), Hex("#374151"), Hex("#F9FAFB"), true),
        [ThemeChoice.Green] = new(Hex("#107C41"), Hex("#34A853"), Hex("#F2FBF6"), Hex("#FFFFFF"), Hex("#D1E7DD"), Hex("#0F2419"), false),
    };

    // Classic 2007 Blue keeps the Office 2007 colours (navy icons, light blue selection).
    private static readonly ThemeColors Classic = new(Color.FromArgb(0x15, 0x42, 0x8B), Hex("#3979B5"), Hex("#E3EFFB"), Color.FromArgb(0xFA, 0xFC, 0xFF),
        Color.FromArgb(0xD9, 0xD9, 0xD9), Color.FromArgb(0x1F, 0x1F, 0x1F), false);

    private static ThemeColors _c = Palettes[ThemeChoice.Blue];

    public static bool IsDark => _c.Dark;
    public static bool IsOffice2007 { get; private set; }
    /// <summary>The flat themes colour the navigation pane with the primary colour.</summary>
    public static bool ColouredNavigation => !IsOffice2007;

    public static Color Primary => _c.Primary;
    /// <summary>Icon and highlight colour.</summary>
    public static Color Accent => _c.Dark ? Hex("#93C5FD") : _c.Primary;
    public static Color Background => _c.Background;
    public static Color Text => _c.Text;
    public static Color Muted => Mix(_c.Text, _c.Surface, 0.38);
    public static Color Warning => _c.Dark ? Hex("#F3C27A") : Hex("#9A5800");
    public static Color Good => _c.Dark ? Hex("#8FD6A0") : Hex("#1E7E34");
    public static Color Error => _c.Dark ? Hex("#F28B87") : Hex("#C9302C");
    /// <summary>Background of the white boxes (cards) that group fields, and their border.</summary>
    public static Color CardBack => _c.Surface;
    public static Color CardBorder => _c.Border;
    /// <summary>Background of the selected item in a list: a light tint of the primary colour.</summary>
    public static Color Selected => IsOffice2007 ? Color.FromArgb(0xD5, 0xE3, 0xF5) : Mix(_c.Primary, _c.Surface, _c.Dark ? 0.45 : 0.86);
    /// <summary>Hovered buttons and tabs.</summary>
    public static Color Hover => Mix(_c.Accent, _c.Surface, _c.Dark ? 0.5 : 0.82);

    /// <summary>Navigation pane colours (flat themes): primary background, white text, a lighter band for the open screen.</summary>
    public static Color NavBack => _c.Dark ? _c.Surface : _c.Primary;
    public static Color NavText => Color.White;
    public static Color NavMuted => Mix(Color.White, NavBack, 0.4);
    public static Color NavSelected => _c.Dark ? _c.Accent : Mix(_c.Primary, Color.White, 0.2);

    public static event EventHandler? Changed;

    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>A mixed with B: <paramref name="amountOfB"/> 0 gives A, 1 gives B.</summary>
    public static Color Mix(Color a, Color b, double amountOfB) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * amountOfB), (int)Math.Round(a.G + (b.G - a.G) * amountOfB), (int)Math.Round(a.B + (b.B - a.B) * amountOfB));

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

    // ----- Applying -----

    public static void Apply(KryptonManager manager, UserSettings settings)
    {
        SetFont(settings.FontName, settings.FontSize);
        manager.BaseFont = BaseFont;
        Apply(manager, settings.Theme);
    }

    private static void Apply(KryptonManager manager, ThemeChoice choice)
    {
        if (choice == ThemeChoice.FollowWindows) choice = WindowsUsesDarkMode() ? ThemeChoice.Black : ThemeChoice.Blue;
        IsOffice2007 = choice == ThemeChoice.Classic2007Blue;
        _c = IsOffice2007 ? Classic : Palettes[choice];
        if (IsOffice2007)
            manager.GlobalPaletteMode = PaletteMode.Office2007Blue; // glossy: buttons glow gold/orange on hover and press
        else
        {
            manager.GlobalCustomPalette = BuildPalette(_c);
            manager.GlobalPaletteMode = PaletteMode.Custom;
        }
        Icons.ClearCache();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>A flat palette from the theme colours, on top of Krypton's flat Office 2013 (light) or Material dark palette.</summary>
    private static KryptonCustomPaletteBase BuildPalette(ThemeColors c)
    {
        var p = new KryptonCustomPaletteBase { BasePalette = KryptonManager.GetPaletteForMode(c.Dark ? PaletteMode.MaterialDark : PaletteMode.Office2013White) };
        var hover = Hover;
        var pressed = Mix(c.Primary, c.Surface, c.Dark ? 0.35 : 0.72);
        var onPrimary = Color.White;

        // Panels: the window background behind the cards.
        foreach (var panel in new[] { p.PanelStyles.PanelClient, p.PanelStyles.PanelAlternate, p.PanelStyles.PanelCommon })
            Solid(panel.StateCommon, c.Background);
        Solid(p.ControlStyles.ControlClient.StateCommon.Back, c.Background);

        // Ordinary buttons: surface with a border; accent tint on hover, primary tint when pressed. No gloss.
        foreach (var button in new[] { p.ButtonStyles.ButtonStandalone, p.ButtonStyles.ButtonAlternate, p.ButtonStyles.ButtonCluster, p.ButtonStyles.ButtonCommand, p.ButtonStyles.ButtonGallery })
        {
            ButtonState(button.StateNormal, c.Surface, c.Border, c.Text);
            ButtonState(button.StateTracking, hover, c.Accent, c.Text);
            ButtonState(button.StatePressed, pressed, c.Primary, c.Text);
            ButtonState(button.StateCheckedNormal, pressed, c.Primary, c.Text);
            ButtonState(button.StateCheckedTracking, pressed, c.Primary, c.Text);
            ButtonState(button.StateCheckedPressed, pressed, c.Primary, c.Text);
            button.StateCommon.Border.Rounding = 3;
        }
        // Main action buttons (Save receipt, Generate fees, Sign in): filled with the primary colour.
        var main = p.ButtonStyles.ButtonCustom1;
        ButtonState(main.StateNormal, c.Primary, c.Primary, onPrimary);
        ButtonState(main.StateTracking, Mix(c.Primary, Color.Black, 0.12), Mix(c.Primary, Color.Black, 0.12), onPrimary);
        ButtonState(main.StatePressed, Mix(c.Primary, Color.Black, 0.25), Mix(c.Primary, Color.Black, 0.25), onPrimary);
        ButtonState(main.StateDisabled, Mix(c.Primary, c.Surface, 0.6), Mix(c.Primary, c.Surface, 0.6), Mix(onPrimary, c.Surface, 0.2));
        main.StateCommon.Border.Rounding = 3;
        // The default (Enter) and focused button would otherwise take the base palette's pale colours.
        foreach (var o in new[] { main.OverrideDefault, main.OverrideFocus })
        {
            Solid(o.Back, c.Primary);
            o.Border.ColorStyle = PaletteColorStyle.Solid;
            o.Border.Color1 = o.Border.Color2 = Mix(c.Primary, Color.Black, 0.2);
            o.Content.ShortText.Color1 = o.Content.ShortText.Color2 = onPrimary;
        }
        foreach (var o in new[] { p.ButtonStyles.ButtonStandalone.OverrideDefault, p.ButtonStyles.ButtonStandalone.OverrideFocus })
        {
            Solid(o.Back, c.Surface);
            o.Border.ColorStyle = PaletteColorStyle.Solid;
            o.Border.Color1 = o.Border.Color2 = c.Primary;
            o.Content.ShortText.Color1 = o.Content.ShortText.Color2 = c.Text;
        }

        // Ribbon tabs: the selected tab on the card colour (Material dark would make it light cyan).
        foreach (var t in new[] { p.Ribbon.RibbonTab.StateCheckedNormal, p.Ribbon.RibbonTab.StateCheckedTracking })
        {
            t.BackColor1 = t.BackColor2 = t.BackColor3 = t.BackColor4 = t.BackColor5 = c.Surface;
        }

        // Tabs: flat; the selected one has the card colour.
        var tab = p.TabStyles.TabCommon;
        Solid(tab.StateNormal.Back, c.Background);
        Solid(tab.StateTracking.Back, hover);
        Solid(tab.StatePressed.Back, pressed);
        Solid(tab.StateSelected.Back, c.Surface);
        tab.StateCommon.Content.ShortText.Color1 = c.Text;

        // Grids: surface cells, background headings, primary tint for the selected row.
        foreach (var grid in new[] { p.GridStyles.GridCommon, p.GridStyles.GridList, p.GridStyles.GridSheet })
        {
            Solid(grid.StateCommon.Background, c.Surface);
            Solid(grid.StateCommon.HeaderColumn.Back, c.Background);
            Solid(grid.StateCommon.HeaderRow.Back, c.Background);
            grid.StateCommon.HeaderColumn.Border.Color1 = c.Border;
            grid.StateCommon.HeaderColumn.Content.Color1 = c.Text;
            Solid(grid.StateCommon.DataCell.Back, c.Surface);
            grid.StateCommon.DataCell.Border.Color1 = c.Border;
            grid.StateCommon.DataCell.Content.Color1 = c.Text;
            Solid(grid.StateSelected.DataCell.Back, Selected);
            grid.StateSelected.DataCell.Content.Color1 = c.Text;
        }

        // Ribbon: the selected tab's text and the File tab in the primary colour.
        p.Ribbon.RibbonTab.StateCheckedNormal.TextColor = c.Dark ? c.Text : c.Primary;
        p.Ribbon.RibbonTab.StateCheckedTracking.TextColor = c.Dark ? c.Text : c.Primary;
        p.Ribbon.RibbonFileAppTab.RibbonFileAppTabTopColor = c.Primary;
        p.Ribbon.RibbonFileAppTab.RibbonFileAppTabBottomColor = c.Primary;
        p.Ribbon.RibbonFileAppTab.RibbonFileAppTabTextColor = onPrimary;
        return p;
    }

    private static void Solid(PaletteBack back, Color color)
    {
        back.ColorStyle = PaletteColorStyle.Solid;
        back.Color1 = back.Color2 = color;
    }

    private static void Solid(KryptonPalettePanel panel, Color color) => Solid(panel.StateCommon, color);

    private static void ButtonState(PaletteTriple state, Color back, Color border, Color text)
    {
        Solid(state.Back, back);
        state.Border.ColorStyle = PaletteColorStyle.Solid;
        state.Border.Color1 = state.Border.Color2 = border;
        state.Content.ShortText.Color1 = text;
        state.Content.ShortText.Color2 = text;
    }

    /// <summary>Makes a button a main action button (filled with the primary colour in the flat themes).</summary>
    public static void MainButton(KryptonButton button) => button.ButtonStyle = ButtonStyle.Custom1;

    /// <summary>Widens (and heightens) every button under a control that is too small for its text, e.g. after a larger font size.</summary>
    public static void FitButtons(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is KryptonButton b && b.Dock != DockStyle.Fill && !b.AutoSize)
            {
                var preferred = b.GetPreferredSize(new Size(int.MaxValue, int.MaxValue));
                if (preferred.Width > b.Width) b.Width = preferred.Width;
                if (preferred.Height > b.Height) b.Height = preferred.Height;
            }
            if (c.HasChildren) FitButtons(c);
        }
    }

    public static bool WindowsUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>The themes in the order Settings lists them.</summary>
    public static IReadOnlyList<ThemeChoice> Choices { get; } =
        [ThemeChoice.Blue, ThemeChoice.Silver, ThemeChoice.Black, ThemeChoice.Green, ThemeChoice.Classic2007Blue, ThemeChoice.FollowWindows];

    public static string Label(ThemeChoice c) => c switch
    {
        ThemeChoice.FollowWindows => "Follow Windows (Blue, or Black when Windows is dark)",
        ThemeChoice.Classic2007Blue => "Classic Office 2007 Blue",
        ThemeChoice.Black => "Black (dark)",
        _ => c.ToString()
    };
}
