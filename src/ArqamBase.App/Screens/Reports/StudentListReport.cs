using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Lists of students by class and section (replaces Students List, Students Detail Address and Ph, rptPolioList, WeeklyTestReport):
/// names and mobile, all phones and address, a list for the polio team, or a weekly test sheet with empty marks columns.
/// </summary>
public sealed class StudentListReport : ReportView
{
    private enum Columns { Mobile, Phones, Polio, WeeklyTest }
    private enum Grouping { ClassSection, Class, Family, None }

    private readonly ClassFilter _classes = new();
    private readonly ComboBox _layout = Combo(190);
    private readonly ComboBox _group = Combo(170);
    private readonly KryptonCheckBox _left = new() { Text = "Include left students", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
    private List<StudentInfo> _students = [];
    private FilterBindingList<StudentRow>? _list;
    private bool _filling;

    public StudentListReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<Columns>(_layout, null, [(Columns.Mobile, "Names and mobile"), (Columns.Phones, "Phones and address"),
            (Columns.Polio, "List for polio team"), (Columns.WeeklyTest, "Weekly test sheet")]);
        Fill<Grouping>(_group, null, [(Grouping.ClassSection, "By class and section"), (Grouping.Class, "By class"),
            (Grouping.Family, "By family"), (Grouping.None, "No grouping")]);
        _filling = false;
        Filters.Controls.AddRange([.. _classes.Controls, _layout, _group, _left]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        _layout.SelectedIndexChanged += (_, _) => Apply();
        _group.SelectedIndexChanged += (_, _) => Apply();
        _left.CheckedChanged += (_, _) => Apply();
        Hint = "Students on roll. The weekly test sheet has empty columns to write the marks in. Print and Excel use the rows shown.";
    }

    public override string Title => "Student list";
    public override string Replaces => "Students List, Students Detail Address and Ph, rptPolioList, WeeklyTestReport";

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<StudentRow>().ToList() ?? [];
            return $"{rows.Count:N0} students · {rows.Select(r => r.FamilyId).Distinct().Count():N0} families";
        }
    }

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<StudentRow>().ToList();
            return rows.Count == 0 ? [] : [("Total students", $"{rows.Count:N0}"), ("Families", $"{rows.Select(r => r.FamilyId).Distinct().Count():N0}")];
        }
    }

    protected override string FilterText =>
        string.Join(" · ", _classes.TextParts().Concat(_left.Checked ? ["Including left students"] : []).DefaultIfEmpty("All classes"));

    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<Grouping>(_group) switch
    {
        Grouping.ClassSection => [("Class", nameof(StudentRow.ClassName)), ("Section", nameof(StudentRow.SectionName))],
        Grouping.Class => [("Class", nameof(StudentRow.ClassName))],
        Grouping.Family => [("Family", nameof(StudentRow.Family))],
        _ => [],
    };

    protected override async Task LoadAsync()
    {
        var (lookup, students) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            return (ClassLookup.Read(db).For(Ctx.Session), StudentInfo.Read(db, onRollOnly: false));
        });
        if (IsDisposed) return;
        _students = students;
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var grouping = Selected<Grouping>(_group);
        var rows = _students.Where(s => (_left.Checked || s.OnRoll) && _classes.Matches(s.ClassId, s.SectionId))
            .Select(s => StudentRowOf(s, _classes));
        rows = grouping == Grouping.Family
            ? rows.OrderBy(r => r.FamilyId).ThenBy(r => r.ClassSort).ThenBy(r => r.Name)
            : rows.OrderBy(r => grouping == Grouping.None ? "" : r.ClassSort).ThenBy(r => grouping == Grouping.ClassSection ? r.SectionName : "")
                .ThenBy(r => r.Name);
        BuildColumns(Selected<Columns>(_layout));
        Grid.Columns[nameof(StudentRow.Status)]!.Visible = _left.Checked;
        _list = new FilterBindingList<StudentRow>(rows.ToList());
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    private Columns? _columnsFor;

    private void BuildColumns(Columns layout)
    {
        if (_columnsFor == layout) return;
        _columnsFor = layout;
        Grid.ResetColumns();
        SumProperties.Clear();
        Grid.AddText(nameof(StudentRow.Cc), "CC", 60);
        if (layout != Columns.Polio) Grid.AddText(nameof(StudentRow.FamilyId), "Family", 60);
        Grid.AddText(nameof(StudentRow.Name), "Name of the student", 190, fill: layout != Columns.WeeklyTest);
        Grid.AddText(nameof(StudentRow.FatherName), "Father name", 170, fill: layout != Columns.WeeklyTest);
        Grid.AddText(nameof(StudentRow.ClassName), "Class", 90);
        Grid.AddText(nameof(StudentRow.SectionName), "Section", 80);
        Grid.AddText(nameof(StudentRow.Family), "Family name", 160).Visible = false;
        switch (layout)
        {
            case Columns.Mobile:
                AddPhones((nameof(StudentRow.Mobile), "Mobile"));
                break;
            case Columns.Phones:
                AddPhones((nameof(StudentRow.Mobile), "Mobile"), (nameof(StudentRow.MotherMobile), "MCell"),
                    (nameof(StudentRow.PhoneHome), "Ph. home"), (nameof(StudentRow.PhoneOffice), "Ph. office"));
                Grid.AddText(nameof(StudentRow.Address), "Address", 220, fill: true);
                break;
            case Columns.WeeklyTest:
                foreach (var subject in new[] { "English", "Urdu", "Math", "Sci./Phy", "S.St.", "I.S.", "Total" }) AddBlank(subject, 60);
                AddBlank("Remarks", 140);
                break;
        }
        Grid.AddText(nameof(StudentRow.Status), "Status", 80);
    }
}
