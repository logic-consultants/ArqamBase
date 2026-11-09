using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>
/// The WhatsApp and email accounts messages go out through, and where test messages go. Kept encrypted for this Windows user
/// on this computer, so each computer that sends is set up once.
/// </summary>
public sealed class MessageSettingsScreen : ScreenBase
{
    private readonly MessageSettings _settings = MessageSettings.Load();
    private readonly BindingSource _bs = new();
    private readonly KryptonCheckBox _test = new() { Text = "Test mode: send everything to the test number and email", AutoSize = true };
    private readonly KryptonLabel _state = new() { AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private bool _dirty;

    public MessageSettingsScreen(ScreenContext ctx) : base(ctx)
    {
        _bs.DataSource = _settings;

        var whatsApp = new FieldPanel(2);
        whatsApp.Add("Provider", Inputs.Combo<string>(_bs, nameof(MessageSettings.WhatsAppProvider), [(MessageSettings.WppConnect, MessageSettings.WppConnect), (MessageSettings.GreenApi, MessageSettings.GreenApi), (MessageSettings.WaApi, MessageSettings.WaApi)]));
        whatsApp.Add("Server address (own gateway)", Inputs.Text(_bs, nameof(MessageSettings.ServerUrl)));
        whatsApp.Add("Instance or session", Inputs.Text(_bs, nameof(MessageSettings.Instance)));
        whatsApp.Add("API key or token", Secret(nameof(MessageSettings.ApiKey)));
        var whatsAppCard = new Card("WhatsApp");
        whatsAppCard.AddTop(whatsApp);

        var email = new FieldPanel(2);
        email.Add("Send from", Inputs.Text(_bs, nameof(MessageSettings.SendFrom)));
        email.Add("Reply to", Inputs.Text(_bs, nameof(MessageSettings.ReplyTo)));
        email.Add("Sign-in", Inputs.Combo<string>(_bs, nameof(MessageSettings.EmailSignIn), [(MessageSettings.Microsoft365, MessageSettings.Microsoft365), (MessageSettings.Smtp, MessageSettings.Smtp)]), 2);
        email.Add("Microsoft 365 tenant ID", Inputs.Text(_bs, nameof(MessageSettings.TenantId)));
        email.Add("Microsoft 365 client ID", Inputs.Text(_bs, nameof(MessageSettings.ClientId)));
        email.Add("Microsoft 365 client secret", Secret(nameof(MessageSettings.ClientSecret)), 2);
        email.Add("SMTP server", Inputs.Text(_bs, nameof(MessageSettings.SmtpServer)));
        email.Add("SMTP port", Inputs.Text(_bs, nameof(MessageSettings.SmtpPort)));
        email.Add("SMTP user", Inputs.Text(_bs, nameof(MessageSettings.SmtpUser)));
        email.Add("SMTP password", Secret(nameof(MessageSettings.SmtpPassword)));
        var emailCard = new Card("Email");
        emailCard.AddTop(email);

        var test = new FieldPanel(2);
        test.Add("Test WhatsApp number", Inputs.Text(_bs, nameof(MessageSettings.TestNumber)));
        test.Add("Test email", Inputs.Text(_bs, nameof(MessageSettings.TestEmail)));
        _test.Checked = _settings.TestMode;
        _test.CheckedChanged += (_, _) => { _settings.TestMode = _test.Checked; _dirty = true; RaiseStatusChanged(); };
        var testCard = new Card("Test messages");
        testCard.AddTop(test, _test, _state);

        var content = new StackPanel();
        content.AddTop(whatsAppCard, emailCard, testCard);
        var scroll = new KryptonPanel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(content);
        Controls.Add(scroll);
        _bs.CurrentItemChanged += (_, _) => { _dirty = true; RaiseStatusChanged(); };
        _state.Text = string.Equals(ctx.Config.Database, "ArqamBase_be", StringComparison.OrdinalIgnoreCase)
            ? "Keys are stored encrypted for you on this computer."
            : $"This is the {ctx.Config.Database} database, so messages always go to the test number and email, even with test mode off. Keys are stored encrypted for you on this computer.";
        _dirty = false;
    }

    public override bool SupportsRecords => true;
    public override bool HasChanges => _dirty;
    public override string StatusText => _dirty ? "Unsaved changes" : "Message settings";
    public override IReadOnlyList<ScreenCommandGroup> Commands =>
        [new("Check", new ScreenCommand("Check WhatsApp", Icons.Chat, () => _ = CheckAsync()))];

    private KryptonTextBox Secret(string property)
    {
        var box = Inputs.Text(_bs, property);
        box.UseSystemPasswordChar = true;
        return box;
    }

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate();
        _bs.EndEdit();
    }

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException) { Program.ShowError("Could not save the settings.", ex); return false; }
        _dirty = false;
        RaiseStatusChanged();
        return true;
    }

    /// <summary>Asks the provider whether the WhatsApp account is connected. Sends no message.</summary>
    private async Task CheckAsync()
    {
        CommitPendingEdits();
        if (!_settings.WhatsAppReady) { Inform("Enter the instance and API key first."); return; }
        try { Inform($"WhatsApp: {await new MessageSender(_settings).WhatsAppStateAsync(CancellationToken.None)}"); }
        catch (MessageSendException ex) { Inform(ex.Message); }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Message settings", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
