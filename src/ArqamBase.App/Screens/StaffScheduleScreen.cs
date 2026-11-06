using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// The staff's check-in and check-out times for a period, and the minutes allowed before someone counts as late or as
/// leaving early. Staff attendance uses the schedule covering the date. Periods may not overlap.
/// </summary>
public sealed class StaffScheduleScreen : ScreenBase
{
    public sealed class Row
    {
        public StaffSchedule? Saved { get; init; }
        public string Starting { get; init; } = "";
        public string Name { get; set; } = "";
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string LateMinutes { get; set; } = "0";
        public string EarlyMinutes { get; set; } = "0";

        public string Key => string.Join('\u0001', Name, CheckIn, CheckOut, From, To, LateMinutes, EarlyMinutes);
        public bool Changed => Saved is null || Key != Starting;
        public string State => From is null || To is null ? "" : From > DateTime.Today ? "Upcoming" : To < DateTime.Today ? "Past" : "Current";
    }

    private readonly AccessGrid _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false,
    };
    private readonly BindingSource _bs = new() { DataSource = typeof(Row) };
    private readonly KryptonPanel _details = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 8, 0) };
    private readonly List<Row> _rows = [];
    private readonly List<StaffSchedule> _removed = [];
    private readonly ComboBox _checkIn, _checkOut;
    private ArqamDbContext? _db;
    private Row? _current;
    private bool _loading;

    public StaffScheduleScreen(ScreenContext ctx) : base(ctx)
    {
        _grid.AddText(nameof(Row.Name), "Schedule", 150, fill: true);
        _grid.AddText(nameof(Row.From), "From", 90, format: "dd-MMM-yyyy");
        _grid.AddText(nameof(Row.To), "To", 90, format: "dd-MMM-yyyy");
        _grid.AddText(nameof(Row.State), "Status", 70);
        _grid.SelectionChanged += (_, _) => { if (!_loading) ShowCurrent(); };

        var fields = new FieldPanel(2);
        fields.Add("Schedule name", Inputs.Text(_bs, nameof(Row.Name)), 2);
        _checkIn = fields.Add("Staff check-in time", Inputs.Combo(_bs, nameof(Row.CheckIn), TimeItems(StaffTimes.Choices)));
        _checkOut = fields.Add("Staff check-out time", Inputs.Combo(_bs, nameof(Row.CheckOut), TimeItems(StaffTimes.Choices)));
        fields.Add("Effective from", Inputs.Date(_bs, nameof(Row.From)));
        fields.Add("Effective to", Inputs.Date(_bs, nameof(Row.To)));
        fields.Add("Late policy (minutes allowed)", Inputs.Text(_bs, nameof(Row.LateMinutes)));
        fields.Add("Early leave (minutes allowed)", Inputs.Text(_bs, nameof(Row.EarlyMinutes)));
        var card = new Card("Schedule");
        card.AddTop(fields, new KryptonLabel
        {
            Text = "Late means after check-in plus the late minutes; early means before check-out less the early minutes.",
            AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Muted } },
        });
        var content = new StackPanel();
        content.AddTop(card);
        _details.Controls.Add(content);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_details);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width * 2 / 5, 380, 520));
        Controls.Add(split);
        HandleCreated += (_, _) => LoadRows();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New schedule";
    public override bool HasChanges => !_loading && (_removed.Count > 0 || _rows.Any(r => r.Changed));

    public override string StatusText
    {
        get
        {
            var current = _rows.FirstOrDefault(r => r.State == "Current");
            return $"{_rows.Count:N0} schedule{(_rows.Count == 1 ? "" : "s")}  ·  {(current is null ? "no schedule covers today" : $"current: {current.Name.Trim()}")}{(HasChanges ? "  ·  unsaved changes" : "")}";
        }
    }

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
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        _rows.Clear();
        _removed.Clear();
        try
        {
            _rows.AddRange(db.StaffSchedules.OrderByDescending(s => s.From).ToList().Select(ToRow));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not load the schedules. Has sql/staff_schedule_install.sql been run on this database?", ex);
        }
        // A saved time may not be on the 5-minute list, and the drop-down must still show it.
        var times = TimeItems(StaffTimes.Choices.Concat(_rows.SelectMany(r => new[] { r.CheckIn!, r.CheckOut! })).Distinct().Order(StringComparer.Ordinal)).ToList();
        Inputs.SetItems(_checkIn, times);
        Inputs.SetItems(_checkOut, times);
        Bind(null);
    }

    private static IEnumerable<(string? Value, string Text)> TimeItems(IEnumerable<string> times) => times.Select(t => ((string?)t, t));

    private static Row ToRow(StaffSchedule s)
    {
        Row Make(string starting, StaffSchedule? saved) => new()
        {
            Saved = saved, Starting = starting, Name = s.Name, CheckIn = StaffTimes.Text(s.CheckIn), CheckOut = StaffTimes.Text(s.CheckOut),
            From = s.From, To = s.To, LateMinutes = $"{s.LateMinutes}", EarlyMinutes = $"{s.EarlyMinutes}",
        };
        return Make(Make("", null).Key, s);
    }

    private void Bind(Row? select)
    {
        _loading = true;
        _current = null;
        try
        {
            _grid.Bind(new FilterBindingList<Row>(_rows));
            foreach (DataGridViewRow row in _grid.Rows)
                if (ReferenceEquals(row.DataBoundItem, select)) _grid.CurrentCell = row.Cells[0];
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
        RaiseStatusChanged();
    }

    public override void NewRecord()
    {
        CommitPendingEdits();
        var from = _rows.Max(r => r.To)?.AddDays(1) ?? DateTime.Today;
        var schedule = _rows.FirstOrDefault(r => r.State == "Current") ?? _rows.FirstOrDefault();
        var row = new Row { From = from, To = from, CheckIn = schedule?.CheckIn, CheckOut = schedule?.CheckOut };
        _rows.Insert(0, row);
        Bind(row);
    }

    /// <summary>What is wrong with a schedule, or null.</summary>
    private string? Problem(Row r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Give the schedule a name.";
        var name = r.Name.Trim();
        if (!StaffTimes.TryParse(r.CheckIn, out var checkIn) || checkIn is null) return $"{name}: choose the check-in time.";
        if (!StaffTimes.TryParse(r.CheckOut, out var checkOut) || checkOut is null) return $"{name}: choose the check-out time.";
        if (checkOut <= checkIn) return $"{name}: check-out must be after check-in.";
        if (r.From is not { } from || r.To is not { } to) return $"{name}: choose both effective dates.";
        if (to < from) return $"{name}: Effective to ({to:dd-MMM-yyyy}) is before Effective from ({from:dd-MMM-yyyy}).";
        if (!TryMinutes(r.LateMinutes, out _)) return $"{name}: type the late policy in minutes, such as 10.";
        if (!TryMinutes(r.EarlyMinutes, out _)) return $"{name}: type the early leave in minutes, such as 0.";
        return _rows.FirstOrDefault(o => o != r && o.From <= to && o.To >= from) is { } other
            ? $"{name} overlaps {other.Name.Trim()}. Only one schedule can apply on a date." : null;
    }

    /// <summary>Whole minutes; 10:00 (minutes and seconds) is read as 10.</summary>
    private static bool TryMinutes(string? text, out short minutes)
    {
        var t = text?.Trim() ?? "";
        var colon = t.IndexOf(':');
        if (colon >= 0 && t[(colon + 1)..].Trim('0').Length == 0) t = t[..colon];
        if (t.Length == 0) t = "0";
        return short.TryParse(t, out minutes) && minutes is >= 0 and <= 240;
    }

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        if (_db is null || !HasChanges) return true;
        if (_rows.Select(Problem).FirstOrDefault(p => p is not null) is { } problem) { Inform(problem); return false; }
        foreach (var r in _rows.Where(r => r.Changed))
        {
            var s = r.Saved ?? new StaffSchedule();
            StaffTimes.TryParse(r.CheckIn, out var checkIn);
            StaffTimes.TryParse(r.CheckOut, out var checkOut);
            TryMinutes(r.LateMinutes, out var late);
            TryMinutes(r.EarlyMinutes, out var early);
            s.Name = r.Name.Trim();
            (s.CheckIn, s.CheckOut) = (checkIn!.Value, checkOut!.Value);
            (s.From, s.To) = (r.From!.Value, r.To!.Value);
            (s.LateMinutes, s.EarlyMinutes) = (late, early);
            s.UpdatedBy = Ctx.Session.UserName;
            s.UpdatedOn = DateTime.Now;
            if (r.Saved is null) _db.StaffSchedules.Add(s);
        }
        try
        {
            _db.StaffSchedules.RemoveRange(_removed);
            _db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save the schedule.", ex); return false; }
        LoadRows();
        return true;
    }

    public override void DeleteRecord()
    {
        if (_current is not { } row) return;
        if (KryptonMessageBox.Show(this, $"Delete the schedule {row.Name.Trim()}? Attendance already saved is not changed.", "Staff schedule",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        if (row.Saved is not null) _removed.Add(row.Saved);
        _rows.Remove(row);
        Bind(null);
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Staff schedule", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}
