using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Students admitted or withdrawn in a date range (replaces ADM_WD Register, Admission Report By Month, Withdrawal Report By Month,
/// rptDropOutReprot): by month or class, or the admission and withdrawal register by DS#.
/// </summary>
public sealed class AdmissionsReport : ReportView
{
    private enum Kind { Admissions, Withdrawals, Register }
    private enum Grouping { Month, Class, None }

    private readonly KryptonDateTimePicker _from = DatePicker();
    private readonly KryptonDateTimePicker _to = DatePicker();
    private readonly ClassFilter _classes = new();
    private readonly ComboBox _kind = Combo(190);
    private readonly ComboBox _group = Combo(130);
    private List<StudentInfo> _students = [];
    private FilterBindingList<StudentRow>? _list;
    private bool _filling;

    public AdmissionsReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<Kind>(_kind, null, [(Kind.Admissions, "Admissions"), (Kind.Withdrawals, "Withdrawals and dropouts"), (Kind.Register, "Admission withdrawal register")]);
        Fill<Grouping>(_group, null, [(Grouping.Month, "By month"), (Grouping.Class, "By class"), (Grouping.None, "No grouping")]);
        _filling = false;
        _from.Value = new DateTime(DateTime.Today.Year, 1, 1);
        _to.Value = DateTime.Today;
        Filters.Controls.AddRange([Caption("From"), _from, Caption("To"), _to, .. _classes.Controls, _kind, _group]);
        AddShowButton();
        _from.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _to.Value = _from.Value; Apply(); };
        _to.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _from.Value = _to.Value; Apply(); };
        _classes.Changed += (_, _) => Apply();
        _kind.SelectedIndexChanged += (_, _) => Apply();
        _group.SelectedIndexChanged += (_, _) => Apply();
        Hint = "Admissions by admission date, withdrawals by leaving date, in the date range. The register lists admissions by DS# with home address and leaving date. Print and Excel use the rows shown.";
    }

    public override string Title => "Admissions and withdrawals";
    public override string Replaces => "ADM_WD Register, Admission Report By Month, Withdrawal Report By Month, rptDropOutReprot";

    public override string StatusText => $"{_list?.Count ?? 0:N0} students";

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<StudentRow>().ToList();
            if (rows.Count == 0) return [];
            var fees = rows.Where(r => r.Fee is not null).Select(r => r.Fee!.Value).ToList();
            var label = Selected<Kind>(_kind) == Kind.Withdrawals ? "Total withdrawals" : "Total admissions";
            var lines = new List<(string, string)> { (label, $"{rows.Count:N0}") };
            if (Selected<Kind>(_kind) != Kind.Withdrawals) lines.Add(("Left since", $"{rows.Count(r => r.Status != "Active"):N0}"));
            lines.Add(("Average fee per student", fees.Count == 0 ? "0" : $"Rs {fees.Average():N0}"));
            return lines;
        }
    }

    protected override string FilterText =>
        string.Join(" · ", new[] { $"{_from.Value:dd/MM/yyyy} to {_to.Value:dd/MM/yyyy}" }.Concat(_classes.TextParts()));

    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<Grouping>(_group) switch
    {
        Grouping.Month => [("Month", nameof(StudentRow.Month))],
        Grouping.Class => [("Class", nameof(StudentRow.ClassName))],
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
        var kind = Selected<Kind>(_kind);
        var (first, last) = (_from.Value.Date, _to.Value.Date);
        DateTime? DateOf(StudentInfo s) => kind == Kind.Withdrawals ? s.LeavingDate : s.AdmissionDate;
        var rows = _students
            .Where(s => (kind != Kind.Withdrawals || !s.OnRoll) && DateOf(s) is { } d && d.Date >= first && d.Date <= last
                        && _classes.Matches(s.ClassId, s.SectionId))
            .Select(s =>
            {
                var r = StudentRowOf(s, _classes);
                var date = DateOf(s)!.Value;
                return new StudentRow
                {
                    Cc = r.Cc, DsNo = r.DsNo, FamilyId = r.FamilyId, Family = r.Family, Name = r.Name, FatherName = r.FatherName,
                    ClassName = r.ClassName, ClassSort = r.ClassSort, SectionName = r.SectionName, BirthDate = r.BirthDate,
                    AdmissionDate = r.AdmissionDate, LeavingDate = r.LeavingDate, Status = r.Status, Reason = r.Reason, Fee = r.Fee,
                    Caste = r.Caste, Profession = r.Profession, Remarks = r.Remarks,
                    Address = string.Join("  PH: ", new[] { r.Address, NumberFormat.Phone(r.PhoneHome ?? r.Mobile) }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    Month = date.ToString("MMMM yyyy"), SortDate = date,
                };
            });
        rows = Selected<Grouping>(_group) switch
        {
            Grouping.Class => rows.OrderBy(r => r.ClassSort).ThenBy(r => r.SortDate).ThenBy(r => r.Cc),
            _ when kind == Kind.Register => rows.OrderBy(r => r.SortDate!.Value.Year).ThenBy(r => r.SortDate!.Value.Month).ThenBy(r => r.DsNo ?? int.MaxValue).ThenBy(r => r.Cc),
            _ => rows.OrderBy(r => r.SortDate).ThenBy(r => r.Cc),
        };
        BuildColumns(kind);
        _list = new FilterBindingList<StudentRow>(rows.ToList());
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    private Kind? _columnsFor;

    private void BuildColumns(Kind kind)
    {
        if (_columnsFor == kind) return;
        _columnsFor = kind;
        Grid.ResetColumns();
        SumProperties.Clear();
        Grid.AddText(nameof(StudentRow.Month), "Month", 110).Visible = false;
        if (kind == Kind.Register)
        {
            Grid.AddText(nameof(StudentRow.DsNo), "DS#", 60);
            Grid.AddText(nameof(StudentRow.AdmissionDate), "Date", 85, format: "dd/MM/yyyy");
            Grid.AddText(nameof(StudentRow.Cc), "CC", 60);
            Grid.AddText(nameof(StudentRow.Name), "Name of the student", 170, fill: true);
            Grid.AddText(nameof(StudentRow.FatherName), "Father name", 160, fill: true);
            Grid.AddText(nameof(StudentRow.BirthDate), "Birth date", 85, format: "dd/MM/yyyy");
            Grid.AddText(nameof(StudentRow.Caste), "Caste", 90);
            Grid.AddText(nameof(StudentRow.Profession), "Profession", 100);
            Grid.AddText(nameof(StudentRow.Address), "Home address", 220, fill: true);
            Grid.AddText(nameof(StudentRow.ClassName), "Class", 80);
            Grid.AddText(nameof(StudentRow.LeavingDate), "Left on", 85, format: "dd/MM/yyyy");
            Grid.AddText(nameof(StudentRow.Remarks), "Remarks", 140);
            return;
        }
        Grid.AddText(nameof(StudentRow.Cc), "CC", 60);
        Grid.AddText(nameof(StudentRow.FamilyId), "Family", 60);
        Grid.AddText(nameof(StudentRow.Name), "Name of the student", 180, fill: true);
        Grid.AddText(nameof(StudentRow.FatherName), "Father name", 170, fill: true);
        Grid.AddText(nameof(StudentRow.ClassName), "Class", 90);
        Grid.AddText(nameof(StudentRow.SectionName), "Section", 80);
        Grid.AddText(nameof(StudentRow.AdmissionDate), "Admitted", 85, format: "dd/MM/yyyy");
        if (kind == Kind.Withdrawals)
        {
            Grid.AddText(nameof(StudentRow.LeavingDate), "Left on", 85, format: "dd/MM/yyyy");
            Grid.AddText(nameof(StudentRow.Status), "Status", 80);
            Grid.AddText(nameof(StudentRow.Reason), "Reason", 180, fill: true);
        }
        else
            Grid.AddText(nameof(StudentRow.Status), "Status", 80);
        AddNumber(nameof(StudentRow.Fee), "Tuition fee", 90, sum: false);
        if (kind == Kind.Admissions) Grid.AddText(nameof(StudentRow.Remarks), "Remarks", 160);
    }
}
