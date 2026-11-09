using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>The wording of each type of message, changed by an Admin. A type with no saved wording uses the app's own.</summary>
public sealed class MessageTemplatesScreen : ScreenBase
{
    public sealed class Row
    {
        public required MessageType Type { get; init; }
        public MessageTemplate? Saved { get; init; }
        public string Title => Type.Title;
        public string Uses => Saved is null ? "App's wording" : "Changed";
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public string Starting { get; init; } = "";
        public string Key => Subject + '\u0001' + Body;
        public bool Changed => Key != Starting;
    }

    private readonly AccessGrid _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false,
    };
    private readonly BindingSource _bs = new() { DataSource = typeof(Row) };
    private readonly KryptonPanel _details = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 8, 0) };
    private readonly KryptonLabel _fields = new() { AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly List<Row> _rows = [];
    private Row? _current;
    private bool _loading;

    public MessageTemplatesScreen(ScreenContext ctx) : base(ctx)
    {
        _grid.AddText(nameof(Row.Title), "Message", 170, fill: true);
        _grid.AddText(nameof(Row.Uses), "Wording", 100);
        _grid.SelectionChanged += (_, _) => { if (!_loading) ShowCurrent(); };

        var fields = new FieldPanel(1);
        fields.Add("Email subject", Inputs.Text(_bs, nameof(Row.Subject)));
        var body = Inputs.Text(_bs, nameof(Row.Body), multiline: true);
        body.Height = 300;
        body.ScrollBars = ScrollBars.Vertical;
        body.AcceptsReturn = true;
        fields.Add("Message (put * around words for bold on WhatsApp)", body);
        var card = new Card("Wording");
        card.AddTop(fields, _fields);
        var content = new StackPanel();
        content.AddTop(card);
        _details.Controls.Add(content);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_details);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width / 4, 280, 360));
        Controls.Add(split);
        HandleCreated += (_, _) => LoadRows();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New message";
    public override bool HasChanges => !_loading && _rows.Any(r => r.Changed);
    public override string StatusText => $"{_rows.Count(r => r.Saved is not null)} of {_rows.Count} messages changed{(HasChanges ? "  ·  unsaved changes" : "")}";

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate();
        _bs.EndEdit();
    }

    public override void Reload()
    {
        if (ConfirmLeave()) LoadRows();
    }

    private void LoadRows()
    {
        var keep = _current?.Type.Kind;
        _rows.Clear();
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var saved = db.MessageTemplates.AsNoTracking().ToDictionary(t => t.Key);
            foreach (var type in MessageType.All)
            {
                var s = saved.GetValueOrDefault(type.Key);
                var (subject, text) = (s?.Subject ?? type.Subject, (s?.Body ?? type.Body).ReplaceLineEndings("\r\n"));
                _rows.Add(new Row { Type = type, Saved = s, Subject = subject, Body = text, Starting = subject + '\u0001' + text });
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not load the templates. Has sql/messages_install.sql been run on this database?", ex);
        }
        _loading = true;
        _current = null;
        try
        {
            _grid.Bind(new FilterBindingList<Row>(_rows));
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.DataBoundItem is Row r && r.Type.Kind == keep) _grid.CurrentCell = row.Cells[0];
        }
        finally { _loading = false; }
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (_current is not null) CommitPendingEdits();
        _current = _grid.CurrentRow?.DataBoundItem as Row;
        _bs.DataSource = _current ?? (object)typeof(Row);
        _details.Visible = _current is not null;
        _fields.Text = _current is null ? "" : $"These fields fill in by themselves: {_current.Type.Fields}";
        RaiseStatusChanged();
    }

    public override void NewRecord() => Inform("Each type of message has one wording. Choose it on the left and change it.");

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        var changed = _rows.Where(r => r.Changed).ToList();
        if (changed.Count == 0) return true;
        if (changed.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Body)) is { } empty)
        {
            Inform($"{empty.Title} needs a subject and a message.");
            return false;
        }
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var saved = db.MessageTemplates.ToDictionary(t => t.Key);
            foreach (var r in changed)
            {
                if (!saved.TryGetValue(r.Type.Key, out var t)) db.MessageTemplates.Add(t = new MessageTemplate { Key = r.Type.Key });
                (t.Subject, t.Body) = (r.Subject.Trim(), r.Body.Trim().ReplaceLineEndings("\n"));
                t.UpdatedBy = Ctx.Session.UserName;
                t.UpdatedOn = DateTime.Now;
            }
            db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save the wording.", ex); return false; }
        LoadRows();
        return true;
    }

    /// <summary>Goes back to the app's own wording.</summary>
    public override void DeleteRecord()
    {
        if (_current is not { } row) return;
        if (row.Saved is null) { Inform($"{row.Title} already uses the app's wording."); return; }
        if (KryptonMessageBox.Show(this, $"Go back to the app's wording for {row.Title}?", "Message templates",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            db.MessageTemplates.Where(t => t.Key == row.Type.Key).ExecuteDelete();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not remove the wording.", ex); return; }
        row.Subject = row.Saved.Subject;
        row.Body = row.Saved.Body;
        LoadRows();
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Message templates", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
