using ArqamBase.App.Core;
using ArqamBase.App.Screens;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>
/// Sends one message, with its file if any (a receipt or fee voucher), by WhatsApp, email or SMS (text only). The wording comes from
/// Message templates and can be changed before sending. The result is shown here, and the message is logged.
/// </summary>
public sealed class SendMessageForm : KryptonForm
{
    private readonly ScreenContext _ctx;
    private readonly MessageRow _row;
    private readonly KryptonCheckBox _byWhatsApp = new() { AutoSize = true }, _byEmail = new() { AutoSize = true }, _bySms = new() { AutoSize = true };
    private readonly KryptonTextBox _subject = new() { Width = 520 };
    private readonly KryptonTextBox _body = new() { Width = 520, Height = 190, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly KryptonLabel _status = new() { Text = "", AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly KryptonButton _send = new() { Text = "Send", Width = 100 }, _close = new() { Text = "Close", Width = 100, DialogResult = DialogResult.Cancel };

    public SendMessageForm(ScreenContext ctx, MessageRow row, MessageChannel channel)
    {
        _ctx = ctx;
        _row = row;
        var settings = MessageSettings.Load();
        var test = MessageOut.IsTest(ctx.Config, settings);
        Text = $"Send message: {row.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => Theme.FitButtons(this);

        _byWhatsApp.Text = row.Number is null ? "WhatsApp (no number saved)" : $"WhatsApp to +{row.Number}";
        _byEmail.Text = row.Email is null ? "Email (no email saved)" : $"Email to {row.Email}";
        _bySms.Text = row.Number is null ? "SMS (no number saved)" : $"SMS to +{row.Number} (text only, no file)";
        _byWhatsApp.Enabled = _bySms.Enabled = row.Number is not null || test;
        _byEmail.Enabled = row.Email is not null || test;
        _byWhatsApp.Checked = channel == MessageChannel.WhatsApp && _byWhatsApp.Enabled;
        _byEmail.Checked = channel == MessageChannel.Email && _byEmail.Enabled;
        _bySms.Checked = channel == MessageChannel.Sms && _bySms.Enabled;
        _subject.Text = row.Subject;
        _body.Text = row.Body.ReplaceLineEndings("\r\n");

        var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        if (test)
            t.Controls.Add(new KryptonLabel
            {
                AutoSize = true, MaximumSize = new Size(520, 0), StateCommon = { ShortText = { Color1 = Theme.Warning } },
                Text = $"TEST MODE: this goes to {settings.TestNumber ?? "(no test number)"} and {settings.TestEmail ?? "(no test email)"}, not to the parent.",
            });
        t.Controls.Add(Caption("Send by"));
        t.Controls.Add(_byWhatsApp);
        t.Controls.Add(_byEmail);
        t.Controls.Add(_bySms);
        t.Controls.Add(Caption("Email subject"));
        t.Controls.Add(_subject);
        t.Controls.Add(Caption("Message (put * around words for bold on WhatsApp)"));
        t.Controls.Add(_body);
        if (row.FileName is not null) t.Controls.Add(new KryptonLabel { AutoSize = true, Text = $"Attached: {row.FileName}" });
        t.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_send);
        t.Controls.Add(buttons);
        Controls.Add(t);
        ClientSize = t.GetPreferredSize(Size.Empty) + new Size(0, 20);
        CancelButton = _close;
        _send.Click += async (_, _) => await SendAsync();
    }

    private static KryptonLabel Caption(string text) =>
        new() { Text = text, AutoSize = true, LabelStyle = LabelStyle.BoldControl, Padding = new Padding(0, 8, 0, 0) };

    private async Task SendAsync()
    {
        var channels = new[] { (_byWhatsApp, MessageChannel.WhatsApp), (_byEmail, MessageChannel.Email), (_bySms, MessageChannel.Sms) }
            .Where(c => c.Item1.Checked).Select(c => c.Item2).ToList();
        if (channels.Count == 0) { _status.Text = "Tick WhatsApp, email or SMS."; return; }
        if (MessageOut.Problem(_ctx.Config, MessageSettings.Load(), channels) is { } problem) { _status.Text = problem; return; }
        if (_body.Text.Trim().Length == 0) { _status.Text = "Write the message."; return; }
        _send.Enabled = false;
        _status.Text = "Sending…";
        try
        {
            var (ok, result) = await MessageOut.SendAsync(_ctx.Config, _ctx.Session, _row, channels, _subject.Text.Trim(), _body.Text.ReplaceLineEndings("\n").Trim());
            if (IsDisposed) return;
            _status.Text = result;
            _status.StateCommon.ShortText.Color1 = ok ? Theme.Good : Theme.Error;
            _send.Enabled = !ok;
            if (ok) _close.Text = "Done";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
        {
            if (IsDisposed) return;
            _status.Text = "Could not send: " + ex.GetBaseException().Message;
            _send.Enabled = true;
        }
    }

    /// <summary>Shows the dialog for a message built on a background thread (it reads the database), with a wait cursor meanwhile.</summary>
    public static async Task ShowAsync(IWin32Window owner, ScreenContext ctx, Func<MessageRow> build, MessageChannel channel)
    {
        MessageRow row;
        var old = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try { row = await Task.Run(build); }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not prepare the message. Has sql/messages_install.sql been run on this database?", ex);
            return;
        }
        finally { Cursor.Current = old; }
        using var form = new SendMessageForm(ctx, row, channel);
        form.ShowDialog(owner);
    }
}
