using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>One saved mark with its register's date, class and section.</summary>
internal sealed record AttendanceMark(DateTime Date, int ClassId, int SectionId, int Cc, string Status);

internal static class AttendanceData
{
    /// <summary>The marks saved from <paramref name="first"/> up to (not including) <paramref name="after"/>.</summary>
    public static List<AttendanceMark> Read(ArqamBase.Data.ArqamDbContext db, DateTime first, DateTime after) =>
        (from m in db.StudentAttendance.AsNoTracking()
         join r in db.AttendanceRegisters.AsNoTracking() on m.RegisterId equals r.Id
         where r.Date >= first && r.Date < after
         select new { r.Date, r.ClassId, r.SectionId, m.StudentId, m.Status }).AsEnumerable()
        .Select(x => new AttendanceMark(x.Date.Date, x.ClassId, x.SectionId, x.StudentId, x.Status.Trim().ToUpperInvariant())).ToList();

    /// <summary>Present and late count as present.</summary>
    public static bool CountsAsPresent(string status) => status is "P" or "T";

    public static int? Percent(int present, int days) => days == 0 ? null : (int)Math.Round(100.0 * present / days);
}

/// <summary>
/// The month's attendance register of a class: each student's mark on each day, and their totals
/// (replaces the Access month sheet in table Attendance, columns 1 to 31). H marks a holiday or Sunday.
/// </summary>
public sealed class MonthlyAttendanceReport : ReportView
{
    public sealed class Row
    {
        internal string[] Days { get; init; } = new string[32];
        public int Cc { get; init; }
        public string Name { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public int Present { get; init; }
        public int Absent { get; init; }
        public int Leave { get; init; }
        public int Late { get; init; }
        /// <summary>Staff only: the days they left before the schedule allows.</summary>
        public int LeftEarly { get; init; }
        public int Marked { get; init; }
        public int? Percent { get; init; }
        /// <summary>Staff only: the average time in, e.g. "08:05".</summary>
        public string AverageTimeIn { get; init; } = "";
        internal string ClassSort { get; init; } = "";
        private string Day(int d) => Days[d] ?? "";
        public string D1 => Day(1);
        public string D2 => Day(2);
        public string D3 => Day(3);
        public string D4 => Day(4);
        public string D5 => Day(5);
        public string D6 => Day(6);
        public string D7 => Day(7);
        public string D8 => Day(8);
        public string D9 => Day(9);
        public string D10 => Day(10);
        public string D11 => Day(11);
        public string D12 => Day(12);
        public string D13 => Day(13);
        public string D14 => Day(14);
        public string D15 => Day(15);
        public string D16 => Day(16);
        public string D17 => Day(17);
        public string D18 => Day(18);
        public string D19 => Day(19);
        public string D20 => Day(20);
        public string D21 => Day(21);
        public string D22 => Day(22);
        public string D23 => Day(23);
        public string D24 => Day(24);
        public string D25 => Day(25);
        public string D26 => Day(26);
        public string D27 => Day(27);
        public string D28 => Day(28);
        public string D29 => Day(29);
        public string D30 => Day(30);
        public string D31 => Day(31);
    }

    private readonly ClassFilter _classes = new();
    private readonly ComboBox _month = Combo(140);
    private List<AttendanceMark> _marks = [];
    private List<StudentInfo> _students = [];
    private HashSet<DateTime> _holidays = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public MonthlyAttendanceReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Fill<DateTime>(_month, null, Enumerable.Range(0, 24).Select(i => month.AddMonths(-i)).Select(m => (m, m.ToString("MMMM yyyy"))));
        _filling = false;
        Filters.Controls.AddRange([_month, .. _classes.Controls]);
        AddShowButton();
        _month.SelectedIndexChanged += (_, _) => { if (!_filling) _ = ShowReportAsync(); };
        _classes.Changed += (_, _) => Apply();
        Hint = "P present, A absent, L leave, T late (counts as present), H holiday or Sunday; a blank day was not taken. Students on roll and any marked that month.";
        Grid.AddText(nameof(Row.Cc), "CC", 55);
        Grid.AddText(nameof(Row.Name), "Name", 170, fill: true);
        Grid.AddText(nameof(Row.ClassName), "Class", 80);
        Grid.AddText(nameof(Row.SectionName), "Section", 70);
        AddDayColumns(Grid);
        AddNumber(nameof(Row.Present), "P", 40);
        AddNumber(nameof(Row.Absent), "A", 40);
        AddNumber(nameof(Row.Leave), "L", 40);
        AddNumber(nameof(Row.Late), "T", 40);
        AddNumber(nameof(Row.Percent), "%", 45, sum: false);
    }

