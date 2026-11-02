using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Prints fee challan forms class and section wise (replaces frmPrintChallanClasewise): one sheet per student on roll with
/// unpaid challans, showing all of them added together.
/// </summary>
public sealed class PrintChallansScreen : ScreenBase
{
    public sealed class Row
    {
        public bool Print { get; set; } = true;
        public int Cc { get; init; }
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public int? SchoolId { get; init; }
        public int? ClassId { get; init; }
        public int? SectionId { get; init; }
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public string SortKey { get; init; } = "";
        public List<string> PeriodList { get; init; } = [];
        public string Periods => string.Join(", ", PeriodList);
        public int Challans => PeriodList.Count;
        public int Total { get; init; }
    }

    private const string AllPeriods = "All unpaid challans";
    private readonly ComboBox _school = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _class = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _section = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _period = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonButton _all = new() { Text = "Tick all" };
    private readonly KryptonButton _none = new() { Text = "Untick all" };
    private readonly KryptonButton _print = new() { Text = "Print challans", Values = { Image = Icons.Small(Icons.Print) } };
    private readonly AccessGrid _grid = new();
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private List<Row> _rows = [];
    private FilterBindingList<Row>? _list;
    private List<(int Id, int? DepartmentId, string Name, string Sort)> _classes = [];
    private Dictionary<int, int?> _departmentSchool = [];
    private List<(int Id, int? ClassId, string Name)> _sections = [];
    private string? _pendingPeriod;
    private bool _filling;

