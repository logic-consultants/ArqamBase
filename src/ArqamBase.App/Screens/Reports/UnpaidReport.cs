using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Unpaid fees (replaces Unpaid List, Unpaid Detail, Unpaid_Detail_family, Unpaid_Detail_ClassPDF, rpt_TopDefaulters):
/// one row per student or per challan, grouped by class and section, family or department, with the family's phone numbers.
/// </summary>
public sealed class UnpaidReport : ReportView
{
    public sealed class Row
    {
        public int Cc { get; init; }
        public int? FamilyId { get; init; }
        public string Family { get; init; } = "";
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public string Department { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public string Status { get; init; } = "";
        public string Periods { get; init; } = "";
        public int Months { get; init; }
        public string Period { get; init; } = "";
        public DateTime? IssueDate { get; init; }
        public DateTime? DueDate { get; init; }
        public int Tuition { get; init; }
        public int Exams { get; init; }
        public int Registration { get; init; }
        public int Admission { get; init; }
        public int Prospectus { get; init; }
        public int Fine { get; init; }
        public int Other { get; init; }
        public int Total { get; init; }
        public string? Mobile { get; init; }
        public string? MotherMobile { get; init; }
        public string? PhoneHome { get; init; }
        public string? EmergencyNo { get; init; }
        internal string? WhatsApp { get; init; }
        internal string? Email { get; init; }
        internal string ClassSort { get; init; } = "";
    }

    private sealed record Charge(int Cc, string Name, int? FamilyId, string Father, bool OnRoll, string Status, int? ClassId, int? SectionId,
        string? Mobile, string? MotherMobile, string? PhoneHome, string? EmergencyNo, string Period, DateTime? IssueDate, DateTime? DueDate,
        int Tuition, int Exams, int Registration, int Admission, int Prospectus, int Fine, int Other, int Total, string? WhatsApp, string? Email);

    private enum Grouping { ClassSection, Family, Department, None }

    private const string AllPeriods = "All unpaid challans";
    private readonly ComboBox _school = Combo(170);
    private readonly ComboBox _class = Combo(130);
    private readonly ComboBox _section = Combo(130);
    private readonly ComboBox _period = Combo(170);
    private readonly ComboBox _group = Combo(170);
    private readonly ComboBox _detail = Combo(150);
    private readonly NumericUpDown _top = new() { Width = 70, Maximum = 10000, Margin = new Padding(4, 3, 0, 0) };
    private readonly NumericUpDown _minimum = new() { Width = 90, Maximum = 10_000_000, Increment = 1000, ThousandsSeparator = true, Margin = new Padding(4, 3, 0, 0) };
    private readonly KryptonCheckBox _left = new() { Text = "Include left students", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
    private List<Charge> _charges = [];
    private List<(int Id, int? DepartmentId, string Name, string Sort)> _classes = [];
    private Dictionary<int, (int? SchoolId, string Name)> _departments = [];
    private List<(int Id, int? ClassId, string Name)> _sections = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public UnpaidReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<Grouping>(_group, null, [(Grouping.ClassSection, "By class and section"), (Grouping.Family, "By family"),
            (Grouping.Department, "By department"), (Grouping.None, "No grouping")]);
        Fill<bool>(_detail, null, [(false, "Each student"), (true, "Each challan")]);
        _filling = false;
        Filters.Controls.AddRange([_school, _class, _section, _period, _group, _detail, Caption("Top"), _top, Caption("At least Rs"), _minimum, _left]);
        AddShowButton();
        foreach (var c in new Control[] { _section, _period, _group, _detail })
            ((ComboBox)c).SelectedIndexChanged += (_, _) => Apply();
        _school.SelectedIndexChanged += (_, _) => { if (!_filling) { FillClasses(); Apply(); } };
        _class.SelectedIndexChanged += (_, _) => { if (!_filling) { FillSections(); Apply(); } };
        _top.ValueChanged += (_, _) => Apply();
        _minimum.ValueChanged += (_, _) => Apply();
        _left.CheckedChanged += (_, _) => Apply();
        Grid.CellMouseDown += (_, e) =>
        {
            // Right-click picks the row, unless it is one of several already selected.
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !Grid.Rows[e.RowIndex].Selected)
                Grid.CurrentCell = Grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        };
        Grid.CellContextMenuStripNeeded += (_, e) => { if (e.RowIndex >= 0) e.ContextMenuStrip = RowMenu(); };
        Hint ="Unpaid challans of students on roll. Top shows the biggest defaulters (0 = everyone). Right-click a student to send a reminder, take payment or print the challan. Print and Excel use the rows shown.";
    }

