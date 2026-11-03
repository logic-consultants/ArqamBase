using System.ComponentModel;
using System.Globalization;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Marks the staff's attendance for a date, with the time they came and left. Everyone starts present; press A, L or T
/// (or P) on a person or several selected, or Time in now / Time out now, then Save. Only an Admin marks staff attendance.
/// </summary>
public sealed class StaffAttendanceScreen : ScreenBase
{
    public sealed class Row : INotifyPropertyChanged
    {
        private string _status = "P";
        private string? _timeIn, _timeOut, _remarks;
        public int Sr { get; init; }
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Post { get; init; } = "";
        public string Kind { get; init; } = "";
        public string? Mobile { get; init; }
        public int AbsentThisMonth { get; init; }

        public string Status { get => _status; set => Set(ref _status, value, nameof(Status)); }
        /// <summary>"08:05"; empty when not recorded.</summary>
        public string? TimeIn { get => _timeIn; set => Set(ref _timeIn, value, nameof(TimeIn)); }
        public string? TimeOut { get => _timeOut; set => Set(ref _timeOut, value, nameof(TimeOut)); }
        public string? Remarks { get => _remarks; set => Set(ref _remarks, value, nameof(Remarks)); }

        private void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new(name));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static readonly (string Code, string Name)[] Statuses = [("P", "Present"), ("A", "Absent"), ("L", "Leave"), ("T", "Late")];
    private const string AllStaff = "All staff";

    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd/MM/yyyy" };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonLabel _banner = new()
    {
        Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), StateCommon = { ShortText = { Color1 = Theme.Muted } }
    };
    private readonly StatCard _total = new("Staff"), _present = new("Present"), _absent = new("Absent"), _leave = new("Leave"), _late = new("Late");
    private readonly AccessGrid _grid = new();
    private readonly bool _canEdit;
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private Dictionary<DateTime, string?> _holidays = [];
    private FilterBindingList<Row>? _list;
    private (DateTime Date, string? TakenBy, DateTime? TakenOn)? _taken;
    private DateTime? _shown;
    private bool _loading, _dirty;

    public StaffAttendanceScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Staff);
        _date.Value = DateTime.Today;
        _date.MaxDate = DateTime.Today;
        _kind.Items.AddRange([AllStaff, "Academic", "Non Academic"]);
        _kind.SelectedIndex = 0;

        var fields = new FieldPanel(4);
        fields.Add("Date", _date);
        fields.Add("Staff", _kind);
        var choose = new Card();
        choose.AddTop(fields);
        var stats = new EqualColumns { Padding = new Padding(0, Card.Gap, 0, 0) };
        stats.Controls.AddRange([_total, _present, _absent, _leave, _late]);

        BuildGrid();
        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0),
            Text = _canEdit ? "Everyone starts present. Press A (absent), L (leave), T (late) or P; Time in now and Time out now fill the time for the selected staff. Type times as 08:05. Then Save."
                            : "You can view staff attendance. Only an Admin can mark it.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, Card.Gap, 0, 0) };
        body.Controls.Add(_grid);
        body.Controls.Add(stats);
        body.Controls.Add(_banner);
        Controls.Add(body);
        Controls.Add(hint);
        Controls.Add(choose);

        _date.ValueChanged += (_, _) => ShowDay();
        _kind.SelectedIndexChanged += (_, _) => ApplyKind();

        _commands =
        [
            new("Mark",
                new ScreenCommand("Present", Icons.Check, () => Mark(r => r.Status = "P")),
                new ScreenCommand("Absent", Icons.Cross, () => Mark(r => r.Status = "A")),
                new ScreenCommand("Leave", Icons.Calendar, () => Mark(r => r.Status = "L")),
                new ScreenCommand("Late", Icons.Clock, () => Mark(r => r.Status = "T"))),
            new("Time",
                new ScreenCommand("Time in now", Icons.Clock, () => Mark(r => r.TimeIn = DateTime.Now.ToString("HH:mm"))),
                new ScreenCommand("Time out now", Icons.Clock, () => Mark(r => r.TimeOut = DateTime.Now.ToString("HH:mm")))),
            new("Date",
                new ScreenCommand("Previous day", Icons.Previous, () => MoveDay(-1)),
                new ScreenCommand("Next day", Icons.Next, () => MoveDay(1)),
                new ScreenCommand("Today", Icons.Calendar, () => _date.Value = DateTime.Today)),
        ];
        LoadHolidays();
        ShowDay();
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool SupportsRecords => true;
    public override string NewText => "All present";
    public override bool HasChanges => _dirty;

    public override string StatusText => _list is null ? "" :
        $"{_list.AllItems.Count:N0} staff · {Count("P") + Count("T"):N0} present · {Count("A"):N0} absent · {Count("L"):N0} leave · {Count("T"):N0} late";

    private int Count(string status) => _list?.AllItems.Count(r => r.Status == status) ?? 0;

    public override void NewRecord()
    {
        if (!_canEdit || _list is null) return;
        foreach (var r in _list.AllItems) r.Status = "P";
    }

    public override void Reload()
    {
        if (!ConfirmLeave()) return;
        _dirty = false;
        _shown = null;
        LoadHolidays();
        ShowDay();
    }

    public override void DeleteRecord()
    {
        if (_taken is null) { Inform("This day's staff attendance has not been saved, so there is nothing to delete."); return; }
        if (!_canEdit) return;
        var date = _date.Value.Date;
        if (KryptonMessageBox.Show($"Delete the staff attendance of {date:dd/MM/yyyy}?", "Delete staff attendance",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            db.StaffAttendance.Where(a => a.Date == date).ExecuteDelete();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not delete the staff attendance.", ex); return; }
        _dirty = false;
        _shown = null;
        ShowDay();
    }

    public override bool SaveChanges()
    {
        _grid.EndEdit();
        if (!_dirty) return true;
        if (!_canEdit || _list is null || _shown is not { } date) return false;
        var times = new Dictionary<Row, (TimeSpan? In, TimeSpan? Out)>();
        foreach (var r in _list.AllItems)
        {
            if (!TryTime(r.TimeIn, out var tin) || !TryTime(r.TimeOut, out var tout))
            {
                Inform($"The time of {r.Name} is not valid. Type it as 08:05 (24-hour), or leave it empty.");
                return false;
            }
            times[r] = (tin, tout);
        }
        if (HolidayText(date) is { } holiday &&
            KryptonMessageBox.Show($"{date:dddd dd/MM/yyyy} is {holiday}. Save staff attendance for it anyway?", "Save staff attendance",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return false;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var saved = db.StaffAttendance.Where(a => a.Date == date).ToDictionary(a => a.StaffId);
            var now = DateTime.Now;
            foreach (var r in _list.AllItems)
            {
                if (!saved.TryGetValue(r.Id, out var a)) db.StaffAttendance.Add(a = new StaffAttendance { Date = date, StaffId = r.Id });
                a.Status = r.Status;
                (a.TimeIn, a.TimeOut) = times[r];
                a.Remarks = string.IsNullOrWhiteSpace(r.Remarks) ? null : r.Remarks.Trim();
                a.TakenBy = Ctx.Session.UserName;
                a.TakenOn = now;
            }
            db.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save the staff attendance. Someone may have saved this day at the same time; click Refresh and try again.", ex);
            return false;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not save the staff attendance.", ex); return false; }
        _dirty = false;
        _shown = null;
        ShowDay();
        return true;
    }

    private static bool TryTime(string? text, out TimeSpan? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim().Replace('.', ':');
        if (!t.Contains(':') && t.Length is 3 or 4) t = t.PadLeft(4, '0').Insert(2, ":"); // 805 → 08:05
        if (!TimeSpan.TryParseExact(t, ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var v) || v >= TimeSpan.FromDays(1)) return false;
        time = v;
        return true;
    }

    private void LoadHolidays()
    {
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            _holidays = db.Calendar.AsNoTracking().Where(d => d.IsHoliday).ToList().ToDictionary(d => d.Date.Date, d => d.Reason);
        }
        catch (Microsoft.Data.SqlClient.SqlException) { _holidays = []; }
    }

    private string? HolidayText(DateTime date) =>
        _holidays.TryGetValue(date.Date, out var reason) ? $"a holiday{(string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason.Trim()})")}"
        : date.DayOfWeek == DayOfWeek.Sunday ? "a Sunday" : null;

    /// <summary>The staff working on the date (and anyone marked that day) with their marks; a day not taken starts with everyone present.</summary>
    private void ShowDay()
    {
        if (_loading) return;
        var date = _date.Value.Date;
        if (_shown == date) return;
        if (_dirty && _shown is not null && !ConfirmLeave())
        {
            _loading = true;
            _date.Value = _shown.Value;
            _loading = false;
            return;
        }
        _loading = true;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var marks = db.StaffAttendance.AsNoTracking().Where(a => a.Date == date).ToDictionary(a => a.StaffId);
            var ids = marks.Keys.ToList();
            var types = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => (Kind: t.Kind?.Trim() ?? "", Name: t.Name?.Trim() ?? ""));
            var staff = db.Staff.AsNoTracking().Where(s => s.LeftOn == null || s.LeftOn > date || ids.Contains(s.Id))
                .OrderBy(s => s.Name).ToList();
            var monthStart = new DateTime(date.Year, date.Month, 1);
            var absences = db.StaffAttendance.AsNoTracking().Where(a => a.Status == "A" && a.Date >= monthStart && a.Date < date)
                .GroupBy(a => a.StaffId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
            var sr = 0;
            var rows = staff.Select(s =>
            {
                var type = s.TypeId is int t ? types.GetValueOrDefault(t) : default;
                var mark = marks.GetValueOrDefault(s.Id);
                return new Row
                {
                    Sr = ++sr, Id = s.Id, Name = s.Name?.Trim() ?? "", Post = type.Name is { Length: > 0 } n ? n : s.Designation?.Trim() ?? "",
                    Kind = type.Kind ?? "", Mobile = s.CellNo, AbsentThisMonth = absences.GetValueOrDefault(s.Id),
                    Status = mark?.Status.Trim().ToUpperInvariant() ?? "P", TimeIn = mark?.TimeIn?.ToString("hh\\:mm"),
                    TimeOut = mark?.TimeOut?.ToString("hh\\:mm"), Remarks = mark?.Remarks,
                };
            }).ToList();
            foreach (var r in rows) r.PropertyChanged += (_, _) => Changed();
            _list = new FilterBindingList<Row>(rows);
            _grid.Bind(_list);
            var first = marks.Values.MaxBy(m => m.TakenOn);
            _taken = first is null ? null : (date, first.TakenBy, first.TakenOn);
            _shown = date;
            _dirty = false;
            ApplyKind();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not load the staff attendance. Has sql/attendance_roles_install.sql been run on this database?", ex);
        }
        finally { _loading = false; }
        ShowBanner();
        UpdateCounts();
    }

    /// <summary>Shows only academic or non-academic staff (all are still saved).</summary>
    private void ApplyKind()
    {
        if (_list is null) return;
        var kind = _kind.SelectedItem as string;
        if (kind == AllStaff) _list.ClearFilter(nameof(Row.Kind));
        else _list.SetFilter(nameof(Row.Kind), [kind ?? ""], null);
        UpdateCounts();
    }

    private void ShowBanner()
    {
        var parts = new List<string>();
        var warn = false;
        if (HolidayText(_date.Value) is { } holiday) { parts.Add($"{_date.Value:dddd dd/MM/yyyy} is {holiday}."); warn = true; }
        parts.Add(_taken is { } t ? $"Taken by {t.TakenBy} on {t.TakenOn:dd/MM/yyyy HH:mm}." : "Not taken yet: everyone is marked present until you save.");
        if (_dirty) parts.Add("Not saved.");
        _banner.Text = string.Join("  ", parts);
        _banner.StateCommon.ShortText.Color1 = warn ? Theme.Warning : _dirty ? Theme.Accent : Theme.Muted;
        _grid.ReadOnly = !_canEdit;
    }

    private void UpdateCounts()
    {
        _total.SetValue($"{_list?.AllItems.Count ?? 0:N0}");
        _present.SetValue($"{Count("P") + Count("T"):N0}", Theme.Good);
        _absent.SetValue($"{Count("A"):N0}", Count("A") > 0 ? Theme.Error : null);
        _leave.SetValue($"{Count("L"):N0}");
        _late.SetValue($"{Count("T"):N0}", Count("T") > 0 ? Theme.Warning : null);
        RaiseStatusChanged();
    }

    private void Changed()
    {
        if (_loading) return;
        _dirty = true;
        ShowBanner();
        UpdateCounts();
        _grid.Invalidate();
    }

    private List<Row> SelectedRows()
    {
        var rows = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => _grid.Rows[c.RowIndex].DataBoundItem).OfType<Row>().Distinct().ToList();
        if (rows.Count == 0 && _grid.CurrentRow?.DataBoundItem is Row current) rows.Add(current);
        return rows;
    }

    private void Mark(Action<Row> change)
    {
        if (!_canEdit) return;
        _grid.EndEdit();
        foreach (var r in SelectedRows()) change(r);
    }

    private void MoveDay(int days)
    {
        var d = _date.Value.Date.AddDays(days);
        if (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(days);
        if (d <= _date.MaxDate) _date.Value = d;
    }

    private void BuildGrid()
    {
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.AddText(nameof(Row.Sr), "Sr#", 45, readOnly: true);
        _grid.AddText(nameof(Row.Id), "ID", 50, readOnly: true);
        _grid.AddText(nameof(Row.Name), "Name", 200, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.Post), "Post", 140, readOnly: true);
        var status = _grid.AddCombo(nameof(Row.Status), "Attendance", Statuses.Select(s => (s.Code, s.Name)), 110);
        status.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        _grid.AddText(nameof(Row.TimeIn), "Time in", 75);
        _grid.AddText(nameof(Row.TimeOut), "Time out", 75);
        _grid.AddText(nameof(Row.AbsentThisMonth), "Absent this month", 120, readOnly: true).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Remarks), "Remarks", 200, fill: true);
        _grid.AddText(nameof(Row.Mobile), "Mobile", 115, readOnly: true);
        _grid.FormatNumbers(nameof(Row.Mobile), NumberFormat.Phone);
        _grid.CanEditCell = (_, column) => _canEdit && _grid.Columns[column].DataPropertyName is nameof(Row.Remarks) or nameof(Row.Status)
            or nameof(Row.TimeIn) or nameof(Row.TimeOut);

        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(Row.Status) || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            e.CellStyle!.ForeColor = row.Status switch { "A" => Theme.Error, "T" => Theme.Warning, "L" => Theme.Accent, _ => Theme.Good };
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        };
        // P / A / L / T mark the selected staff, except while typing a time or remarks.
        _grid.KeyDown += (_, e) =>
        {
            if (_grid.IsCurrentCellInEditMode || _grid.CurrentCell?.OwningColumn?.DataPropertyName is nameof(Row.Remarks) or nameof(Row.TimeIn) or nameof(Row.TimeOut)) return;
            var code = e.KeyCode switch { Keys.P => "P", Keys.A => "A", Keys.L => "L", Keys.T => "T", _ => null };
            if (code is null || e.Modifiers != Keys.None) return;
            Mark(r => r.Status = code);
            e.Handled = e.SuppressKeyPress = true;
            if (_grid.SelectedCells.Count <= 1 && _grid.CurrentCell is { } cell && cell.RowIndex + 1 < _grid.Rows.Count)
                _grid.CurrentCell = _grid.Rows[cell.RowIndex + 1].Cells[cell.ColumnIndex];
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (!_canEdit || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(Row.Status) || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            var i = Array.FindIndex(Statuses, s => s.Code == row.Status);
            row.Status = Statuses[(i + 1) % Statuses.Length].Code;
        };
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Staff attendance", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
