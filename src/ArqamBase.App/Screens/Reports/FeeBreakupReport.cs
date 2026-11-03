using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// The fee students on roll are charged (replaces FeeBreakup, FeeBreakup ADM Fee, FeeBreakup Periodic): how many pay each amount,
/// or each student's fee, by department or class, from their current fee definition.
/// </summary>
public sealed class FeeBreakupReport : ReportView
{
    public sealed class Row
    {
        public int? Cc { get; init; }
        public string Name { get; init; } = "";
        public string Department { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public DateTime? AdmissionDate { get; init; }
        public int Fee { get; init; }
        public int Students { get; init; }
        public int Amount { get; init; }
        internal string ClassSort { get; init; } = "";
    }

    private sealed record Student(int Cc, string Name, int? ClassId, int? SectionId, DateTime? AdmissionDate, int Tuition, int Monthly, int Admission);

    private enum FeeKind { Tuition, Monthly, Admission }
    private enum Grouping { Department, Class, None }

    private readonly ClassFilter _classes = new();
    private readonly ComboBox _fee = Combo(190);
    private readonly ComboBox _view = Combo(130);
    private readonly ComboBox _group = Combo(150);
    private readonly KryptonCheckBox _since = new() { Text = "Admitted from", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
    private readonly KryptonDateTimePicker _sinceDate = DatePicker();
    private List<Student> _students = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public FeeBreakupReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<FeeKind>(_fee, null, [(FeeKind.Tuition, "Tuition fee"), (FeeKind.Monthly, "Monthly fee (all heads)"), (FeeKind.Admission, "Admission fee")]);
        Fill<bool>(_view, null, [(false, "By amount"), (true, "Each student")]);
        Fill<Grouping>(_group, null, [(Grouping.Department, "By department"), (Grouping.Class, "By class"), (Grouping.None, "No grouping")]);
        _filling = false;
        _sinceDate.Value = new DateTime(DateTime.Today.Year, 1, 1);
        _sinceDate.Enabled = false;
        Filters.Controls.AddRange([.. _classes.Controls, _fee, _view, _group, _since, _sinceDate]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        foreach (var c in new[] { _fee, _view, _group }) c.SelectedIndexChanged += (_, _) => Apply();
        _since.CheckedChanged += (_, _) => { _sinceDate.Enabled = _since.Checked; Apply(); };
        _sinceDate.ValueChanged += (_, _) => Apply();
        Hint = "Students on roll and their current fee definition. Monthly fee is tuition, exams/AF, registration, prospectus, A/C and other. Print and Excel use the rows shown.";
    }

    public override string Title => "Fee breakup";
    public override string Replaces => "FeeBreakup, FeeBreakup ADM Fee, FeeBreakup Periodic";

    public override string StatusText
    {
        get
        {
            var fees = Shown().Select(s => Fee(s)).ToList();
            return fees.Count == 0 ? "No students"
                : $"{fees.Count:N0} students · Rs {fees.Sum():N0} · average {fees.Average():N0} · lowest {fees.Min():N0} · highest {fees.Max():N0}";
        }
    }

    /// <summary>Of the rows shown (also after the grid's own column filters).</summary>
    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            if (rows.Count == 0) return [];
            var (sum, students) = (rows.Sum(r => r.Amount), rows.Sum(r => r.Students));
            return [("Total sum", $"Rs {sum:N0}"), ("Total students", $"{students:N0}"),
                ("Average fee per student", students == 0 ? "0" : $"Rs {(decimal)sum / students:N0}")];
        }
    }

    protected override string FilterText =>
        string.Join(" · ", _classes.TextParts().Append(_fee.Text).Concat(_since.Checked ? [$"Admitted from {_sinceDate.Value:dd/MM/yyyy}"] : []));

    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<Grouping>(_group) switch
    {
        Grouping.Department => [("Department", nameof(Row.Department))],
        Grouping.Class => [("Class", nameof(Row.ClassName))],
        _ => [],
    };