    /// <summary>Columns D1 to D31 for the days of the month, coloured by mark.</summary>
    internal static void AddDayColumns(AccessGrid grid)
    {
        for (var d = 1; d <= 31; d++)
        {
            var col = grid.AddText($"D{d}", $"{d}", 30);
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            col.HeaderCell.Style.Padding = new Padding(0);
        }
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex < 0 || !grid.Columns[e.ColumnIndex].Name.StartsWith('D') || e.Value is not string v) return;
            e.CellStyle!.ForeColor = v switch { "A" => Theme.Error, "T" => Theme.Warning, "L" => Theme.Accent, "H" => Theme.Muted, _ => e.CellStyle.ForeColor };
        };
    }

    /// <summary>The day cells of a month: H on holidays and Sundays, then each saved mark.</summary>
    internal static string[] DayCells(DateTime month, HashSet<DateTime> holidays, IEnumerable<(DateTime Date, string Status)> marks)
    {
        var cells = new string[32];
        for (var d = 1; d <= DateTime.DaysInMonth(month.Year, month.Month); d++)
        {
            var date = new DateTime(month.Year, month.Month, d);
            if (holidays.Contains(date) || date.DayOfWeek == DayOfWeek.Sunday) cells[d] = "H";
        }
        foreach (var (date, status) in marks) cells[date.Day] = status;
        return cells;
    }

    public override string Title => "Monthly attendance";
    public override string Replaces => "the Access month sheet (table Attendance)";

    private DateTime Month => Selected<DateTime>(_month) is { Year: > 1 } month ? month : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<Row>().ToList() ?? [];
            return $"{rows.Count:N0} students · {_marks.Select(m => (m.Date, m.ClassId, m.SectionId)).Distinct().Count():N0} registers taken";
        }
    }

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            if (rows.Count == 0) return [];
            var present = rows.Sum(r => r.Present + r.Late);
            var marked = rows.Sum(r => r.Marked);
            return [("Students", $"{rows.Count:N0}"), ("Absences", $"{rows.Sum(r => r.Absent):N0}"), ("Leave", $"{rows.Sum(r => r.Leave):N0}"),
                ("Late", $"{rows.Sum(r => r.Late):N0}"), ("Attendance", AttendanceData.Percent(present, marked) is int p ? $"{p}%" : "not taken")];
        }
    }

    protected override string FilterText => string.Join(" · ", new[] { Month.ToString("MMMM yyyy") }.Concat(_classes.TextParts()));
    protected override int? PrintSchoolId => _classes.SchoolId;
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups =>
        [("Class", nameof(Row.ClassName)), ("Section", nameof(Row.SectionName))];

    protected override async Task LoadAsync()
    {
        var month = Month;
        var (lookup, students, marks, holidays) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var after = month.AddMonths(1);
            var holidays = db.Calendar.AsNoTracking().Where(d => d.IsHoliday && d.Date >= month && d.Date < after).Select(d => d.Date).ToList();
            return (ClassLookup.Read(db).For(Ctx.Session), StudentInfo.Read(db, onRollOnly: false), AttendanceData.Read(db, month, after), holidays);
        });
        if (IsDisposed) return;
        _students = students;
        _marks = marks;
        _holidays = holidays.Select(d => d.Date).ToHashSet();
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var month = Month;
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        for (var d = 1; d <= 31; d++) Grid.Columns[$"D{d}"]!.Visible = d <= days;
        var byStudent = _marks.GroupBy(m => m.Cc).ToDictionary(g => g.Key, g => g.ToList());
        var rows = _students.Where(s => s.OnRoll || byStudent.ContainsKey(s.Cc))
            .Select(s =>
            {
                var marks = byStudent.GetValueOrDefault(s.Cc) ?? [];
                // A student is listed under the class they were marked in that month, else their current class.
                var last = marks.MaxBy(m => m.Date);
                var (classId, sectionId) = last is null ? (s.ClassId, s.SectionId) : ((int?)last.ClassId, (int?)last.SectionId);
                return (s, marks, classId, sectionId);
            })
            .Where(x => _classes.Matches(x.classId, x.sectionId))
            .Select(x =>
            {
                var cells = DayCells(month, _holidays, x.marks.Select(m => (m.Date, m.Status)));
                int N(string status) => x.marks.Count(m => m.Status == status);
                return new Row
                {
                    Days = cells, Cc = x.s.Cc, Name = x.s.Name, ClassName = _classes.ClassName(x.classId), ClassSort = _classes.ClassSort(x.classId),
                    SectionName = _classes.SectionName(x.sectionId), Present = N("P"), Absent = N("A"), Leave = N("L"), Late = N("T"),
                    Marked = x.marks.Count, Percent = AttendanceData.Percent(x.marks.Count(m => AttendanceData.CountsAsPresent(m.Status)), x.marks.Count),
                };
            })
            .OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ToList();
        _list = new FilterBindingList<Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}

