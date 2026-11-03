using ArqamBase.App.Core;
using ArqamBase.App.Screens;
using Krypton.Navigator;
using Krypton.Ribbon;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>The main window: ribbon, navigation pane, tabbed screens and status bar.</summary>
public sealed class MainForm : KryptonForm
{
    private readonly KryptonManager _manager = new();
    private readonly KryptonRibbon _ribbon = new();
    private readonly NavPane _nav;
    private readonly KryptonNavigator _tabs = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _recordLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly List<(KryptonRibbonGroupButton Button, char Glyph)> _iconButtons = [];
    private readonly List<KryptonRibbonGroupButton> _recordButtons = [], _newButtons = [];
    private readonly Dictionary<string, KryptonRibbonTab> _groupTabs = [];
    private readonly List<(KryptonRibbonTab Tab, KryptonRibbonGroup Group)> _screenGroups = [];
    private readonly HashSet<KryptonRibbonGroupButton> _screenButtons = [];
    private readonly ScreenContext _ctx;
    private readonly UserSettings _settings;

    public MainForm(AppConfig config, UserSession session, UserSettings settings, string? startScreen = null)
    {
        _settings = settings;
        _ctx = new ScreenContext(config, session, settings, OpenScreen, ApplyTheme, (key, target) =>
        {
            OpenScreen(key);
            ActiveScreen?.Navigate(target);
        });

        Text = "ArqamBase";
        Icon = Program.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1400, 900);
        MinimumSize = new Size(900, 600);
        WindowState = FormWindowState.Maximized;
        _manager.GlobalApplyToolstrips = true;
        Theme.Apply(_manager, settings);
        Font = Theme.BaseFont;

        _nav = new NavPane(settings.NavigationPaneWidth, ScreenCatalog.For(session).ToList());
        _nav.ScreenSelected += OpenScreen;
        _nav.SetCollapsed(!settings.NavigationPaneOpen);

        _tabs.Dock = DockStyle.Fill;
        _tabs.NavigatorMode = NavigatorMode.BarTabGroup;
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.ShowEnabled;
        _tabs.Button.CloseButtonAction = CloseButtonAction.RemovePageAndDispose;
        _tabs.Button.ContextButtonDisplay = ButtonDisplay.Hide;
        _tabs.CloseAction += OnTabClosing;
        _tabs.SelectedPageChanged += (_, _) => OnActiveScreenChanged();
        StyleTabs();

        BuildRibbon();
        BuildStatusBar(config, session);

        var splitter = new Splitter { Dock = DockStyle.Left, Width = 4 };
        splitter.SplitterMoved += (_, _) => { if (!_nav.Collapsed) _settings.NavigationPaneWidth = _nav.Width; };
        var body = new KryptonPanel { Dock = DockStyle.Fill };
        body.Controls.Add(_nav);
        body.Controls.Add(splitter);
        body.Controls.Add(_tabs);
        splitter.BringToFront();
        _tabs.BringToFront();
        Controls.Add(_ribbon);
        Controls.Add(_status);
        Controls.Add(body);
        body.BringToFront();

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsThemeChanged;
        // Shown, not Load: errors raised during Load are silently swallowed by Windows on 64-bit.
        Shown += (_, _) =>
        {
            OpenScreen("dashboard");
            if (startScreen is not null && ScreenCatalog.For(session).Any(s => s.Key == startScreen))
                OpenScreen(startScreen);
            if (session.CreatedFirstAdmin)
                KryptonMessageBox.Show($"Welcome. Your Admin account \"{session.UserName}\" is created.\n\nAdd other people in Setup lists > Users & roles.",
                    "ArqamBase", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
        };
    }

    private ScreenBase? ActiveScreen => _tabs.SelectedPage?.Controls.OfType<ScreenBase>().FirstOrDefault();

    // ----- Screens -----

