using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// The month's staff attendance register: each person's mark on each day, their totals and average time in.
/// </summary>
public sealed class StaffAttendanceReport : ReportView
{
    private sealed record Mark(DateTime Date, int StaffId, string Status, TimeSpan? TimeIn);
    private sealed record Person(int Id, string Name, string Post, string Kind, DateTime? LeftOn);
    private const string AllStaff = "All staff";

    private readonly ComboBox _month = Combo(140);
    private readonly ComboBox _kind = Combo(130);
    private List<Mark> _marks = [];
    private List<Person> _staff = [];
    private HashSet<DateTime> _holidays = [];
    private FilterBindingList<MonthlyAttendanceReport.Row>? _list;
    private bool _filling;

    public StaffAttendanceReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Fill<DateTime>(_month, null, Enumerable.Range(0, 24).Select(i => month.AddMonths(-i)).Select(m => (m, m.ToString("MMMM yyyy"))));
        _kind.Items.AddRange([AllStaff, "Academic", "Non Academic"]);
        _kind.SelectedIndex = 0;
        _filling = false;
        Filters.Controls.AddRange([_month, _kind]);
        AddShowButton();
        _month.SelectedIndexChanged += (_, _) => { if (!_filling) _ = ShowReportAsync(); };
        _kind.SelectedIndexChanged += (_, _) => Apply();
        Hint = "P present, A absent, L leave, T late (counts as present), H holiday or Sunday; a blank day was not taken. Average time in is of the days a time was recorded.";
        Grid.AddText(nameof(MonthlyAttendanceReport.Row.Cc), "ID", 50);
        Grid.AddText(nameof(MonthlyAttendanceReport.Row.Name), "Name", 170, fill: true);
        Grid.AddText(nameof(MonthlyAttendanceReport.Row.ClassName), "Post", 110);
        Grid.AddText(nameof(MonthlyAttendanceReport.Row.SectionName), "Staff", 100);
        MonthlyAttendanceReport.AddDayColumns(Grid);
        AddNumber(nameof(MonthlyAttendanceReport.Row.Present), "P", 40);
        AddNumber(nameof(MonthlyAttendanceReport.Row.Absent), "A", 40);
        AddNumber(nameof(MonthlyAttendanceReport.Row.Leave), "L", 40);
        AddNumber(nameof(MonthlyAttendanceReport.Row.Late), "T", 40);
        AddNumber(nameof(MonthlyAttendanceReport.Row.Percent), "%", 45, sum: false);
        Grid.AddText(nameof(MonthlyAttendanceReport.Row.AverageTimeIn), "Avg in", 60);
    }

    public override string Title => "Staff attendance";
    public override string Replaces => "nothing (new in this app)";

    private DateTime Month => Selected<DateTime>(_month);

    public override string StatusText => $"{_list?.Count ?? 0:N0} staff · {_marks.Select(m => m.Date).Distinct().Count():N0} days taken";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<MonthlyAttendanceReport.Row>().ToList();
            if (rows.Count == 0) return [];
            var present = rows.Sum(r => r.Present + r.Late);
            var marked = rows.Sum(r => r.Marked);
            return [("Staff", $"{rows.Count:N0}"), ("Absences", $"{rows.Sum(r => r.Absent):N0}"), ("Leave", $"{rows.Sum(r => r.Leave):N0}"),
                ("Late", $"{rows.Sum(r => r.Late):N0}"), ("Attendance", AttendanceData.Percent(present, marked) is int p ? $"{p}%" : "not taken")];
        }
    }

    protected override string FilterText => $"{Month:MMMM yyyy} · {_kind.SelectedItem}";
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [];
    protected override IReadOnlyList<string> Signatures => ["Prepared by", "Principal"];

    protected override async Task LoadAsync()
    {
        var month = Month;
        var (staff, marks, holidays) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var after = month.AddMonths(1);
            var types = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => (Kind: t.Kind?.Trim() ?? "", Name: t.Name?.Trim() ?? ""));
            var staff = db.Staff.AsNoTracking().ToList().Select(s =>
            {
                var type = s.TypeId is int t ? types.GetValueOrDefault(t) : default;
                return new Person(s.Id, s.Name?.Trim() ?? "", type.Name is { Length: > 0 } n ? n : s.Designation?.Trim() ?? "", type.Kind ?? "", s.LeftOn);
            }).ToList();
            var marks = db.StaffAttendance.AsNoTracking().Where(a => a.Date >= month && a.Date < after)
                .Select(a => new { a.Date, a.StaffId, a.Status, a.TimeIn }).AsEnumerable()
                .Select(a => new Mark(a.Date.Date, a.StaffId, a.Status.Trim().ToUpperInvariant(), a.TimeIn)).ToList();
            var holidays = db.Calendar.AsNoTracking().Where(d => d.IsHoliday && d.Date >= month && d.Date < after).Select(d => d.Date).ToList();
            return (staff, marks, holidays);
        });
        if (IsDisposed) return;
        _staff = staff;
        _marks = marks;
        _holidays = holidays.Select(d => d.Date).ToHashSet();
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var month = Month;
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        for (var d = 1; d <= 31; d++) Grid.Columns[$"D{d}"]!.Visible = d <= days;
        var kind = _kind.SelectedItem as string;
        var byStaff = _marks.GroupBy(m => m.StaffId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = _staff.Where(s => (s.LeftOn is null || s.LeftOn >= month || byStaff.ContainsKey(s.Id)) && (kind == AllStaff || s.Kind == kind))
            .Select(s =>
            {
                var marks = byStaff.GetValueOrDefault(s.Id) ?? [];
                int N(string status) => marks.Count(m => m.Status == status);
                var times = marks.Where(m => m.TimeIn is not null).Select(m => m.TimeIn!.Value.TotalMinutes).ToList();
                return new MonthlyAttendanceReport.Row
                {
                    Days = MonthlyAttendanceReport.DayCells(month, _holidays, marks.Select(m => (m.Date, m.Status))),
                    Cc = s.Id, Name = s.Name, ClassName = s.Post, SectionName = s.Kind,
                    Present = N("P"), Absent = N("A"), Leave = N("L"), Late = N("T"), Marked = marks.Count,
                    Percent = AttendanceData.Percent(marks.Count(m => AttendanceData.CountsAsPresent(m.Status)), marks.Count),
                    AverageTimeIn = times.Count == 0 ? "" : TimeSpan.FromMinutes(Math.Round(times.Average())).ToString("hh\\:mm"),
                };
            })
            .OrderBy(r => r.SectionName).ThenBy(r => r.Name).ToList();
        _list = new FilterBindingList<MonthlyAttendanceReport.Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}
