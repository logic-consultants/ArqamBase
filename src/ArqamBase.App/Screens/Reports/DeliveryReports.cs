using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// One row per family on roll, under its eldest child's class, with an empty column to sign when the challans are handed over
/// (replaces FeeDeliveryProforma).
/// </summary>
public sealed class DeliveryProformaReport : ReportView
{
    private readonly ClassFilter _classes = new();
    private FilterBindingList<StudentRow>? _list;
    private List<StudentInfo> _students = [];

    public DeliveryProformaReport(ScreenContext ctx) : base(ctx)
    {
        Filters.Controls.AddRange(_classes.Controls);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        Hint = "Each family on roll once, under the class of its child in the highest class (who takes the challans home). Print and Excel use the rows shown.";
        Grid.AddText(nameof(StudentRow.ClassName), "Highest class", 110);
        Grid.AddText(nameof(StudentRow.FamilyId), "Family", 60);
        Grid.AddText(nameof(StudentRow.FatherName), "Father name", 180, fill: true);
        AddPhones((nameof(StudentRow.Mobile), "Mobile"));
        AddNumber(nameof(StudentRow.Students), "Pupils", 70);
        Grid.AddText(nameof(StudentRow.Name), "Eldest child", 170, fill: true);
        Grid.AddText(nameof(StudentRow.SectionName), "Section", 80);
        AddBlank("Signature", 120);
    }

    public override string Title => "Fee delivery proforma";
    public override string Replaces => "FeeDeliveryProforma";
    public override string StatusText => $"{_list?.Count ?? 0:N0} families · {_list?.Cast<StudentRow>().Sum(r => r.Students) ?? 0:N0} pupils";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<StudentRow>().ToList();
            return rows.Count == 0 ? [] : [("Total families", $"{rows.Count:N0}"), ("Total pupils", $"{rows.Sum(r => r.Students):N0}")];
        }
    }

    protected override string FilterText => string.Join(" · ", _classes.TextParts().DefaultIfEmpty("All classes"));
    protected override int? PrintSchoolId => _classes.SchoolId;
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [("Class", nameof(StudentRow.ClassName))];

    protected override async Task LoadAsync()
    {
        var (lookup, students) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            return (ClassLookup.Read(db).For(Ctx.Session), StudentInfo.Read(db, onRollOnly: true));
        });
        if (IsDisposed) return;
        _students = students;
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        // The family's eldest child is the one in the highest class (classes sort from junior to senior).
        var rows = _students.GroupBy(s => s.FamilyId ?? -s.Cc)
            .Select(g => (Eldest: g.OrderByDescending(s => _classes.ClassSort(s.ClassId), StringComparer.Ordinal).ThenBy(s => s.Name).First(), Pupils: g.Count()))
            .Where(f => _classes.Matches(f.Eldest.ClassId, f.Eldest.SectionId))
            .Select(f =>
            {
                var r = StudentRowOf(f.Eldest, _classes);
                return new StudentRow
                {
                    FamilyId = r.FamilyId, FatherName = r.FatherName, Mobile = r.Mobile, Students = f.Pupils,
                    Name = r.Name, ClassName = r.ClassName, ClassSort = r.ClassSort, SectionName = r.SectionName,
                };
            })
            .OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.FamilyId)
            .ToList();
        _list = new FilterBindingList<StudentRow>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}

/// <summary>
/// Students on roll by class and section with an empty notes column, for handing out homework or assignments
/// (replaces rptStudentsListDelivery, which listed students paying up to Rs 2,500).
/// </summary>
public sealed class DeliveryListReport : ReportView
{
    private readonly ClassFilter _classes = new();
    private readonly NumericUpDown _feeUpTo = new() { Width = 90, Maximum = 1_000_000, Increment = 500, ThousandsSeparator = true, Margin = new Padding(4, 3, 0, 0) };
    private FilterBindingList<StudentRow>? _list;
    private List<StudentInfo> _students = [];

    public DeliveryListReport(ScreenContext ctx) : base(ctx)
    {
        Filters.Controls.AddRange([.. _classes.Controls, Caption("Tuition fee up to Rs"), _feeUpTo]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        _feeUpTo.ValueChanged += (_, _) => Apply();
        Hint = "Students on roll with an empty column for delivery notes. Fee up to 0 lists everyone (Access listed fees up to Rs 2,500). Print and Excel use the rows shown.";
        Grid.AddText(nameof(StudentRow.Cc), "CC", 60);
        Grid.AddText(nameof(StudentRow.Name), "Name of the student", 180, fill: true);
        Grid.AddText(nameof(StudentRow.FatherName), "Father name", 170, fill: true);
        AddPhones((nameof(StudentRow.Mobile), "Mobile"));
        Grid.AddText(nameof(StudentRow.ClassName), "Class", 90);
        Grid.AddText(nameof(StudentRow.SectionName), "Section", 80);
        AddNumber(nameof(StudentRow.Fee), "Fee", 80, sum: false);
        AddBlank("Delivery notes", 200);
    }

    public override string Title => "Delivery list";
    public override string Replaces => "rptStudentsListDelivery";
    public override string StatusText => $"{_list?.Count ?? 0:N0} students";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<StudentRow>().ToList();
            return rows.Count == 0 ? [] : [("Total students", $"{rows.Count:N0}")];
        }
    }

    protected override string FilterText =>
        string.Join(" · ", _classes.TextParts().Concat(_feeUpTo.Value > 0 ? [$"Tuition fee up to Rs {_feeUpTo.Value:N0}"] : []).DefaultIfEmpty("All classes"));

    protected override int? PrintSchoolId => _classes.SchoolId;
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups =>
        [("Class", nameof(StudentRow.ClassName)), ("Section", nameof(StudentRow.SectionName))];

    protected override async Task LoadAsync()
    {
        var (lookup, students) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            return (ClassLookup.Read(db).For(Ctx.Session), StudentInfo.Read(db, onRollOnly: true));
        });
        if (IsDisposed) return;
        _students = students;
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        var rows = _students.Where(s => _classes.Matches(s.ClassId, s.SectionId) && (_feeUpTo.Value == 0 || (s.Tuition ?? 0) <= _feeUpTo.Value))
            .Select(s => StudentRowOf(s, _classes))
            .OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ToList();
        _list = new FilterBindingList<StudentRow>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}