    public void OpenScreen(string key)
    {
        var existing = _tabs.Pages.FirstOrDefault(p => p.UniqueName == key);
        if (existing is not null)
        {
            _tabs.SelectedPage = existing;
            return;
        }

        var info = ScreenCatalog.Get(key);
        if (!ScreenCatalog.For(_ctx.Session).Contains(info)) return; // not for this role
        var t = Timing.Step($"OpenScreen {key}");
        ScreenBase screen;
        try
        {
            screen = ScreenCatalog.CreateScreen(info, _ctx);
            t.Mark("create");
        }
        catch (Exception ex)
        {
            Program.ShowError($"Could not open {info.Title}.", ex);
            return;
        }
        screen.Text = info.Title;
        screen.StatusChanged += (_, _) => { if (ActiveScreen == screen) UpdateStatus(); };

        var page = new KryptonPage { Text = info.Title, TextTitle = info.Title, UniqueName = key, ImageSmall = Icons.Small(info.Glyph) };
        page.Controls.Add(screen);
        _tabs.Pages.Add(page);
        Theme.FitButtons(page);
        t.Mark("pages.add");
        _tabs.SelectedPage = page;
        t.Mark("add tab (loads data)");
        BeginInvoke(() => { t.Mark("first paint"); t.Dispose(); });
    }

    private void OnTabClosing(object? sender, CloseActionEventArgs e)
    {
        var screen = e.Item?.Controls.OfType<ScreenBase>().FirstOrDefault();
        if (screen is not null && !screen.ConfirmLeave())
            e.Action = CloseButtonAction.None;
    }

    private void OnActiveScreenChanged()
    {
        _nav.SetActive(_tabs.SelectedPage?.UniqueName);
        var screen = ActiveScreen;
        foreach (var b in _recordButtons)
            b.Enabled = screen?.SupportsRecords == true;
        ShowScreenCommands(screen);
        UpdateStatus();
    }

