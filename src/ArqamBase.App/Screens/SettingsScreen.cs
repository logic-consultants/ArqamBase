using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>Per-user settings. Email, SMS and WhatsApp settings are added in phase 5.</summary>
public sealed class SettingsScreen : ScreenBase
{
    private readonly Dictionary<ThemeChoice, KryptonRadioButton> _themes = [];
    private readonly KryptonCheckBox _navOpen = new() { Text = "Start with the navigation pane open", AutoSize = true };
    private readonly KryptonComboBox _fontName = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonNumericUpDown _fontSize = new() { Minimum = Theme.MinFontSize, Maximum = Theme.MaxFontSize };
    private readonly KryptonButton _smaller = new() { Text = "A−" }, _bigger = new() { Text = "A+" }, _reset = new() { Text = "Default" };
    private bool _syncing;

    public SettingsScreen(ScreenContext ctx) : base(ctx)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };

        var appearance = new KryptonGroupBox { Text = "Appearance", Width = 560, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var inner = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(6) };
        foreach (var choice in Enum.GetValues<ThemeChoice>())
        {
            var rb = new KryptonRadioButton { Text = Theme.Label(choice), AutoSize = true, Checked = ctx.Settings.Theme == choice };
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { ctx.Settings.Theme = choice; Persist(); ctx.ApplyTheme(); } };
            _themes[choice] = rb;
            inner.Controls.Add(rb);
        }
        _navOpen.Checked = ctx.Settings.NavigationPaneOpen;
        _navOpen.CheckedChanged += (_, _) => { ctx.Settings.NavigationPaneOpen = _navOpen.Checked; Persist(); };
        inner.Controls.Add(new Panel { Height = 6, Width = 10, BackColor = Color.Transparent });
        inner.Controls.Add(_navOpen);
        inner.Controls.Add(new Panel { Height = 12, Width = 10, BackColor = Color.Transparent });
        inner.Controls.Add(FontSettings());
        appearance.Panel.Controls.Add(inner);

        var account = new KryptonGroupBox { Text = "Your account", Width = 560, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var accountFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(6) };
        accountFlow.Controls.Add(new KryptonLabel
        {
            AutoSize = true,
            Text = $"User: {ctx.Session.UserName} ({UserSession.RoleLabel(ctx.Session.Role)})\nSQL Server login: {ctx.Session.SqlLoginName}\nDatabase: {ctx.Config.Database} on {ctx.Config.Server}"
        });
        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
        var change = new KryptonButton { Text = "Change my password…", AutoSize = true };
        change.Click += (_, _) => { using var f = new Shell.ChangePasswordForm(ctx.Config, ctx.Session, required: false); f.ShowDialog(this); };
        var forget = new KryptonButton { Text = "Forget my saved SQL Server login", AutoSize = true, Enabled = SqlLoginStore.Load(ctx.Session.UserName) is not null };
        forget.Click += (_, _) =>
        {
            SqlLoginStore.Forget(ctx.Session.UserName);
            forget.Enabled = false;
            KryptonMessageBox.Show(this, "Done. You will be asked for your SQL Server login next time you sign in on this computer.", "Settings",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
        };
        buttons.Controls.Add(change);
        buttons.Controls.Add(forget);
        accountFlow.Controls.Add(buttons);
        accountFlow.Controls.Add(new KryptonLabel { AutoSize = true, Text = "Appearance settings are saved for you only, on this computer." });
        account.Panel.Controls.Add(accountFlow);

        flow.Controls.Add(appearance);
        flow.Controls.Add(account);
        flow.Controls.Add(new KryptonLabel { AutoSize = true, Text = "Email, SMS and WhatsApp settings come with the Messages module (phase 5)." });

        Controls.Add(flow);
        Controls.Add(Heading("Settings"));
    }

    /// <summary>Font and text size (10 to 18), with A− and A+ buttons and a sample line.</summary>
    private Control FontSettings()
    {
        var box = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        box.Controls.Add(new KryptonLabel { Text = "Text font and size", LabelStyle = LabelStyle.BoldControl, AutoSize = true });

        _fontName.Items.AddRange(Theme.FontChoices.Cast<object>().ToArray());
        _smaller.Click += (_, _) => { if (_fontSize.Value > _fontSize.Minimum) _fontSize.Value--; };
        _bigger.Click += (_, _) => { if (_fontSize.Value < _fontSize.Maximum) _fontSize.Value++; };
        _reset.Click += (_, _) => ChangeFont(Theme.DefaultFontName, Theme.DefaultFontSize);
        _fontName.SelectedIndexChanged += (_, _) => { if (!_syncing) ChangeFont(_fontName.Text, (int)_fontSize.Value); };
        _fontSize.ValueChanged += (_, _) => { if (!_syncing) ChangeFont(_fontName.Text, (int)_fontSize.Value); };

        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent };
        row.Controls.AddRange([_fontName, _smaller, _fontSize, _bigger, _reset]);
        box.Controls.Add(row);
        box.Controls.Add(new KryptonLabel { Text = "Sample: MARYAM NAEEM · CC 2562 · Rs 12,800 due", AutoSize = true });
        box.Controls.Add(new KryptonLabel { Text = $"Sizes {Theme.MinFontSize} to {Theme.MaxFontSize}. The ribbon's View group also has Larger text and Smaller text.", AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Muted } } });
        ShowFont();
        Theme.Changed += OnThemeChanged;
        return box;
    }

    private void ChangeFont(string name, int size)
    {
        if (name == Ctx.Settings.FontName && size == Ctx.Settings.FontSize) return;
        Ctx.Settings.FontName = name;
        Ctx.Settings.FontSize = Theme.ClampFontSize(size);
        Persist();
        Ctx.ApplyTheme();
    }

    /// <summary>Shows the current font (it can also change from the ribbon).</summary>
    private void ShowFont()
    {
        _syncing = true;
        _fontName.SelectedItem = Theme.BaseFont.Name;
        _fontSize.Value = Theme.ClampFontSize((int)Theme.BaseFont.SizeInPoints);
        _syncing = false;

        // Sized from the text so they fit at every font size (Krypton's AutoSize buttons came out too wide).
        int TextWidth(string text) => TextRenderer.MeasureText(text, Theme.BaseFont).Width;
        var height = Theme.RowHeight + 2;
        foreach (var b in new[] { _smaller, _bigger, _reset })
            b.Size = new Size(TextWidth(b.Text) + 24, height);
        _fontName.Width = TextWidth("Microsoft Sans Serif") + 40;
        _fontSize.Width = TextWidth("18") + 40;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ShowFont();

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private void Persist()
    {
        try { Ctx.Settings.Save(); }
        catch (IOException ex) { Program.ShowError("Could not save your settings.", ex); }
    }
}
