using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace ArqamBase.App.Screens;

/// <summary>
/// Sends attendance, fee reminders, fee vouchers and salary vouchers by WhatsApp and email. Pick what to send, check who is
/// ticked, and press Send. Everything is logged, and anyone already sent the same message starts unticked.
/// </summary>
public sealed class MessagesScreen : ScreenBase
{
    private const int WhatsAppGapMs = 1500;
    private static readonly string[] Channels = ["WhatsApp", "Email", "WhatsApp and email", "SMS"];

    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd/MM/yyyy" };
    private readonly ComboBox _month = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _channel = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonLabel _banner = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly StatCard _people = new("People"), _ticked = new("Ticked"), _noContact = new("No number or email"), _sent = new("Sent already");
    private readonly AccessGrid _grid = new() { AllowUserToAddRows = false, AllowUserToDeleteRows = false };
    private readonly KryptonTextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private readonly IMessageSender? _sender;
    private FilterBindingList<MessageRow>? _list;
    private CancellationTokenSource? _stop;
    private bool _loading;
    private int _load;

    /// <param name="sender">Used in place of the real WhatsApp and email services, for testing.</param>
    public MessagesScreen(ScreenContext ctx, IMessageSender? sender = null) : base(ctx)
    {
        _sender = sender;
        _loading = true;
        _date.Value = DateTime.Today;
        _date.MaxDate = DateTime.Today;
        _kind.DisplayMember = nameof(MessageType.Title);
        _kind.Items.AddRange(MessageType.All.Where(t => t.InBulk).Cast<object>().ToArray());
        _kind.SelectedIndex = 0;
        _show.Items.AddRange(["Absent, leave and late", "Everyone marked"]);
        _show.SelectedIndex = 0;
        _channel.Items.AddRange(Channels.Cast<object>().ToArray());
        _channel.SelectedIndex = 0;

        var fields = new FieldPanel(5);
        fields.Add("What to send", _kind);
        fields.Add("Date", _date);
        fields.Add("Salary month", _month);
        fields.Add("Show", _show);
        fields.Add("Send by", _channel);
        var choose = new Card();
        choose.AddTop(fields);
        var stats = new EqualColumns { Padding = new Padding(0, Card.Gap, 0, 0) };
        stats.Controls.AddRange([_people, _ticked, _noContact, _sent]);

        BuildGrid();
        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_preview);
        WhenSized(split, () => split.SplitterDistance = Math.Max(300, split.Width - 340));
        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, Card.Gap, 0, 0) };
        body.Controls.Add(split);
        body.Controls.Add(stats);
        body.Controls.Add(_banner);
        Controls.Add(body);
        Controls.Add(choose);

        _kind.SelectedIndexChanged += (_, _) => ReloadRows();
        _date.ValueChanged += (_, _) => ReloadRows();
        _month.SelectedIndexChanged += (_, _) => ReloadRows();
        _show.SelectedIndexChanged += (_, _) => ReloadRows();
        _channel.SelectedIndexChanged += (_, _) => { ShowCounts(); ShowPreview(); };

        _commands =
        [
            new("Send",
                new ScreenCommand("Send ticked", Icons.Send, () => _ = SendAsync()),
                new ScreenCommand("Stop sending", Icons.Cross, () => _stop?.Cancel())),
            new("Tick",
                new ScreenCommand("Tick all", Icons.Check, () => Tick(r => r.Number is not null || r.Email is not null)),
                new ScreenCommand("Tick none", Icons.Clear, () => Tick(_ => false))),
            new("Message",
                new ScreenCommand("See voucher", Icons.Print, ShowVoucher),
                new ScreenCommand("Templates", Icons.Document, () => Ctx.OpenScreen("messagetemplates")),
                new ScreenCommand("Message settings", Icons.Settings, () => Ctx.OpenScreen("messagesettings"))),
        ];
        _loading = false;
        HandleCreated += (_, _) => { FillMonths(); ReloadRows(); };
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;

    public override string StatusText => _list is null ? "Loading…" : $"{MessageType.Of(Kind).Title}: {_list.AllItems.Count:N0} people, {Ticked.Count:N0} ticked";

    private MessageKind Kind => ((MessageType)_kind.SelectedItem!).Kind;
    private bool ByWhatsApp => _channel.SelectedIndex is 0 or 2;
    private bool ByEmail => _channel.SelectedIndex is 1 or 2;
    private bool BySms => _channel.SelectedIndex == 3;

    private List<MessageChannel> Chosen =>
        [.. new[] { (ByWhatsApp, MessageChannel.WhatsApp), (ByEmail, MessageChannel.Email), (BySms, MessageChannel.Sms) }.Where(c => c.Item1).Select(c => c.Item2)];

    private static bool Reaches(MessageRow r, MessageChannel c) => c == MessageChannel.Email ? r.Email is not null : r.Number is not null;
    private List<MessageRow> Ticked => _list?.AllItems.Where(r => r.Send).ToList() ?? [];

    public override void Reload() => ReloadRows();

    private void FillMonths()
    {
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var periods = db.SalaryHeads.AsNoTracking().OrderByDescending(h => h.Period).Select(h => h.Period).ToList();
            _loading = true;
            _month.DisplayMember = nameof(Inputs.Item<string>.Text);
            _month.Items.AddRange(periods.Select(p => (object)new Inputs.Item<string>(p, DateTime.ParseExact(p, "yyyyMM", null).ToString("MMMM yyyy"))).ToArray());
            if (_month.Items.Count > 0) _month.SelectedIndex = 0;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the salary months.", ex); }
        finally { _loading = false; }
    }

    private async void ReloadRows()
    {
        if (_loading || _stop is not null) return;
        var kind = Kind;
        _date.Enabled = _show.Enabled = kind is MessageKind.StudentAttendance or MessageKind.StaffAttendance;
        _month.Enabled = kind == MessageKind.SalaryVoucher;
        var (date, month, everyone, load) = (_date.Value.Date, (_month.SelectedItem as Inputs.Item<string>)?.Value, _show.SelectedIndex == 1, ++_load);
        _banner.Text = "Loading…";
        try
        {
            var rows = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                return MessageData.Load(kind, db, Ctx.Config, date, month, everyone);
            });
            if (IsDisposed || load != _load) return;
            foreach (var r in rows) r.PropertyChanged += (_, _) => { ShowCounts(); _grid.Invalidate(); };
            _list = new FilterBindingList<MessageRow>(rows);
            _grid.Bind(_list);
            ShowCounts();
            ShowPreview();
            ShowBanner();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the messages. Has sql/messages_install.sql been run on this database?", ex);
        }
    }

    /// <summary>Test messages go to the test number and address on any database but the live one.</summary>
    private bool MustTest => MessageOut.MustTest(Ctx.Config);

    /// <summary>Opens on a type of message, e.g. fee vouchers from the Periodic fee screen.</summary>
    public override void Navigate(object target)
    {
        if (target is not MessageKind kind) return;
        var index = _kind.Items.Cast<MessageType>().ToList().FindIndex(t => t.Kind == kind);
        if (index >= 0 && index != _kind.SelectedIndex) _kind.SelectedIndex = index;
    }

    private void ShowBanner()
    {
        var settings = MessageSettings.Load();
        var test = settings.TestMode || MustTest;
        _banner.Text = test ? $"TEST MODE: messages go to {settings.TestNumber ?? "(no test number)"} and {settings.TestEmail ?? "(no test email)"}, not to the people listed."
            : "Messages go to the people listed.";
        _banner.StateCommon.ShortText.Color1 = test ? Theme.Warning : Theme.Muted;
    }

    private void ShowCounts()
    {
        var all = _list?.AllItems ?? [];
        _people.SetValue($"{all.Count:N0}");
        _ticked.SetValue($"{all.Count(r => r.Send):N0}", Theme.Good);
        var chosen = Chosen;
        var missing = all.Count(r => !chosen.Any(c => Reaches(r, c)));
        _noContact.SetValue($"{missing:N0}", missing > 0 ? Theme.Warning : null);
        _sent.SetValue($"{all.Count(r => r.LastSent.Length > 0):N0}");
        RaiseStatusChanged();
    }

    private void ShowPreview()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MessageRow r) { _preview.Text = ""; return; }
        var text = $"{(ByWhatsApp ? r.Body : MessageText.ForEmail(r.Body))}{(r.FileName is null || BySms ? "" : $"\n\nAttached: {r.FileName}")}";
        if (ByEmail) text = $"Subject: {r.Subject}\n\n{text}";
        _preview.Text = text.ReplaceLineEndings("\r\n");
    }

    private void Tick(Func<MessageRow, bool> tick)
    {
        _grid.EndEdit();
        if (_list is null) return;
        foreach (var r in _list) r.Send = tick(r);
    }

    private void ShowVoucher()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MessageRow { Document: { } document } row)
        {
            Inform("Fee vouchers and salary vouchers have a file to see. Choose one of those and a person.");
            return;
        }
        Shell.PrintPreviewForm.Show(this, document, row.FileName ?? "Voucher", Path.GetFileNameWithoutExtension(row.FileName ?? "Voucher"));
    }

    private async Task SendAsync()
    {
        _grid.EndEdit();
        if (_stop is not null) return;
        var settings = MessageSettings.Load();
        var test = settings.TestMode || MustTest;
        var chosen = Chosen;
        if (_sender is null && MessageOut.Problem(Ctx.Config, settings, chosen) is { } problem) { Inform(problem); return; }
        var work = Ticked.SelectMany(r => chosen.Where(c => Reaches(r, c)).Select(c => (Row: r, Channel: c))).ToList();
        if (work.Count == 0) { Inform("Nobody ticked has a number or email for this."); return; }
        var question = test ? $"Send {work.Count:N0} test messages to your test number and email?" : $"Send {work.Count:N0} messages to the people ticked? This cannot be undone.";
        if (KryptonMessageBox.Show(this, question, "Send messages", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes) return;

        var sender = _sender ?? new MessageSender(settings);
        using var stop = _stop = new CancellationTokenSource();
        var (done, failed) = (0, 0);
        try
        {
            foreach (var group in work.GroupBy(w => w.Row))
            {
                var row = group.Key;
                var file = row.Document is null || BySms ? null : await Task.Run(() => new MessageFile(row.FileName!, row.Document().GeneratePdf()), stop.Token);
                var results = new List<string>();
                foreach (var (_, channel) in group)
                {
                    _banner.Text = $"Sending {++done:N0} of {work.Count:N0}: {row.Name}…";
                    var to = MessageOut.To(row, channel, test, settings) ?? "";
                    var (status, detail) = await MessageOut.SendOneAsync(Ctx.Config, Ctx.Session, sender, row, channel, to, row.Subject, row.Body, file, test, stop.Token);
                    results.Add($"{MessageOut.Name(channel)} {status.ToLowerInvariant()}");
                    if (status == "Failed") { failed++; results[^1] += $": {detail}"; }
                    if (channel != MessageChannel.Email) await Task.Delay(_sender is null ? WhatsAppGapMs : 0, stop.Token);
                }
                row.Result = string.Join("; ", results);
                row.Send = results.Any(r => r.Contains("failed"));
                if (test == false) row.LastSent = results.Any(r => r.EndsWith(" sent")) ? DateTime.Now.ToString("dd/MM/yyyy HH:mm") : row.LastSent;
            }
            Inform(failed == 0 ? $"Done. {done:N0} messages sent." : $"Done. {done - failed:N0} sent and {failed:N0} failed. Those that failed are still ticked.");
        }
        catch (OperationCanceledException) { Inform($"Stopped after {done:N0} of {work.Count:N0} messages."); }
        finally
        {
            _stop = null;
            ShowBanner();
            ShowCounts();
        }
    }

    private void BuildGrid()
    {
        _grid.AddCheck(nameof(MessageRow.Send), "Send", 50);
        _grid.AddText(nameof(MessageRow.Id), "ID", 55, readOnly: true);
        _grid.AddText(nameof(MessageRow.Name), "Name", 170, readOnly: true, fill: true);
        _grid.AddText(nameof(MessageRow.Detail), "Class or post", 120, readOnly: true);
        _grid.AddText(nameof(MessageRow.About), "About", 110, readOnly: true);
        _grid.AddText(nameof(MessageRow.NumberText), "WhatsApp", 110, readOnly: true);
        _grid.AddText(nameof(MessageRow.Email), "Email", 140, readOnly: true);
        _grid.AddText(nameof(MessageRow.LastSent), "Last sent", 110, readOnly: true);
        _grid.AddText(nameof(MessageRow.Result), "Result", 160, readOnly: true);
        _grid.SelectionChanged += (_, _) => ShowPreview();
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Messages", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _stop?.Cancel();
        base.Dispose(disposing);
    }
}