    private void UpdateStatus() => _recordLabel.Text = ActiveScreen?.StatusText ?? "";

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var page in _tabs.Pages.ToList())
        {
            var screen = page.Controls.OfType<ScreenBase>().FirstOrDefault();
            if (screen is null || screen.ConfirmLeave()) continue;
            _tabs.SelectedPage = page;
            e.Cancel = true;
            return;
        }

        _settings.NavigationPaneOpen = !_nav.Collapsed;
        if (!_nav.Collapsed) _settings.NavigationPaneWidth = _nav.Width;
        try { _settings.Save(); } catch (IOException) { }
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnWindowsThemeChanged;
        base.OnFormClosing(e);
    }

    // ----- Theme -----

    private void ApplyTheme()
    {
        Theme.Apply(_manager, _settings);
        Font = Theme.BaseFont; // standard controls (drop-downs, lists) take the font from the window
        foreach (var (button, glyph) in _iconButtons)
        {
            button.ImageLarge = Icons.Large(glyph);
            button.ImageSmall = Icons.Small(glyph);
        }
        foreach (var page in _tabs.Pages)
        {
            if (ScreenCatalog.All.FirstOrDefault(s => s.Key == page.UniqueName) is { } info)
                page.ImageSmall = Icons.Small(info.Glyph);
            Theme.FitButtons(page);
        }
        StyleTabs();
        Refresh();
    }

    /// <summary>
    /// Tab text is never bold (bold text did not fit the tab). In Light and Dark the tabs are flat: the selected tab
    /// has the page colour and a hovered one the selection colour, instead of the palette's glossy gold.
    /// </summary>
    private void StyleTabs()
    {
        var font = Theme.BaseFont;
        _tabs.StateCommon.Tab.Content.ShortText.Font = font;
        _tabs.StateNormal.Tab.Content.ShortText.Font = font;
        _tabs.StateTracking.Tab.Content.ShortText.Font = font;
        _tabs.StatePressed.Tab.Content.ShortText.Font = font;
        _tabs.StateSelected.Tab.Content.ShortText.Font = font;
        var flat = !Theme.IsOffice2007;
        void Set(PaletteBack back, PaletteContent content, Color color)
        {
            back.ColorStyle = flat ? PaletteColorStyle.Solid : PaletteColorStyle.Inherit;
            back.Color1 = back.Color2 = flat ? color : Color.Empty;
            content.ShortText.Color1 = flat ? Theme.Text : Color.Empty;
        }
        Set(_tabs.StateSelected.Tab.Back, _tabs.StateSelected.Tab.Content, Theme.CardBack);
        Set(_tabs.StateTracking.Tab.Back, _tabs.StateTracking.Tab.Content, Theme.Selected);
        Set(_tabs.StatePressed.Tab.Back, _tabs.StatePressed.Tab.Content, Theme.Selected);
    }

    /// <summary>Makes all text one point larger or smaller, within the allowed range.</summary>
    private void ChangeFontSize(int delta)
    {
        var size = Theme.ClampFontSize(_settings.FontSize + delta);
        if (size == _settings.FontSize) return;
        _settings.FontSize = size;
        try { _settings.Save(); } catch (IOException) { }
        ApplyTheme();
    }

    private void OnWindowsThemeChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == ThemeChoice.FollowWindows && e.Category == Microsoft.Win32.UserPreferenceCategory.General
            && Theme.WindowsUsesDarkMode() != Theme.IsDark)
            BeginInvoke(ApplyTheme);
    }

    // ----- Ribbon -----

    private void BuildRibbon()
    {
        _ribbon.ShowMinimizeButton = true;
        // Krypton hides the window's close (X) button by default when the ribbon is in the title bar.
        _ribbon.RibbonFileAppButton.FormCloseBoxVisible = true;
        var exit = new KryptonContextMenuItem("Exit ArqamBase") { Image = Icons.Small(Icons.Exit) };
        exit.Click += (_, _) => Close();
        _ribbon.RibbonFileAppButton.AppButtonMenuItems.Add(exit);
        _ribbon.RibbonFileAppButton.AppButtonShowRecentDocs = false;
        _ribbon.RibbonFileAppButton.AppButtonImage = new Icon(Program.AppIcon, 32, 32).ToBitmap();
        _ribbon.RibbonTabs.Add(Tab("Home",
            RecordsGroup(),
            SortFilterGroup(),
            Group("Find", Btn("Find", Icons.Search, () => ActiveScreen?.FocusSearch()), Btn("Find screen", Icons.Menu, _nav.FocusSearch)),
            Group("Go to", Btn("Dashboard", Icons.Home, () => OpenScreen("dashboard"))),
            Group("View", Btn("Navigation pane", Icons.Menu, () => _nav.SetCollapsed(!_nav.Collapsed)), Btn("Theme", Icons.ThemeIcon, () => OpenScreen("settings")),
                Btn("Larger text", Icons.FontBigger, () => ChangeFontSize(+1)), Btn("Smaller text", Icons.FontSmaller, () => ChangeFontSize(-1)))));

        foreach (var group in ScreenCatalog.For(_ctx.Session).Where(s => s.Group != "Home").GroupBy(s => s.Group))
        {
            var tabName = group.Key == "Staff & salaries" ? "Staff" : group.Key;
            var screens = Group(group.Key == "Staff & salaries" ? "Staff" : "Screens",
                group.Select(s => Btn(s.Title, s.Glyph, () => OpenScreen(s.Key))).ToArray());
            var tab = Tab(tabName, screens, RecordsGroup(), SortFilterGroup());
            _groupTabs[group.Key] = tab;
            _ribbon.RibbonTabs.Add(tab);
        }
        _ribbon.SelectedTab = _ribbon.RibbonTabs[0];
    }

    private KryptonRibbonGroup RecordsGroup()
    {
        var buttons = new[]
        {
            Btn("New", Icons.Add, () => ActiveScreen?.NewRecord()),
            Btn("Save", Icons.Save, () => ActiveScreen?.SaveChanges()),
            Btn("Delete", Icons.Delete, () => ActiveScreen?.DeleteRecord()),
            Btn("Undo changes", Icons.Undo, () => ActiveScreen?.Reload()),
        };
        _recordButtons.AddRange(buttons);
        _newButtons.Add(buttons[0]);
        return Group("Records", buttons);
    }

    /// <summary>Shows the active screen's own ribbon groups (after Records, on Home and on the screen's tab) and its New button text.</summary>
    private void ShowScreenCommands(ScreenBase? screen)
    {
        foreach (var (tab, group) in _screenGroups)
            tab.Groups.Remove(group);
        _screenGroups.Clear();
        _iconButtons.RemoveAll(b => _screenButtons.Contains(b.Button));
        _screenButtons.Clear();

        var words = (screen?.NewText ?? "New").Split(' ', 2);
        foreach (var b in _newButtons)
        {
            b.TextLine1 = words[0];
            b.TextLine2 = words.Length > 1 ? words[1] : "";
        }

        if (screen is null || screen.Commands.Count == 0) return;
        var tabs = new List<KryptonRibbonTab> { _ribbon.RibbonTabs[0] };
        var key = _tabs.SelectedPage?.UniqueName;
        if (ScreenCatalog.All.FirstOrDefault(s => s.Key == key) is { } info && _groupTabs.TryGetValue(info.Group, out var own))
            tabs.Add(own);
        foreach (var tab in tabs)
        {
            var at = tab.Groups.Cast<KryptonRibbonGroup>().ToList().FindIndex(g => g.TextLine1 == "Records") + 1;
            foreach (var commands in screen.Commands)
            {
                var buttons = commands.Commands.Select(c => Btn(c.Text, c.Glyph, c.Run)).ToArray();
                _screenButtons.UnionWith(buttons);
                var group = Group(commands.Title, buttons);
                tab.Groups.Insert(at++, group);
                _screenGroups.Add((tab, group));
            }
        }
    }

    private KryptonRibbonGroup SortFilterGroup()
    {
        var buttons = new[]
        {
            Btn("Sort A to Z", Icons.SortUp, () => ActiveScreen?.SortAscending()),
            Btn("Sort Z to A", Icons.SortDown, () => ActiveScreen?.SortDescending()),
            Btn("Clear filters", Icons.Clear, () => ActiveScreen?.ClearFilters()),
        };
        _recordButtons.AddRange(buttons);
        return Group("Sort & filter", buttons);
    }

    private static KryptonRibbonTab Tab(string text, params KryptonRibbonGroup[] groups)
    {
        var tab = new KryptonRibbonTab { Text = text };
        tab.Groups.AddRange(groups);
        return tab;
    }

    private static KryptonRibbonGroup Group(string text, params KryptonRibbonGroupButton[] buttons)
    {
        var group = new KryptonRibbonGroup { TextLine1 = text, DialogBoxLauncher = false };
        foreach (var chunk in buttons.Chunk(3))
        {
            var triple = new KryptonRibbonGroupTriple { MaximumSize = GroupItemSize.Large, MinimumSize = GroupItemSize.Large };
            triple.Items!.AddRange(chunk);
            group.Items.Add(triple);
        }
        return group;
    }

    private KryptonRibbonGroupButton Btn(string text, char glyph, Action click)
    {
        var words = text.Split(' ', 2);
        var button = new KryptonRibbonGroupButton
        {
            TextLine1 = words[0],
            TextLine2 = words.Length > 1 ? words[1] : "",
            ImageLarge = Icons.Large(glyph),
            ImageSmall = Icons.Small(glyph),
        };
        button.Click += (_, _) => click();
        _iconButtons.Add((button, glyph));
        return button;
    }

    // ----- Status bar -----

    private void BuildStatusBar(AppConfig config, UserSession session)
    {
        _status.SizingGrip = false;
        _status.Items.Add(new ToolStripStatusLabel($"Server: {config.Server}"));
        _status.Items.Add(new ToolStripStatusLabel($"Database: {config.Database}") { BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched });
        _status.Items.Add(new ToolStripStatusLabel($"User: {session.UserName}") { BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched });
        _status.Items.Add(new ToolStripStatusLabel($"SQL login: {session.SqlLoginName}") { BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched });
        _status.Items.Add(new ToolStripStatusLabel($"Role: {UserSession.RoleLabel(session.Role)}") { BorderSides = ToolStripStatusLabelBorderSides.Left, BorderStyle = Border3DStyle.Etched });
        _status.Items.Add(_recordLabel);
    }
}