    public PrintChallansScreen(ScreenContext ctx) : base(ctx)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_school, _class, _section, _period, _all, _none, _print]);
        foreach (var b in new[] { _all, _none, _print })
        {
            b.Size = new Size(TextRenderer.MeasureText(b.Text, Theme.BaseFont).Width + (b == _print ? 48 : 28), Theme.RowHeight + 2);
            b.Margin = new Padding(8, 2, 0, 0);
        }
        _school.SelectedIndexChanged += (_, _) => { if (!_filling) { FillClasses(); ApplyFilters(); } };
        _class.SelectedIndexChanged += (_, _) => { if (!_filling) { FillSections(); ApplyFilters(); } };
        _section.SelectedIndexChanged += (_, _) => { if (!_filling) ApplyFilters(); };
        _period.SelectedIndexChanged += (_, _) => { if (!_filling) ApplyFilters(); };
        _all.Click += (_, _) => Tick(true);
        _none.Click += (_, _) => Tick(false);
        _print.Click += (_, _) => PrintTicked();
        Theme.MainButton(_print);

        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 8, Padding = new Padding(0, 6, 0, 0),
            Text = "Students on roll with unpaid challans. Each challan form shows all the student's unpaid challans added together; untick students you do not want to print.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.AddCheck(nameof(Row.Print), "Print", 50);
        _grid.AddText(nameof(Row.Cc), "CC", 60, readOnly: true);
        _grid.AddText(nameof(Row.Name), "Name", 200, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.FatherName), "Father name", 180, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.ClassName), "Class", 110, readOnly: true);
        _grid.AddText(nameof(Row.SectionName), "Section", 100, readOnly: true);
        _grid.AddText(nameof(Row.Periods), "Unpaid periods", 220, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.Challans), "Challans", 70, readOnly: true);
        var total = _grid.AddText(nameof(Row.Total), "Total", 90, readOnly: true, format: "N0");
        total.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.CellValueChanged += (_, _) => RaiseStatusChanged();
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.FilterChanged += (_, _) => RaiseStatusChanged();

        var body = new KryptonPanel { Dock = DockStyle.Fill };
        body.Controls.Add(_grid);
        body.Controls.Add(hint);
        body.Controls.Add(bar);
        Controls.Add(body);

        _commands = [new("Print", new ScreenCommand("Print challans", Icons.Print, PrintTicked))];
        HandleCreated += (_, _) => _ = LoadAsync();
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;

    public override string StatusText
    {
        get
        {
            var shown = Shown().ToList();
            var ticked = shown.Where(r => r.Print).ToList();
            return $"{shown.Count} students shown · {ticked.Count} ticked to print · Rs {ticked.Sum(r => r.Total):N0}";
        }
    }

    public override void Reload() => _ = LoadAsync();
    public override void SortAscending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Descending);
    public override void ClearFilters() => _grid.ClearAllFilters();

    /// <summary>A period such as "NOV/26": shows the students with an unpaid challan for it.</summary>
    public override void Navigate(object target)
    {
        if (target is not string period) return;
        if (_period.Items.Count == 0) { _pendingPeriod = period; return; }
        SelectPeriod(period);
    }

    private sealed record Loaded(List<Row> Rows, List<(int, int?, string, string)> Classes, Dictionary<int, int?> DepartmentSchool,
        List<(int, int?, string)> Sections, List<(int?, string)> Schools, List<string> Periods);

    private async Task LoadAsync()
    {
        UseWaitCursor = true;
        try
        {
            var data = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                var classes = db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).AsEnumerable()
                    .Select(c => (c.Id, c.DepartmentId, c.Name ?? "", c.SortName ?? c.Name ?? "")).ToList();
                var departments = db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.SchoolId);
                var sections = db.Sections.AsNoTracking().OrderBy(s => s.Name).AsEnumerable().Select(s => (s.Id, s.ClassId, s.Name ?? "")).ToList();
                var schools = db.Schools.AsNoTracking().OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList();
                var unpaid = (from c in db.FeeChallans.AsNoTracking()
                              join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                              where !c.Paid && s.OnRoll
                              select new { s.Id, s.Name, Father = s.Family!.FatherName, s.ClassId, s.SectionId, c.Period, c.IssueDate, Charge = c }).ToList();
                var className = classes.ToDictionary(c => c.Id, c => (Name: c.Item3, Sort: c.Item4, Dept: c.DepartmentId));
                var sectionName = sections.ToDictionary(s => s.Id, s => s.Item3);
                var rows = unpaid.GroupBy(u => u.Id).Select(g =>
                {
                    var f = g.First();
                    var cls = f.ClassId is int ci && className.TryGetValue(ci, out var cn) ? cn : ("", "", (int?)null);
                    return new Row
                    {
                        Cc = g.Key, Name = f.Name ?? "", FatherName = f.Father ?? "", ClassId = f.ClassId, SectionId = f.SectionId,
                        ClassName = cls.Item1, SectionName = f.SectionId is int si && sectionName.TryGetValue(si, out var sn) ? sn : "",
                        SchoolId = cls.Item3 is int d && departments.TryGetValue(d, out var school) ? school : null,
                        SortKey = cls.Item2,
                        PeriodList = g.OrderBy(x => x.IssueDate).Select(x => x.Period ?? "").ToList(),
                        Total = g.Sum(x => x.Charge.Total),
                    };
                }).OrderBy(r => r.SortKey).ThenBy(r => r.SectionName).ThenBy(r => r.Name).ToList();
                var periods = unpaid.Where(u => u.Period is { Length: > 0 }).GroupBy(u => FeeChallanData.PeriodKey(u.Period))
                    .OrderByDescending(g => g.Max(x => x.IssueDate)).Select(g => g.First().Period!).ToList();
                return new Loaded(rows, classes, departments, sections, schools, periods);
            });
            if (IsDisposed) return;
            _rows = data.Rows;
            _classes = data.Classes;
            _departmentSchool = data.DepartmentSchool;
            _sections = data.Sections;
            _filling = true;
            Fill(_school, "All schools", data.Schools);
            var keepPeriod = _period.SelectedItem as string;
            _period.Items.Clear();
            _period.Items.Add(AllPeriods);
            foreach (var p in data.Periods) _period.Items.Add(p);
            _period.SelectedItem = keepPeriod is not null && _period.Items.Contains(keepPeriod) ? keepPeriod : AllPeriods;
            _filling = false;
            FillClasses();
            _list = new FilterBindingList<Row>(_rows);
            _grid.Bind(_list);
            if (_pendingPeriod is { } pending) { _pendingPeriod = null; SelectPeriod(pending); }
            ApplyFilters();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the unpaid challans.", ex);
        }
        finally
        {
            _filling = false;
            UseWaitCursor = false;
        }
    }

    private void SelectPeriod(string period)
    {
        var item = _period.Items.OfType<string>().FirstOrDefault(p => p != AllPeriods && FeeChallanData.PeriodKey(p) == FeeChallanData.PeriodKey(period));
        if (item is null) { Inform($"No unpaid challans for {period}."); return; }
        _period.SelectedItem = item;
    }

    private static void Fill(ComboBox combo, string all, IEnumerable<(int? Value, string Text)> items)
    {
        var keep = (combo.SelectedItem as Inputs.Item<int?>)?.Value;
        var list = new List<Inputs.Item<int?>> { new(null, all) };
        list.AddRange(items.Select(i => new Inputs.Item<int?>(i.Value, i.Text)));
        combo.DataSource = list;
        combo.DisplayMember = nameof(Inputs.Item<int?>.Text);
        combo.SelectedItem = list.FirstOrDefault(i => keep is not null && i.Value == keep) ?? list[0];
    }

    private static int? Selected(ComboBox combo) => (combo.SelectedItem as Inputs.Item<int?>)?.Value;

    private void FillClasses()
    {
        _filling = true;
        var school = Selected(_school);
        Fill(_class, "All classes", _classes
            .Where(c => school is null || c.DepartmentId is int d && _departmentSchool.GetValueOrDefault(d) == school)
            .Select(c => ((int?)c.Id, $"Class: {c.Name}")));
        _filling = false;
        FillSections();
    }

    private void FillSections()
    {
        _filling = true;
        var cls = Selected(_class);
        Fill(_section, "All sections", _sections.Where(s => cls is null || s.ClassId == cls).Select(s => ((int?)s.Id, $"Section: {s.Name}")));
        _filling = false;
    }

    private void ApplyFilters()
    {
        if (_list is null) return;
        var school = Selected(_school);
        var cls = Selected(_class);
        var section = Selected(_section);
        var period = _period.SelectedItem as string is { } p && p != AllPeriods ? FeeChallanData.PeriodKey(p) : null;
        _list.SetSearch(r => (school is null || r.SchoolId == school) && (cls is null || r.ClassId == cls)
                             && (section is null || r.SectionId == section) && (period is null || r.PeriodList.Any(x => FeeChallanData.PeriodKey(x) == period)));
        RaiseStatusChanged();
    }

    private IEnumerable<Row> Shown() => _list?.Cast<Row>() ?? [];

    private void Tick(bool on)
    {
        _grid.EndEdit();
        foreach (var r in Shown()) r.Print = on;
        _grid.Invalidate();
        RaiseStatusChanged();
    }

    private void PrintTicked()
    {
        _grid.EndEdit();
        var ccs = Shown().Where(r => r.Print).Select(r => r.Cc).ToList();
        if (ccs.Count == 0) { Inform("Tick the students whose challans you want to print."); return; }
        List<Printing.ChallanPrint> challans;
        try
        {
            UseWaitCursor = true;
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            challans = FeeChallanData.Load(db, ccs);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the challans.", ex); return; }
        finally { UseWaitCursor = false; }
        var school = SchoolDetails();
        var title = $"Fee challans · {challans.Count} students";
        PrintPreviewForm.Show(this, () => Printing.Challans.Create(challans, school, Ctx.Config.ChallanBank, Ctx.Config.ChallanLateFee),
            title, $"Fee challans {DateTime.Today:yyyy-MM-dd}");
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Print challans", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
