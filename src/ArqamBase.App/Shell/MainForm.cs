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
    private readonly List<KryptonRibbonGroupButton> _recordButtons = [];
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
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1400, 900);
        MinimumSize = new Size(900, 600);
        WindowState = FormWindowState.Maximized;
        _manager.GlobalApplyToolstrips = true;
        Theme.Apply(_manager, settings.Theme);

        _nav = new NavPane(settings.NavigationPaneWidth);
        _nav.ScreenSelected += OpenScreen;
        _nav.SetCollapsed(!settings.NavigationPaneOpen);

        _tabs.Dock = DockStyle.Fill;
        _tabs.NavigatorMode = NavigatorMode.BarTabGroup;
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.ShowEnabled;
        _tabs.Button.CloseButtonAction = CloseButtonAction.RemovePageAndDispose;
        _tabs.Button.ContextButtonDisplay = ButtonDisplay.Hide;
        _tabs.CloseAction += OnTabClosing;
        _tabs.SelectedPageChanged += (_, _) => OnActiveScreenChanged();

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
            if (startScreen is not null && ScreenCatalog.All.Any(s => s.Key == startScreen))
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
        ScreenBase screen;
        try
        {
            screen = ScreenCatalog.CreateScreen(info, _ctx);
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
        _tabs.SelectedPage = page;
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
        Theme.Apply(_manager, _settings.Theme);
        foreach (var (button, glyph) in _iconButtons)
        {
            button.ImageLarge = Icons.Large(glyph);
            button.ImageSmall = Icons.Small(glyph);
        }
        foreach (var page in _tabs.Pages)
            if (ScreenCatalog.All.FirstOrDefault(s => s.Key == page.UniqueName) is { } info)
                page.ImageSmall = Icons.Small(info.Glyph);
        Refresh();
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
        _ribbon.RibbonTabs.Add(Tab("Home",
            RecordsGroup(),
            SortFilterGroup(),
            Group("Find", Btn("Find", Icons.Search, () => ActiveScreen?.FocusSearch()), Btn("Find screen", Icons.Menu, _nav.FocusSearch)),
            Group("Go to", Btn("Dashboard", Icons.Home, () => OpenScreen("dashboard"))),
            Group("View", Btn("Navigation pane", Icons.Menu, () => _nav.SetCollapsed(!_nav.Collapsed)), Btn("Theme", Icons.ThemeIcon, () => OpenScreen("settings")))));

        foreach (var group in ScreenCatalog.All.Where(s => s.Group != "Home").GroupBy(s => s.Group))
        {
            var tabName = group.Key == "Staff & salaries" ? "Staff" : group.Key;
            var screens = Group(group.Key == "Staff & salaries" ? "Staff" : "Screens",
                group.Select(s => Btn(s.Title, s.Glyph, () => OpenScreen(s.Key))).ToArray());
            _ribbon.RibbonTabs.Add(Tab(tabName, screens, RecordsGroup(), SortFilterGroup()));
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
        return Group("Records", buttons);
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
