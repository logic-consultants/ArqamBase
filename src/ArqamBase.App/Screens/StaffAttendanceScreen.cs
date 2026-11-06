using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Marks the staff's attendance for a date, with the time they came and left. Everyone starts present at the current schedule's
/// times, so only those who differ need changing; a time past the late policy marks the person late. Directors are always
/// present at the schedule's times. Only an Admin marks staff attendance.
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
        public string Campus { get; init; } = "";
        public string? Mobile { get; init; }
        /// <summary>Always present at the schedule's times, with nothing to mark.</summary>
        public bool IsDirector { get; init; }
        public StaffSchedule? Schedule { get; init; }
        public int AbsentThisMonth { get; init; }

        public string Status { get => _status; set => Set(ref _status, value, nameof(Status)); }
        /// <summary>"08:05"; empty when not recorded.</summary>
        public string? TimeIn { get => _timeIn; set => Set(ref _timeIn, value, nameof(TimeIn)); }
        public string? TimeOut { get => _timeOut; set => Set(ref _timeOut, value, nameof(TimeOut)); }
        public string? Remarks { get => _remarks; set => Set(ref _remarks, value, nameof(Remarks)); }

        private bool Came => Status is "P" or "T";
        public int LateMinutes => Came && StaffTimes.TryParse(TimeIn, out var t) ? StaffTimes.LateBy(Schedule, t) : 0;
        public int EarlyMinutes => Came && StaffTimes.TryParse(TimeOut, out var t) ? StaffTimes.EarlyBy(Schedule, t) : 0;
        public string LateBy => LateMinutes > 0 ? $"{LateMinutes} min" : "";
        public string EarlyBy => EarlyMinutes > 0 ? $"{EarlyMinutes} min" : "";

        private void Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new(name));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static readonly (string Code, string Name)[] Statuses = [("P", "Present"), ("A", "Absent"), ("L", "Leave"), ("T", "Late")];
    private const string AllCampuses = "All campuses", NoCampus = "No campus", AllKinds = "All staff", AllPosts = "All posts";

    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd/MM/yyyy" };
    private readonly ComboBox _campus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _post = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonLabel _banner = new()
    {
        Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), StateCommon = { ShortText = { Color1 = Theme.Muted } }
    };
    private readonly StatCard _total = new("Staff"), _present = new("Present"), _absent = new("Absent"), _leave = new("Leave"), _late = new("Late"), _early = new("Left early");
    private readonly AccessGrid _grid = new();
    private readonly bool _canEdit;
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private Dictionary<DateTime, string?> _holidays = [];
    private FilterBindingList<Row>? _list;
    private (DateTime Date, string? TakenBy, DateTime? TakenOn)? _taken;
    private StaffSchedule? _schedule;
    private DateTime? _shown;
    private bool _loading, _dirty, _filling;

    public StaffAttendanceScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Staff);
        _date.Value = DateTime.Today;
        _date.MaxDate = DateTime.Today;
        _campus.Items.Add(AllCampuses);
        _campus.SelectedIndex = 0;
        _kind.Items.AddRange([AllKinds, "Academic", "Non Academic"]);
        _kind.SelectedIndex = 0;
        _post.Items.Add(AllPosts);
        _post.SelectedIndex = 0;

        var fields = new FieldPanel(4);
        fields.Add("Date", _date);
        fields.Add("Campus", _campus);
        fields.Add("Staff type", _kind);
        fields.Add("Post", _post);
        var choose = new Card();
        choose.AddTop(fields);
        var stats = new EqualColumns { Padding = new Padding(0, Card.Gap, 0, 0) };
        stats.Controls.AddRange([_total, _present, _absent, _leave, _late, _early]);

        BuildGrid();
        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0),
            Text = _canEdit ? "Everyone starts present at the schedule's times. Change only those who differ: press A, L or P, or pick a time from the list. You can also type it: 815 means 08:15, and a time out of 3 means 15:00. Then Save."
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
        _campus.SelectedIndexChanged += (_, _) => ApplyFilters();
        _kind.SelectedIndexChanged += (_, _) => ApplyFilters();
        _post.SelectedIndexChanged += (_, _) => ApplyFilters();

        _commands =
        [
            new("Mark",
                new ScreenCommand("Present", Icons.Check, () => Mark(r => r.Status = "P")),
                new ScreenCommand("Absent", Icons.Cross, () => Mark(r => r.Status = "A")),
                new ScreenCommand("Leave", Icons.Calendar, () => Mark(r => r.Status = "L")),
                new ScreenCommand("Late", Icons.Clock, () => Mark(r => r.Status = "T"))),
            new("Time",
                new ScreenCommand("All on time", Icons.Check, AllOnTime),
                new ScreenCommand("On time", Icons.Clock, OnTime),
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
        $"{_list.Count:N0} staff · {Count("P") + Count("T"):N0} present · {Count("A"):N0} absent · {Count("L"):N0} leave · {Count("T"):N0} late · {LeftEarly:N0} left early";

    private int Count(string status) => _list?.Count(r => r.Status == status) ?? 0;
    private int LeftEarly => _list?.Count(r => r.EarlyMinutes > 0) ?? 0;

    public override void NewRecord()
    {
        if (!_canEdit || _list is null) return;
        foreach (var r in _list) r.Status = "P";
    }

    /// <summary>Gives the staff on show the schedule's times where no time is typed yet, and skips anyone absent or on leave.</summary>
    private void AllOnTime()
    {
        if (!_canEdit || _list is null || !HasSchedule()) return;
        _grid.EndEdit();
        foreach (var r in _list.Where(r => !r.IsDirector && r.Status is "P" or "T"))
        {
            r.TimeIn ??= StaffTimes.Text(_schedule!.CheckIn);
            r.TimeOut ??= StaffTimes.Text(_schedule!.CheckOut);
        }
    }

    /// <summary>Puts the selected staff back to present at the schedule's times.</summary>
    private void OnTime()
    {
        if (!HasSchedule()) return;
        Mark(r =>
        {
            r.Status = "P";
            r.TimeIn = StaffTimes.Text(_schedule!.CheckIn);
            r.TimeOut = StaffTimes.Text(_schedule!.CheckOut);
        });
    }

    private bool HasSchedule()
    {
        if (_schedule is not null) return true;
        Inform("No staff schedule covers this date. Add one in Staff schedule.");
        return false;
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
        // A day not taken yet is saved even if nothing was changed: everyone on time is a real result.
        if (!_dirty && _taken is not null) return true;
        if (!_canEdit || _list is null || _shown is not { } date) return false;
        var times = new Dictionary<Row, (TimeSpan? In, TimeSpan? Out)>();
        foreach (var r in _list.AllItems)
        {
            if (!StaffTimes.TryParse(r.TimeIn, out var tin) || !StaffTimes.TryParse(r.TimeOut, out var tout))
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
            var schedule = _schedule = StaffTimes.For(db.StaffSchedules.AsNoTracking().Where(s => s.From <= date && s.To >= date).ToList(), date);
            var campuses = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => SalarySheetScreen.CampusName(s.Name));
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
                var director = string.Equals(type.Name, "Director", StringComparison.OrdinalIgnoreCase);
                var scheduled = director || mark is null; // Anyone saved on the day keeps what was saved.
                return new Row
                {
                    Sr = ++sr, Id = s.Id, Name = s.Name?.Trim() ?? "", Post = type.Name is { Length: > 0 } n ? n : s.Designation?.Trim() ?? "",
                    Kind = type.Kind ?? "", Mobile = s.CellNo, AbsentThisMonth = absences.GetValueOrDefault(s.Id),
                    Campus = s.BranchId is int b ? campuses.GetValueOrDefault(b, "") : "", IsDirector = director, Schedule = schedule,
                    Status = director ? "P" : mark?.Status.Trim().ToUpperInvariant() ?? "P",
                    TimeIn = TimeText(mark?.TimeIn, scheduled ? schedule?.CheckIn : null), TimeOut = TimeText(mark?.TimeOut, scheduled ? schedule?.CheckOut : null),
                    Remarks = mark?.Remarks,
                };
            }).ToList();
            foreach (var r in rows)
            {
                AddTimeChoice(r.TimeIn);
                AddTimeChoice(r.TimeOut);
                r.PropertyChanged += (_, e) => RowChanged(r, e.PropertyName);
            }
            _list = new FilterBindingList<Row>(rows);
            _grid.Bind(_list);
            var first = marks.Values.MaxBy(m => m.TakenOn);
            _taken = first is null ? null : (date, first.TakenBy, first.TakenOn);
            _shown = date;
            _dirty = false;
            FillFilters();
            ApplyFilters();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not load the staff attendance. Have sql/attendance_roles_install.sql and sql/staff_schedule_install.sql been run on this database?", ex);
        }
        finally { _loading = false; }
        ShowBanner();
        UpdateCounts();
    }

    private static string? TimeText(TimeSpan? saved, TimeSpan? otherwise) => (saved ?? otherwise) is { } t ? StaffTimes.Text(t) : null;

    /// <summary>Puts the campuses and posts of the staff on the lists, keeping what is chosen if it is still there.</summary>
    private void FillFilters()
    {
        if (_list is null) return;
        _filling = true;
        var campuses = _list.AllItems.Select(r => r.Campus.Length == 0 ? NoCampus : r.Campus).Distinct().OrderBy(c => c == NoCampus).ThenBy(c => c);
        Refill(_campus, AllCampuses, campuses);
        Refill(_post, AllPosts, PostsOfKind());
        _filling = false;
    }

    private IEnumerable<string> PostsOfKind()
    {
        var kind = _kind.SelectedItem as string;
        return _list?.AllItems.Where(r => kind == AllKinds || r.Kind == kind).Select(r => r.Post).Where(p => p.Length > 0).Distinct().Order() ?? Enumerable.Empty<string>();
    }

    private static void Refill(ComboBox combo, string all, IEnumerable<string> items)
    {
        var keep = combo.SelectedItem;
        combo.Items.Clear();
        combo.Items.Add(all);
        combo.Items.AddRange(items.Cast<object>().ToArray());
        combo.SelectedItem = keep is not null && combo.Items.Contains(keep) ? keep : all;
    }

    /// <summary>Shows only the chosen campus, staff type and post. Everyone is still saved.</summary>
    private void ApplyFilters()
    {
        if (_list is null || _filling) return;
        _filling = true;
        Refill(_post, AllPosts, PostsOfKind());
        _filling = false;
        var (campus, kind, post) = (_campus.SelectedItem as string, _kind.SelectedItem as string, _post.SelectedItem as string);
        _list.SetSearch(r => (campus == AllCampuses || (r.Campus.Length == 0 ? NoCampus : r.Campus) == campus)
            && (kind == AllKinds || r.Kind == kind) && (post == AllPosts || r.Post == post));
        UpdateCounts();
    }

    /// <summary>A time cell can only show a time on its list, so one that is not on the list is added in its place.</summary>
    private void AddTimeChoice(string? time)
    {
        if (string.IsNullOrEmpty(time)) return;
        foreach (var name in new[] { nameof(Row.TimeIn), nameof(Row.TimeOut) })
        {
            var items = ((DataGridViewComboBoxColumn)_grid.Columns[name]!).Items;
            if (items.Contains(time)) continue;
            var at = items.Cast<string>().TakeWhile(i => string.CompareOrdinal(i, time) < 0).Count();
            items.Insert(at, time);
        }
    }

    /// <summary>Tidies a typed time, marks the person late or present from the schedule, and clears or fills the times with the attendance.</summary>
    private void RowChanged(Row row, string? property)
    {
        if (_loading) return;
        if (property == nameof(Row.Status) && row.Status is "A" or "L")
        {
            row.TimeIn = row.TimeOut = null;
        }
        else if (property == nameof(Row.Status) && row.Schedule is { } schedule)
        {
            row.TimeIn ??= StaffTimes.Text(schedule.CheckIn);
            row.TimeOut ??= StaffTimes.Text(schedule.CheckOut);
        }
        else if (property == nameof(Row.TimeIn) && StaffTimes.TryParse(row.TimeIn, out var timeIn))
        {
            var text = timeIn is { } t ? StaffTimes.Text(t) : null;
            AddTimeChoice(text);
            if (text != row.TimeIn) { row.TimeIn = text; return; }
            if (timeIn is not null && row.Schedule is not null && row.Status is "P" or "T")
                row.Status = StaffTimes.LateBy(row.Schedule, timeIn) > 0 ? "T" : "P";
        }
        else if (property == nameof(Row.TimeOut) && StaffTimes.TryParse(row.TimeOut, out var timeOut))
        {
            var text = timeOut is { } t ? StaffTimes.Text(StaffTimes.Afternoon(t, row.Schedule)) : null;
            AddTimeChoice(text);
            if (text != row.TimeOut) { row.TimeOut = text; return; }
        }
        Changed();
    }

    private void ShowBanner()
    {
        var parts = new List<string>();
        var warn = false;
        if (HolidayText(_date.Value) is { } holiday) { parts.Add($"{_date.Value:dddd dd/MM/yyyy} is {holiday}."); warn = true; }
        parts.Add(_schedule is { } s
            ? $"{s.Name}: in {StaffTimes.Text(s.CheckIn)}, out {StaffTimes.Text(s.CheckOut)}, late after {StaffTimes.Text(s.CheckIn + TimeSpan.FromMinutes(s.LateMinutes))}, early before {StaffTimes.Text(s.CheckOut - TimeSpan.FromMinutes(s.EarlyMinutes))}."
            : "No staff schedule covers this date, so late and early are not worked out.");
        parts.Add(_taken is { } t ? $"Taken by {t.TakenBy} on {t.TakenOn:dd/MM/yyyy HH:mm}." : "Not taken yet: everyone is present at the schedule's times until you save.");
        if (_dirty) parts.Add("Not saved.");
        _banner.Text = string.Join("  ", parts);
        _banner.StateCommon.ShortText.Color1 = warn ? Theme.Warning : _dirty ? Theme.Accent : Theme.Muted;
        _grid.ReadOnly = !_canEdit;
    }

    private void UpdateCounts()
    {
        _total.SetValue($"{_list?.Count ?? 0:N0}");
        _present.SetValue($"{Count("P") + Count("T"):N0}", Theme.Good);
        _absent.SetValue($"{Count("A"):N0}", Count("A") > 0 ? Theme.Error : null);
        _leave.SetValue($"{Count("L"):N0}");
        _late.SetValue($"{Count("T"):N0}", Count("T") > 0 ? Theme.Warning : null);
        _early.SetValue($"{LeftEarly:N0}", LeftEarly > 0 ? Theme.Warning : null);
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
        foreach (var r in SelectedRows().Where(r => !r.IsDirector)) change(r);
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
        string[] times = ["", .. StaffTimes.Choices];
        _grid.AddChoices(nameof(Row.TimeIn), "Time in", times, 85);
        _grid.AddText(nameof(Row.LateBy), "Late by", 65, readOnly: true);
        _grid.AddChoices(nameof(Row.TimeOut), "Time out", times, 85);
        _grid.AddText(nameof(Row.EarlyBy), "Early by", 65, readOnly: true);
        _grid.AddText(nameof(Row.AbsentThisMonth), "Absent this month", 120, readOnly: true).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Remarks), "Remarks", 200, fill: true);
        _grid.AddText(nameof(Row.Mobile), "Mobile", 115, readOnly: true);
        _grid.FormatNumbers(nameof(Row.Mobile), NumberFormat.Phone);
        _grid.CanEditCell = (row, column) => _canEdit && _grid.Columns[column].DataPropertyName switch
        {
            nameof(Row.Remarks) => true,
            nameof(Row.Status) or nameof(Row.TimeIn) or nameof(Row.TimeOut) => _grid.Rows[row].DataBoundItem is Row { IsDirector: false },
            _ => false,
        };

        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            var column = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (column is nameof(Row.LateBy) or nameof(Row.EarlyBy)) e.CellStyle!.ForeColor = Theme.Warning;
            if (row.IsDirector && column is nameof(Row.TimeIn) or nameof(Row.TimeOut)) e.CellStyle!.ForeColor = Theme.Muted;
            if (column != nameof(Row.Status)) return;
            e.CellStyle!.ForeColor = row.Status switch { "A" => Theme.Error, "T" => Theme.Warning, "L" => Theme.Accent, _ => Theme.Good };
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        };
        // The time lists also take a typed time, and one click on a time opens its list.
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not ComboBox combo) return;
            combo.DropDownStyle = IsTime(_grid.CurrentCell) ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            combo.MaxDropDownItems = 14;
            combo.TextUpdate -= TimeTyped; // The grid reuses this control; a typed time only saves if the cell is marked changed.
            combo.TextUpdate += TimeTyped;
        };
        // What is typed becomes a time on the list; anything that is not a time puts the old one back.
        _grid.CellParsing += (_, e) =>
        {
            var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (!IsTime(cell) || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            var isOut = cell.OwningColumn.DataPropertyName == nameof(Row.TimeOut);
            e.Value = !StaffTimes.TryParse(e.Value as string, out var time) ? (isOut ? row.TimeOut : row.TimeIn)
                : time is { } t ? StaffTimes.Text(isOut ? StaffTimes.Afternoon(t, row.Schedule) : t) : null;
            AddTimeChoice(e.Value as string);
            e.ParsingApplied = true;
        };
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !IsTime(_grid.Rows[e.RowIndex].Cells[e.ColumnIndex]) || ModifierKeys != Keys.None) return;
            if (_grid.BeginEdit(false) && _grid.EditingControl is ComboBox combo) combo.DroppedDown = true;
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
            if (!_canEdit || e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(Row.Status) || _grid.Rows[e.RowIndex].DataBoundItem is not Row { IsDirector: false } row) return;
            var i = Array.FindIndex(Statuses, s => s.Code == row.Status);
            row.Status = Statuses[(i + 1) % Statuses.Length].Code;
        };
    }

    private void TimeTyped(object? sender, EventArgs e)
    {
        if (IsTime(_grid.CurrentCell)) _grid.NotifyCurrentCellDirty(true);
    }

    private static bool IsTime(DataGridViewCell? cell) => cell?.OwningColumn?.DataPropertyName is nameof(Row.TimeIn) or nameof(Row.TimeOut);

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Staff attendance", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