    public override string Title => "Unpaid list";
    public override string Replaces => "Unpaid List, Unpaid Detail, Unpaid_Detail_family, Unpaid_Detail_ClassPDF, rpt_TopDefaulters";

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<Row>().ToList() ?? [];
            var students = rows.Select(r => r.Cc).Distinct().Count();
            return $"{students:N0} students · {rows.Sum(r => r.Months):N0} challans · Rs {rows.Sum(r => r.Total):N0} unpaid";
        }
    }

    protected override string FilterText
    {
        get
        {
            var parts = new List<string>();
            if (Selected<int?>(_school) is not null) parts.Add(SelectedText(_school));
            if (Selected<int?>(_class) is not null) parts.Add(SelectedText(_class));
            if (Selected<int?>(_section) is not null) parts.Add(SelectedText(_section));
            parts.Add(_period.SelectedItem as string ?? AllPeriods);
            if (_top.Value > 0) parts.Add($"Top {_top.Value:N0}");
            if (_minimum.Value > 0) parts.Add($"At least Rs {_minimum.Value:N0}");
            if (_left.Checked) parts.Add("Including left students");
            return string.Join(" · ", parts);
        }
    }

    protected override int? PrintSchoolId => Selected<int?>(_school);

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<Grouping>(_group) switch
    {
        Grouping.ClassSection => [("Class", nameof(Row.ClassName)), ("Section", nameof(Row.SectionName))],
        Grouping.Family => [("Family", nameof(Row.Family))],
        Grouping.Department => [("Department", nameof(Row.Department))],
        _ => [],
    };

    protected override async Task LoadAsync()
    {
        var data = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var classes = db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).AsEnumerable()
                .Select(c => (c.Id, c.DepartmentId, c.Name ?? "", c.SortName ?? c.Name ?? "")).ToList();
            var departments = db.Departments.AsNoTracking().AsEnumerable().ToDictionary(d => d.Id, d => (d.SchoolId, d.Name ?? ""));
            var sections = ClassSections.Read(db);
            var schools = db.Schools.AsNoTracking().OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList();
            var charges = (from c in db.FeeChallans.AsNoTracking()
                           join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                           where !c.Paid
                           select new { c, s, s.Family }).AsEnumerable()
                .Select(x => new Charge(x.s.Id, x.s.Name ?? "", x.s.FamilyId, x.Family?.FatherName ?? "", x.s.OnRoll, x.s.StatusText, x.s.ClassId, x.s.SectionId,
                    x.Family?.Mobile, x.Family?.MotherMobile, x.Family?.PhoneHome is { Length: > 0 } home ? home : x.Family?.PhoneOffice, x.Family?.EmergencyNo,
                    x.c.Period ?? "", x.c.IssueDate, x.c.DueDate, x.c.TuitionFee ?? 0, x.c.ExamsFee ?? 0, x.c.RegistrationFee ?? 0, x.c.AdmissionFee ?? 0,
                    x.c.ProspectusFee ?? 0, x.c.Fine ?? 0, x.c.Other ?? 0, x.c.Total, x.s.WhatsApp, x.s.Email))
                .ToList();
            return (classes, departments, sections, schools, charges);
        });
        if (IsDisposed) return;
        _classes = data.classes;
        _departments = data.departments;
        _sections = data.sections;
        _charges = data.charges;
        _filling = true;
        Fill(_school, "All schools", data.schools);
        var keep = _period.SelectedItem as string;
        _period.Items.Clear();
        _period.Items.Add(AllPeriods);
        foreach (var p in _charges.Where(c => c.Period.Length > 0).GroupBy(c => FeeChallanData.PeriodKey(c.Period))
                     .OrderByDescending(g => g.Max(c => c.IssueDate)).Select(g => g.First().Period))
            _period.Items.Add(p);
        _period.SelectedItem = keep is not null && _period.Items.Contains(keep) ? keep : AllPeriods;
        _filling = false;
        FillClasses();
        Apply();
    }

    private void FillClasses()
    {
        _filling = true;
        var school = Selected<int?>(_school);
        Fill(_class, "All classes", _classes
            .Where(c => school is null || c.DepartmentId is int d && _departments.GetValueOrDefault(d).SchoolId == school)
            .Select(c => ((int?)c.Id, $"Class: {c.Name}")));
        _filling = false;
        FillSections();
    }

    private void FillSections()
    {
        _filling = true;
        var cls = Selected<int?>(_class);
        Fill(_section, "All sections", _sections.Where(s => cls is null || s.ClassId == cls).DistinctBy(s => s.Id).Select(s => ((int?)s.Id, $"Section: {s.Name}")));
        _filling = false;
    }

    /// <summary>Applies the filters to the loaded challans and shows the rows (no database call).</summary>
    private void Apply()
    {
        if (_filling) return;
        var school = Selected<int?>(_school);
        var cls = Selected<int?>(_class);
        var section = Selected<int?>(_section);
        var period = _period.SelectedItem as string is { } p && p != AllPeriods ? FeeChallanData.PeriodKey(p) : null;
        var classInfo = _classes.ToDictionary(c => c.Id);
        var sectionName = _sections.DistinctBy(s => s.Id).ToDictionary(s => s.Id, s => s.Name);

        var charges = _charges.Where(c => (_left.Checked || c.OnRoll)
                                          && (cls is null || c.ClassId == cls) && (section is null || c.SectionId == section)
                                          && (period is null || FeeChallanData.PeriodKey(c.Period) == period)
                                          && (school is null || DepartmentOf(c.ClassId) is int d && _departments.GetValueOrDefault(d).SchoolId == school))
            .ToList();

        // Top defaulters and the minimum are on the student's total.
        var byStudent = charges.GroupBy(c => c.Cc).Where(g => g.Sum(c => c.Total) >= _minimum.Value).ToList();
        if (_top.Value > 0)
            byStudent = byStudent.OrderByDescending(g => g.Sum(c => c.Total)).Take((int)_top.Value).ToList();

        Row Make(Charge f, IReadOnlyList<Charge> items, bool each)
        {
            var info = f.ClassId is int ci && classInfo.TryGetValue(ci, out var c) ? c : default;
            var dept = info.DepartmentId is int d && _departments.TryGetValue(d, out var dn) ? dn.Name : "";
            var single = each ? items[0] : null;
            return new Row
            {
                Cc = f.Cc, FamilyId = f.FamilyId, Family = $"{f.FamilyId} · {f.Father}", Name = f.Name, FatherName = f.Father,
                Department = dept, ClassName = info.Name ?? "", ClassSort = info.Sort ?? "",
                SectionName = f.SectionId is int si && sectionName.TryGetValue(si, out var sn) ? sn : "", Status = f.Status,
                Periods = string.Join(", ", items.Select(x => x.Period)), Months = items.Count,
                Period = single?.Period ?? "", IssueDate = single?.IssueDate, DueDate = single?.DueDate,
                Tuition = items.Sum(x => x.Tuition), Exams = items.Sum(x => x.Exams), Registration = items.Sum(x => x.Registration),
                Admission = items.Sum(x => x.Admission), Prospectus = items.Sum(x => x.Prospectus), Fine = items.Sum(x => x.Fine),
                Other = items.Sum(x => x.Other), Total = items.Sum(x => x.Total),
                Mobile = f.Mobile, MotherMobile = f.MotherMobile, PhoneHome = f.PhoneHome, EmergencyNo = f.EmergencyNo, WhatsApp = f.WhatsApp, Email = f.Email,
            };
        }

        var each = Selected<bool>(_detail);
        var rows = byStudent.SelectMany(g =>
        {
            var items = g.OrderBy(c => c.IssueDate).ToList();
            return each ? items.Select(c => Make(c, [c], true)) : new[] { Make(items[0], items, false) };
        });
        var grouping = Selected<Grouping>(_group);
        var totals = byStudent.ToDictionary(g => g.Key, g => g.Sum(c => c.Total));
        rows = grouping switch
        {
            Grouping.Family => rows.OrderBy(r => r.FamilyId).ThenBy(r => r.ClassSort).ThenBy(r => r.Name).ThenBy(r => r.IssueDate),
            Grouping.Department => rows.OrderBy(r => r.Department).ThenBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ThenBy(r => r.IssueDate),
            Grouping.None when _top.Value > 0 => rows.OrderByDescending(r => totals[r.Cc]).ThenBy(r => r.Cc).ThenBy(r => r.IssueDate),
            _ => rows.OrderBy(r => r.ClassSort).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ThenBy(r => r.IssueDate),
        };

        BuildColumns(each);
        Grid.Columns[nameof(Row.Status)]!.Visible = _left.Checked;
        _list = new FilterBindingList<Row>(rows.ToList());
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    // ----- Row menu: reminders, take payment, print challan -----

    private Row? CurrentRow => Grid.CurrentRow?.DataBoundItem as Row;

    /// <summary>The selected students (each once), or the current one.</summary>
    private List<Row> SelectedStudents()
    {
        var rows = Grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<Row>().ToList();
        if (rows.Count == 0 && CurrentRow is { } current) rows.Add(current);
        return rows.GroupBy(r => r.Cc).Select(g => g.First()).ToList();
    }

    private ContextMenuStrip? RowMenu()
    {
        if (CurrentRow is not { } row) return null;
        var students = SelectedStudents();
        var phone = WhatsAppNumber(row);
        var menu = new ContextMenuStrip();
        menu.Items.Add(phone is null ? "Send reminder WhatsApp (no mobile number)" : $"Send reminder WhatsApp to {NumberFormat.Phone(row.WhatsApp ?? row.Mobile)}",
            Icons.Small(Icons.Chat), (_, _) => SendWhatsApp(row, phone!)).Enabled = phone is not null;
        var email = row.Email?.Trim();
        menu.Items.Add(string.IsNullOrEmpty(email) ? "Send reminder email (no email address)" : $"Send reminder email to {email}",
            Icons.Small(Icons.Mail), (_, _) => SendEmail(row, email!)).Enabled = !string.IsNullOrEmpty(email);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Take payment ({row.Cc} {row.Name})", Icons.Small(Icons.Ledger), (_, _) => Ctx.OpenScreenAt("ledger", row.Cc));
        menu.Items.Add(students.Count > 1 ? $"Print challans ({students.Count} students)" : "Print challan",
            Icons.Small(Icons.Print), (_, _) => PrintChallans(students));
        return menu;
    }

    /// <summary>The student's WhatsApp number, else the father's mobile, as 92 and the last 10 digits (as Access sent them).</summary>
    private static string? WhatsAppNumber(Row row)
    {
        var digits = NumberFormat.Digits(row.WhatsApp is { Length: > 0 } wa ? wa : row.Mobile);
        return digits.Length >= 10 ? "92" + digits[^10..] : null;
    }

    /// <summary>What the student owes in the rows shown (in "Each challan" all their rows).</summary>
    private (int Months, int Total, string Periods) Owing(Row row)
    {
        var rows = _list?.Cast<Row>().Where(r => r.Cc == row.Cc).ToList() ?? [row];
        return (rows.Sum(r => r.Months), rows.Sum(r => r.Total), string.Join(", ", rows.Select(r => r.Period.Length > 0 ? r.Period : r.Periods)));
    }

    /// <summary>The reminder of the Access unpaid WhatsApp sender, with the amount and periods added.</summary>
    private string Reminder(Row row, bool whatsApp)
    {
        var (months, total, periods) = Owing(row);
        string B(string s) => whatsApp ? $"*{s}*" : s;
        return $"Dear Mr. {B(row.FatherName)}\n" +
               $"This is a gentle reminder that your child {B(row.Name + "'s")} school fee is still pending for {B($"{months} {(months == 1 ? "month" : "months")} ({periods})")}, " +
               $"Rs {B(total.ToString("N0"))} in all.\n" +
               "Kindly deposit the fee at your earliest convenience to avoid any inconvenience.\n\n" +
               "Regards,\nSchool Administration";
    }

    /// <summary>Opens WhatsApp with the reminder written; the user checks it and presses Send there.</summary>
    private void SendWhatsApp(Row row, string phone) =>
        Open($"https://wa.me/{phone}?text={Uri.EscapeDataString(Reminder(row, whatsApp: true))}");

    /// <summary>Opens the email program with the reminder written.</summary>
    private void SendEmail(Row row, string email) =>
        Open($"mailto:{email}?subject={Uri.EscapeDataString($"School fee reminder: {row.Name} ({row.Cc})")}&body={Uri.EscapeDataString(Reminder(row, whatsApp: false))}");

    private void Open(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Program.ShowError("Could not open WhatsApp or the email program on this computer.", ex);
        }
    }

    private void PrintChallans(List<Row> students)
    {
        List<Printing.ChallanPrint> challans;
        Printing.SchoolPrint school;
        try
        {
            UseWaitCursor = true;
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            challans = FeeChallanData.Load(db, students.Select(s => s.Cc).ToList());
            school = Printing.SchoolPrint.From(db.Campuses.AsNoTracking().OrderBy(c => c.Id).FirstOrDefault());
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the challans.", ex); return; }
        finally { UseWaitCursor = false; }
        if (challans.Count == 0) { Inform("These students have no unpaid challans now."); return; }
        var title = challans.Count == 1 ? $"Fee challan · {challans[0].Cc} {challans[0].Name}" : $"Fee challans · {challans.Count} students";
        Shell.PrintPreviewForm.Show(this, () => Printing.Challans.Create(challans, school, Ctx.Config.ChallanBank, Ctx.Config.ChallanLateFee),
            title, challans.Count == 1 ? $"Fee challan {challans[0].Cc} {challans[0].Name}" : $"Fee challans {DateTime.Today:yyyy-MM-dd}");
    }

    private int? DepartmentOf(int? classId) => classId is int id ? _classes.FirstOrDefault(c => c.Id == id).DepartmentId : null;

    private bool? _columnsForEach;

    private void BuildColumns(bool each)
    {
        if (_columnsForEach == each) return;
        _columnsForEach = each;
        Grid.ResetColumns();
        SumProperties.Clear();
        Grid.AddText(nameof(Row.Cc), "CC", 60);
        Grid.AddText(nameof(Row.FamilyId), "Family", 60);
        Grid.AddText(nameof(Row.Name), "Name", 170, fill: true);
        Grid.AddText(nameof(Row.FatherName), "Father name", 160, fill: true);
        Grid.AddText(nameof(Row.Department), "Department", 110).Visible = false;
        Grid.AddText(nameof(Row.Family), "Family name", 160).Visible = false;
        Grid.AddText(nameof(Row.ClassName), "Class", 90);
        Grid.AddText(nameof(Row.SectionName), "Section", 80);
        if (each)
        {
            Grid.AddText(nameof(Row.Period), "Period", 70);
            Grid.AddText(nameof(Row.DueDate), "Due", 85, format: "dd/MM/yyyy");
            foreach (var (p, h) in new[] { (nameof(Row.Tuition), "Tuition"), (nameof(Row.Exams), "Exams/AF"), (nameof(Row.Registration), "Reg."),
                         (nameof(Row.Admission), "Adm."), (nameof(Row.Prospectus), "Prosp."), (nameof(Row.Fine), "Fine"), (nameof(Row.Other), "Other") })
                Number(p, h, 70);
        }
        else
        {
            Grid.AddText(nameof(Row.Periods), "Unpaid periods", 170, fill: true);
            Number(nameof(Row.Months), "Months", 60);
        }
        Number(nameof(Row.Total), "Total", 85);
        foreach (var (p, h) in new[] { (nameof(Row.Mobile), "Mobile"), (nameof(Row.MotherMobile), "MCell"), (nameof(Row.PhoneHome), "Phone"), (nameof(Row.EmergencyNo), "Emergency no.") })
        {
            Grid.AddText(p, h, 115);
            Grid.FormatNumbers(p, NumberFormat.Phone);
        }
        Grid.AddText(nameof(Row.Status), "Status", 80);
    }

    private void Number(string property, string header, int width)
    {
        var col = Grid.AddText(property, header, width, format: "N0");
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        SumProperties.Add(property);
    }
}
