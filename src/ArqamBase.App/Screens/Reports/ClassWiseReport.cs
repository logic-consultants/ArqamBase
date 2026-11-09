using ArqamBase.App.Controls;
using ArqamBase.App.Core;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Students on roll and their tuition fee by class (replaces rptClassWiseStudentsDetails, studentsFeeClasswise, rptFeeStrengthSummary):
/// each student, or the strength, fee collectable and average fee of each class.
/// </summary>
public sealed class ClassWiseReport : ReportView
{
    private readonly ClassFilter _classes = new();
    private readonly ComboBox _view = Combo(170);
    private List<StudentInfo> _students = [];
    private FilterBindingList<StudentRow>? _list;
    private bool _filling;

    public ClassWiseReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<bool>(_view, null, [(true, "Each student"), (false, "Strength summary")]);
        _filling = false;
        Filters.Controls.AddRange([.. _classes.Controls, _view]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        _view.SelectedIndexChanged += (_, _) => Apply();
        Hint = "Students on roll with the tuition fee of their current fee definition. Strength summary gives each class's strength and fee collectable. Print and Excel use the rows shown.";
    }

    public override string Title => "Class-wise students and fees";
    public override string Replaces => "rptClassWiseStudentsDetails, studentsFeeClasswise, rptFeeStrengthSummary";

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<StudentRow>().ToList() ?? [];
            return $"{rows.Sum(r => r.Students):N0} students · Rs {rows.Sum(r => r.Fee ?? 0):N0} tuition fee a month";
        }
    }

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<StudentRow>().ToList();
            if (rows.Count == 0) return [];
            var (sum, students) = (rows.Sum(r => r.Fee ?? 0), rows.Sum(r => r.Students));
            return [("Total students", $"{students:N0}"), ("Total fee", $"Rs {sum:N0}"),
                ("Average fee per student", students == 0 ? "0" : $"Rs {(decimal)sum / students:N0}")];
        }
    }

    protected override string FilterText => string.Join(" · ", _classes.TextParts().DefaultIfEmpty("All classes"));
    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<bool>(_view)
        ? [("Class", nameof(StudentRow.ClassName)), ("Section", nameof(StudentRow.SectionName))]
        : [("School", nameof(StudentRow.School))];

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
        if (_filling) return;
        var each = Selected<bool>(_view);
        var students = _students.Where(s => _classes.Matches(s.ClassId, s.SectionId)).Select(s => StudentRowOf(s, _classes));
        var rows = each
            ? students.OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ToList()
            : students.GroupBy(r => (r.School, r.ClassName, r.ClassSort))
                .Select(g => new StudentRow
                {
                    School = g.Key.School, ClassName = g.Key.ClassName, ClassSort = g.Key.ClassSort, Students = g.Count(),
                    Fee = g.Sum(r => r.Fee ?? 0), AverageFee = g.Any(r => r.Fee is not null) ? (int)Math.Round(g.Where(r => r.Fee is not null).Average(r => r.Fee!.Value)) : null,
                })
                .OrderBy(r => r.School).ThenBy(r => r.ClassSort).ToList();
        BuildColumns(each);
        _list = new FilterBindingList<StudentRow>(rows);
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
            Grid.AddText(nameof(StudentRow.Cc), "CC", 60);
            Grid.AddText(nameof(StudentRow.FamilyId), "Family", 60);
            Grid.AddText(nameof(StudentRow.Name), "Name of the student", 190, fill: true);
            Grid.AddText(nameof(StudentRow.FatherName), "Father name", 170, fill: true);
            Grid.AddText(nameof(StudentRow.ClassName), "Class", 90);
            Grid.AddText(nameof(StudentRow.SectionName), "Section", 80);
            Grid.AddText(nameof(StudentRow.AdmissionDate), "Admitted", 90, format: "dd/MM/yyyy");
            AddNumber(nameof(StudentRow.Fee), "Tuition fee", 90);
            AddPhones((nameof(StudentRow.Mobile), "Mobile"), (nameof(StudentRow.MotherMobile), "MCell"));
        }
        else
        {
            Grid.AddText(nameof(StudentRow.School), "School", 170);
            Grid.AddText(nameof(StudentRow.ClassName), "Class", 150, fill: true);
            AddNumber(nameof(StudentRow.Students), "Strength", 90);
            AddNumber(nameof(StudentRow.Fee), "Fee collectable", 120);
            AddNumber(nameof(StudentRow.AverageFee), "Average fee", 100, sum: false);
        }
    }
}
