using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>Per-user settings. Email, SMS and WhatsApp settings are added in phase 5.</summary>
public sealed class SettingsScreen : ScreenBase
{
    private readonly Dictionary<ThemeChoice, KryptonRadioButton> _themes = [];
    private readonly KryptonCheckBox _navOpen = new() { Text = "Start with the navigation pane open", AutoSize = true };

    public SettingsScreen(ScreenContext ctx) : base(ctx)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };

        var appearance = new KryptonGroupBox { Text = "Appearance", Width = 520, Height = 200 };
        var inner = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, Padding = new Padding(6) };
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
        appearance.Panel.Controls.Add(inner);

        var account = new KryptonGroupBox { Text = "Your account", Width = 520, Height = 190 };
        var accountFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, Padding = new Padding(6) };
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

    private void Persist()
    {
        try { Ctx.Settings.Save(); }
        catch (IOException ex) { Program.ShowError("Could not save your settings.", ex); }
    }
}