    protected override async Task LoadAsync()
    {
        var (lookup, students) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var lookup = ClassLookup.Read(db).For(Ctx.Session);
            var onRoll = db.Students.AsNoTracking().Where(s => s.OnRoll)
                .Select(s => new { s.Id, s.Name, s.ClassId, s.SectionId, s.AdmissionDate }).ToList();
            // The current definition is the student's latest (as the ledger and periodic fee update use).
            var definitions = db.FeeDefinitions.AsNoTracking().Where(d => d.StudentId != null).ToList()
                .GroupBy(d => d.StudentId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(d => d.Id)!);
            var students = onRoll.Where(s => definitions.ContainsKey(s.Id)).Select(s =>
            {
                var d = definitions[s.Id];
                var monthly = (d.TuitionFee ?? 0) + (d.ExamsFee ?? 0) + (d.RegistrationFee ?? 0) + (d.ProspectusFee ?? 0) + (d.AcFee ?? 0) + (d.Other ?? 0);
                return new Student(s.Id, s.Name ?? "", s.ClassId, s.SectionId, s.AdmissionDate, d.TuitionFee ?? 0, monthly, d.AdmissionFee ?? 0);
            }).ToList();
            return (lookup, students);
        });
        if (IsDisposed) return;
        _students = students;
        _classes.Set(lookup);
        Apply();
    }

    private int Fee(Student s) => Selected<FeeKind>(_fee) switch
    {
        FeeKind.Monthly => s.Monthly,
        FeeKind.Admission => s.Admission,
        _ => s.Tuition,
    };

    private IEnumerable<Student> Shown() => _students.Where(s =>
        _classes.Matches(s.ClassId, s.SectionId) && (!_since.Checked || s.AdmissionDate >= _sinceDate.Value.Date));

    private void Apply()
    {
        if (_filling) return;
        var grouping = Selected<Grouping>(_group);
        var each = Selected<bool>(_view);
        string GroupOf(Student s) => grouping switch
        {
            Grouping.Department => _classes.DepartmentName(s.ClassId),
            Grouping.Class => _classes.ClassName(s.ClassId),
            _ => "",
        };
        string SortOf(Student s) => grouping == Grouping.Class ? _classes.ClassSort(s.ClassId) : GroupOf(s);

        List<Row> rows;
        if (each)
            rows = Shown().Select(s => new Row
                {
                    Cc = s.Cc, Name = s.Name, Department = _classes.DepartmentName(s.ClassId), ClassName = _classes.ClassName(s.ClassId),
                    ClassSort = _classes.ClassSort(s.ClassId), SectionName = _classes.SectionName(s.SectionId), AdmissionDate = s.AdmissionDate,
                    Fee = Fee(s), Students = 1, Amount = Fee(s),
                })
                .OrderBy(r => grouping == Grouping.Department ? r.Department : "").ThenBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name)
                .ToList();
        else
            rows = Shown().GroupBy(s => (Group: GroupOf(s), Fee: Fee(s)))
                .Select(g => new Row
                {
                    Department = grouping == Grouping.Department ? g.Key.Group : "", ClassName = grouping == Grouping.Class ? g.Key.Group : "",
                    ClassSort = SortOf(g.First()), Fee = g.Key.Fee, Students = g.Count(), Amount = g.Key.Fee * g.Count(),
                })
                .OrderBy(r => r.ClassSort).ThenBy(r => r.Fee).ToList();

        BuildColumns(each);
        Grid.Columns[nameof(Row.Department)]!.Visible = grouping == Grouping.Department;
        if (!each) Grid.Columns[nameof(Row.ClassName)]!.Visible = grouping == Grouping.Class;
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
        Grid.AddText(nameof(Row.Department), "Department", 130);
        if (each)
        {
            Grid.AddText(nameof(Row.Cc), "CC", 60);
            Grid.AddText(nameof(Row.Name), "Name", 200, fill: true);
            Grid.AddText(nameof(Row.ClassName), "Class", 100);
            Grid.AddText(nameof(Row.SectionName), "Section", 90);
            Grid.AddText(nameof(Row.AdmissionDate), "Admitted", 90, format: "dd/MM/yyyy");
            AddNumber(nameof(Row.Fee), "Fee", 90);
        }
        else
        {
            Grid.AddText(nameof(Row.ClassName), "Class", 110);
            AddNumber(nameof(Row.Fee), "Fee", 100, sum: false);
            AddNumber(nameof(Row.Students), "Students", 100);
            AddNumber(nameof(Row.Amount), "Amount", 120);
        }
    }
}