/// <summary>
/// Absences in a date range: each student's absent, leave and late days and their dates, or each day's attendance by class
/// (with the classes whose attendance was not taken).
/// </summary>
public sealed class AbsenteeReport : ReportView
{
    public sealed class Row
    {
        public DateTime? Date { get; init; }
        public int? Cc { get; init; }
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public int Students { get; init; }
        public int Present { get; init; }
        public int Absent { get; init; }
        public int Leave { get; init; }
        public int Late { get; init; }
        public int Marked { get; init; }
        public int? Percent { get; init; }
        public string AbsentOn { get; init; } = "";
        public string Taken { get; init; } = "";
        public string? Mobile { get; init; }
        internal string ClassSort { get; init; } = "";
    }

    private readonly KryptonDateTimePicker _from = DatePicker();
    private readonly KryptonDateTimePicker _to = DatePicker();
    private readonly ClassFilter _classes = new();
    private readonly ComboBox _view = Combo(150);
    private readonly NumericUpDown _atLeast = new() { Width = 60, Minimum = 0, Maximum = 366, Value = 1, Margin = new Padding(4, 3, 0, 0) };
    private List<AttendanceMark> _marks = [];
    private List<StudentInfo> _students = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public AbsenteeReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<bool>(_view, null, [(true, "Each student"), (false, "Each day by class")]);
        _filling = false;
        _from.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _to.Value = DateTime.Today;
        Filters.Controls.AddRange([Caption("From"), _from, Caption("To"), _to, .. _classes.Controls, _view, Caption("Absent at least"), _atLeast]);
        AddShowButton();
        _from.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _to.Value = _from.Value; };
        _to.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _from.Value = _to.Value; };
        _classes.Changed += (_, _) => Apply();
        _view.SelectedIndexChanged += (_, _) => Apply();
        _atLeast.ValueChanged += (_, _) => Apply();
        Hint = "Change the dates and click Show. Each student lists those absent at least that many days, most absent first; each day by class also shows classes not taken (school days only).";
    }

    public override string Title => "Absentee list";
    public override string Replaces => "the Access attendance queries";

    public override string StatusText => $"{_list?.Count ?? 0:N0} rows";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            if (rows.Count == 0) return [];
            var present = rows.Sum(r => r.Present + r.Late);
            var marked = rows.Sum(r => r.Marked);
            var lines = new List<(string, string)>();
            if (Selected<bool>(_view)) lines.Add(("Students", $"{rows.Count:N0}"));
            else lines.Add(("Not taken", $"{rows.Count(r => r.Marked == 0):N0} classes"));
            lines.AddRange([("Absences", $"{rows.Sum(r => r.Absent):N0}"), ("Leave", $"{rows.Sum(r => r.Leave):N0}"), ("Late", $"{rows.Sum(r => r.Late):N0}"),
                ("Attendance", AttendanceData.Percent(present, marked) is int p ? $"{p}%" : "not taken")]);
            return lines;
        }
    }

    protected override string FilterText =>
        string.Join(" · ", new[] { $"{_from.Value:dd/MM/yyyy} to {_to.Value:dd/MM/yyyy}" }.Concat(_classes.TextParts())
            .Concat(Selected<bool>(_view) && _atLeast.Value > 0 ? [$"Absent at least {_atLeast.Value:N0} days"] : []));

    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<bool>(_view)
        ? [("Class", nameof(Row.ClassName))]
        : [("Date", nameof(Row.Date))];

    private HashSet<DateTime> _holidays = [];

    protected override async Task LoadAsync()
    {
        var (first, after) = (_from.Value.Date, _to.Value.Date.AddDays(1));
        var (lookup, students, marks, holidays) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var holidays = db.Calendar.AsNoTracking().Where(d => d.IsHoliday && d.Date >= first && d.Date < after).Select(d => d.Date).ToList();
            return (ClassLookup.Read(db).For(Ctx.Session), StudentInfo.Read(db, onRollOnly: false), AttendanceData.Read(db, first, after), holidays);
        });
        if (IsDisposed) return;
        _students = students;
        _marks = marks;
        _holidays = holidays.Select(d => d.Date).ToHashSet();
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var each = Selected<bool>(_view);
        var marks = _marks.Where(m => _classes.Matches(m.ClassId, m.SectionId)).ToList();
        var students = _students.ToDictionary(s => s.Cc);
        List<Row> rows;
        if (each)
            rows = marks.GroupBy(m => m.Cc)
                .Select(g =>
                {
                    var s = students.GetValueOrDefault(g.Key);
                    var last = g.MaxBy(m => m.Date)!;
                    int N(string status) => g.Count(m => m.Status == status);
                    return new Row
                    {
                        Cc = g.Key, Name = s?.Name ?? "", FatherName = s?.Father ?? "", Mobile = s?.Mobile,
                        ClassName = _classes.ClassName(last.ClassId), ClassSort = _classes.ClassSort(last.ClassId), SectionName = _classes.SectionName(last.SectionId),
                        Present = N("P"), Absent = N("A"), Leave = N("L"), Late = N("T"), Marked = g.Count(),
                        Percent = AttendanceData.Percent(g.Count(m => AttendanceData.CountsAsPresent(m.Status)), g.Count()),
                        AbsentOn = string.Join(", ", g.Where(m => m.Status == "A").OrderBy(m => m.Date).Select(m => m.Date.ToString("dd/MM"))),
                    };
                })
                .Where(r => r.Absent >= _atLeast.Value)
                .OrderBy(r => r.ClassSort).ThenByDescending(r => r.Absent).ThenBy(r => r.Name).ToList();
        else
        {
            // Every class and section with students on roll, on every school day; those with no register show as not taken.
            var sections = _students.Where(s => s.OnRoll && s.ClassId is not null && s.SectionId is not null && _classes.Matches(s.ClassId, s.SectionId))
                .GroupBy(s => (ClassId: s.ClassId!.Value, SectionId: s.SectionId!.Value)).ToDictionary(g => g.Key, g => g.Count());
            var taken = marks.GroupBy(m => (m.Date, m.ClassId, m.SectionId)).ToDictionary(g => g.Key, g => g.ToList());
            var dates = Enumerable.Range(0, (_to.Value.Date - _from.Value.Date).Days + 1).Select(i => _from.Value.Date.AddDays(i))
                .Where(d => taken.Keys.Any(k => k.Date == d) || (d.DayOfWeek != DayOfWeek.Sunday && !_holidays.Contains(d)));
            rows = dates.SelectMany(d => sections.Keys.Union(taken.Keys.Where(k => k.Date == d).Select(k => (k.ClassId, k.SectionId))).Distinct()
                    .Select(k =>
                    {
                        var list = taken.GetValueOrDefault((d, k.ClassId, k.SectionId)) ?? [];
                        int N(string status) => list.Count(m => m.Status == status);
                        return new Row
                        {
                            Date = d, ClassName = _classes.ClassName(k.ClassId), ClassSort = _classes.ClassSort(k.ClassId), SectionName = _classes.SectionName(k.SectionId),
                            Students = list.Count > 0 ? list.Count : sections.GetValueOrDefault(k), Present = N("P"), Absent = N("A"), Leave = N("L"), Late = N("T"),
                            Marked = list.Count, Percent = AttendanceData.Percent(list.Count(m => AttendanceData.CountsAsPresent(m.Status)), list.Count),
                            Taken = list.Count > 0 ? "Taken" : "Not taken",
                        };
                    }))
                .OrderBy(r => r.Date).ThenBy(r => r.ClassSort).ThenBy(r => r.SectionName).ToList();
        }
        BuildColumns(each);
        _list = new FilterBindingList<Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    private bool? _columnsForEach;

    private void BuildColumns(bool each)
    {
        if (_columnsForEach == each) return;
        _columnsForEach = each;
        Grid.ResetColumns();
        SumProperties.Clear();
        if (each)
        {
            Grid.AddText(nameof(Row.Cc), "CC", 55);
            Grid.AddText(nameof(Row.Name), "Name", 170, fill: true);
            Grid.AddText(nameof(Row.FatherName), "Father name", 150, fill: true);
            Grid.AddText(nameof(Row.ClassName), "Class", 80);
            Grid.AddText(nameof(Row.SectionName), "Section", 70);
        }
        else
        {
            Grid.AddText(nameof(Row.Date), "Date", 90, format: "ddd dd/MM/yyyy");
            Grid.AddText(nameof(Row.ClassName), "Class", 110, fill: true);
            Grid.AddText(nameof(Row.SectionName), "Section", 90);
            AddNumber(nameof(Row.Students), "Students", 75);
        }
        AddNumber(nameof(Row.Absent), "Absent", 65);
        AddNumber(nameof(Row.Leave), "Leave", 60);
        AddNumber(nameof(Row.Late), "Late", 55);
        AddNumber(nameof(Row.Present), "Present", 65);
        AddNumber(nameof(Row.Percent), "%", 50, sum: false);
        if (each)
        {
            Grid.AddText(nameof(Row.AbsentOn), "Absent on", 200, fill: true);
            AddPhones((nameof(Row.Mobile), "Mobile"));
        }
        else Grid.AddText(nameof(Row.Taken), "Attendance", 90);
    }
}