/// <summary>
/// Unpaid challans that carry an Other charge, e.g. the summer task (replaces rptPendingSummarTaskList), by class.
/// </summary>
public sealed class PendingTaskReport : ReportView
{
    public sealed class Row
    {
        public int Cc { get; init; }
        public string Name { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public string Period { get; init; } = "";
        public int Other { get; init; }
        public string? Mobile { get; init; }
        internal string ClassSort { get; init; } = "";
    }

    private sealed record Charge(int Cc, string Name, int? ClassId, int? SectionId, string? Mobile, string Period, DateTime? IssueDate, int Other);

    private const string AllPeriods = "All periods";
    private readonly ClassFilter _classes = new();
    private readonly ComboBox _period = Combo(150);
    private List<Charge> _charges = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public PendingTaskReport(ScreenContext ctx) : base(ctx)
    {
        Filters.Controls.AddRange([.. _classes.Controls, _period]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        _period.SelectedIndexChanged += (_, _) => Apply();
        Hint = "Unpaid challans with an amount under Other (such as the summer task), of students on roll. Print and Excel use the rows shown.";
        Grid.AddText(nameof(Row.Cc), "CC", 60);
        Grid.AddText(nameof(Row.Name), "Name", 200, fill: true);
        Grid.AddText(nameof(Row.ClassName), "Class", 90);
        Grid.AddText(nameof(Row.SectionName), "Section", 80);
        Grid.AddText(nameof(Row.Period), "Period", 80);
        AddNumber(nameof(Row.Other), "Other", 90);
        AddPhones((nameof(Row.Mobile), "Mobile"));
    }

    public override string Title => "Pending task list";
    public override string Replaces => "rptPendingSummarTaskList";
    public override string StatusText => $"{_list?.Count ?? 0:N0} challans · Rs {_list?.Cast<Row>().Sum(r => r.Other) ?? 0:N0}";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            return rows.Count == 0 ? [] : [("Students", $"{rows.Select(r => r.Cc).Distinct().Count():N0}"), ("Total pending", $"Rs {rows.Sum(r => r.Other):N0}")];
        }
    }

    protected override string FilterText =>
        string.Join(" · ", _classes.TextParts().Append(_period.SelectedItem as string ?? AllPeriods));

    protected override int? PrintSchoolId => _classes.SchoolId;
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [("Class", nameof(Row.ClassName))];

    protected override async Task LoadAsync()
    {
        var (lookup, charges) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var charges = (from c in db.FeeChallans.AsNoTracking()
                           join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                           where !c.Paid && s.OnRoll && c.Other != null && c.Other != 0
                           select new { s.Id, s.Name, s.ClassId, s.SectionId, Mobile = s.Family!.Mobile, c.Period, c.IssueDate, c.Other }).AsEnumerable()
                .Select(x => new Charge(x.Id, x.Name ?? "", x.ClassId, x.SectionId, x.Mobile, x.Period ?? "", x.IssueDate, x.Other ?? 0)).ToList();
            return (ClassLookup.Read(db).For(Ctx.Session), charges);
        });
        if (IsDisposed) return;
        _charges = charges;
        _classes.Set(lookup);
        _filling = true;
        var keep = _period.SelectedItem as string;
        _period.Items.Clear();
        _period.Items.Add(AllPeriods);
        foreach (var p in _charges.Where(c => c.Period.Length > 0).GroupBy(c => FeeChallanData.PeriodKey(c.Period))
                     .OrderByDescending(g => g.Max(c => c.IssueDate)).Select(g => g.First().Period))
            _period.Items.Add(p);
        _period.SelectedItem = keep is not null && _period.Items.Contains(keep) ? keep : AllPeriods;
        _filling = false;
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var period = _period.SelectedItem as string is { } p && p != AllPeriods ? FeeChallanData.PeriodKey(p) : null;
        var rows = _charges.Where(c => _classes.Matches(c.ClassId, c.SectionId) && (period is null || FeeChallanData.PeriodKey(c.Period) == period))
            .Select(c => new Row
            {
                Cc = c.Cc, Name = c.Name, ClassName = _classes.ClassName(c.ClassId), ClassSort = _classes.ClassSort(c.ClassId),
                SectionName = _classes.SectionName(c.SectionId), Period = c.Period, Other = c.Other, Mobile = c.Mobile,
            })
            .OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ToList();
        _list = new FilterBindingList<Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}
