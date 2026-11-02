using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Generates a month's challans for every student on roll (replaces Feeupdate). Tuition comes from each student's fee definition;
/// Exams/AF, Registration, Prospectus and Other can be given an amount for everyone and changed per class in the preview.
/// A fine can be added to earlier unpaid challans, and a message (English or Urdu) is printed on the challans.
/// Students who already have that period are skipped. Only an Admin can generate or remove a batch.
/// </summary>
public sealed class PeriodicFeeScreen : ScreenBase
{
    /// <summary>One class in the preview (the last row is the total). The "each" amounts can be changed; blank means the fee definition.</summary>
    public sealed class ClassRow
    {
        public int ClassId { get; init; }
        public string Class { get; init; } = "";
        public string Sections { get; init; } = "";
        public string SortKey { get; init; } = "";
        public bool IsTotal { get; init; }
        public int? ExamsEach { get; set; }
        public int? RegistrationEach { get; set; }
        public int? ProspectusEach { get; set; }
        public int? OtherEach { get; set; }
        public int? FineEach { get; set; }
        public int Students { get; set; }
        public int Tuition { get; set; }
        public int Charged { get; set; }
        public int FinedChallans { get; set; }
        public int Fines { get; set; }
        public int BroughtForward { get; set; }
        public string Status { get; set; } = "";
    }

    /// <summary>A student in the chosen classes, as found by Preview.</summary>
    private sealed record Candidate(int StudentId, int ClassId, FeeDefinition? Definition, bool HasPeriod, bool Flagged, List<int> FineChallanIds, bool HasEarlierUnpaid);

    /// <summary>A challan to be generated.</summary>
    private sealed record Planned(int StudentId, int Tuition, int Exams, int Registration, int Prospectus, int Other)
    {
        public int Total => Tuition + Exams + Registration + Prospectus + Other;
    }

    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonDateTimePicker _issue = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly KryptonDateTimePicker _due = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly ComboBox _school = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _class = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FeeChoice _tuition = new("Include", amount: false) { Checked = true };
    private readonly FeeChoice _exams = new("Include"), _registration = new("Include"), _prospectus = new("Include"), _other = new("Include");
    private readonly FeeChoice _fine = new("Add") { Amount = 200 };
    private readonly KryptonCheckBox _skipFlagged = new() { Text = "Skip flagged families", AutoSize = true };
    private readonly KryptonTextBox _message = new() { Multiline = true, Height = 46, MaxLength = 255, ScrollBars = ScrollBars.Vertical };
    private readonly KryptonLabel _steps = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
    private readonly KryptonLabel _banner = new()
    {
        Dock = DockStyle.Top, AutoSize = true, Visible = false, Padding = new Padding(8, 6, 8, 6),
        StateCommon = { ShortText = { Color1 = Theme.Warning } }
    };
    private readonly AccessGrid _grid = new();
    private readonly KryptonButton _preview = new() { Text = "Preview" };
    private readonly KryptonButton _generate = new() { Text = "Generate fees", Enabled = false };
    private readonly bool _isAdmin;
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private List<(int Id, int? DepartmentId, string Name, string Sort)> _classes = [];
    private Dictionary<int, int?> _departmentSchool = [];
    private List<Candidate> _candidates = [];
    private BindingList<ClassRow>? _rows;
    private List<Planned> _planned = [];
    private List<(int ChallanId, int Fine)> _fines = [];
    private string? _plannedPeriod;
    private bool _filling, _computing;

    public PeriodicFeeScreen(ScreenContext ctx) : base(ctx)
    {
        _isAdmin = ctx.Session.IsAdmin;
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var m = month.AddMonths(-3); m <= month.AddMonths(3); m = m.AddMonths(1)) _period.Items.Add(new PeriodItem(m));
        _period.SelectedIndex = DateTime.Today.Day >= 20 ? 4 : 3; // late in the month: next month's fee
        PeriodChanged();
        _period.SelectedIndexChanged += (_, _) => { PeriodChanged(); Clear(); };
        _issue.ValueChanged += (_, _) => Clear();
        _due.ValueChanged += (_, _) => Clear();
        _school.SelectedIndexChanged += (_, _) => { if (!_filling) { FillClasses(); Clear(); } };
        _class.SelectedIndexChanged += (_, _) => { if (!_filling) Clear(); };
        _tuition.Changed += () => Compute();
        foreach (var (choice, set) in new (FeeChoice, Action<ClassRow, int?>)[]
                 {
                     (_exams, (r, v) => r.ExamsEach = v), (_registration, (r, v) => r.RegistrationEach = v),
                     (_prospectus, (r, v) => r.ProspectusEach = v), (_other, (r, v) => r.OtherEach = v), (_fine, (r, v) => r.FineEach = v),
                 })
            choice.Changed += () => SetAll(choice, set);
        _skipFlagged.CheckedChanged += (_, _) => Compute();
        _message.TextChanged += (_, _) => _message.RightToLeft = HasUrdu(_message.Text) ? RightToLeft.Yes : RightToLeft.No;

        var fields = new FieldPanel(6);
        fields.Add("Period", _period);
        fields.Add("Issue date", _issue);
        fields.Add("Due date", _due);
        fields.Add("School", _school, 2);
        fields.Add("Classes", _class);
        fields.Add("Tuition (fee definition)", _tuition);
        fields.Add("Exams / Annual Fund, each", _exams);
        fields.Add("Registration, each", _registration);
        fields.Add("Prospectus, each", _prospectus);
        fields.Add("Other / summer tasks, each", _other);
        fields.Add("Fine on earlier unpaid challans", _fine);
        fields.Add("Message printed on the challans (English or Urdu)", _message, 5);
        fields.Add("", _skipFlagged);
        var choose = new Card();
        choose.AddTop(fields);

        BuildGrid();

        foreach (var b in new[] { _preview, _generate })
            b.Size = new Size(TextRenderer.MeasureText(b.Text, Theme.BaseFont).Width + 36, Theme.RowHeight + 6);
        _preview.Click += (_, _) => _ = PreviewAsync();
        _generate.Click += (_, _) => Generate();
        Theme.MainButton(_generate);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.RowHeight + 20, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
        _generate.Dock = DockStyle.Right;
        _preview.Dock = DockStyle.Right;
        var note = new KryptonLabel
        {
            Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0),
            Text = _isAdmin ? "Amounts left at 0 come from each student's fee definition. Change an amount for one class in the list. Nothing is saved until you press Generate fees."
                            : "Only an Admin can generate fees. You can preview them.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        footer.Controls.AddRange([note, _preview, new Panel { Dock = DockStyle.Right, Width = 8 }, _generate]);
        _generate.Visible = _isAdmin;

        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        body.Controls.Add(_grid);
        body.Controls.Add(_banner);
        body.Controls.Add(footer);
        Controls.Add(body);
        Controls.Add(choose);
        Controls.Add(_steps);
        ShowStep(1);

        _commands =
        [
            new("Steps", new ScreenCommand("Preview", Icons.Search, () => _ = PreviewAsync()), new ScreenCommand("Generate", Icons.Refresh, Generate)),
            new("After",
                new ScreenCommand("Print challans", Icons.Print, () => Ctx.OpenScreenAt("challans", Period)),
                new ScreenCommand("Send WhatsApp", Icons.Chat, () => Inform("Sending challans on WhatsApp comes with the Messages screens (phase 5)."))),
            new("Undo", new ScreenCommand("Remove batch", Icons.Delete, RemoveBatch)),
        ];
        HandleCreated += (_, _) => _ = LoadListsAsync();
    }

    private void BuildGrid()
    {
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.ShowFilters = false;
        _grid.AddText(nameof(ClassRow.Class), "Class", 120, readOnly: true, fill: true);
        _grid.AddText(nameof(ClassRow.Sections), "Sections", 120, readOnly: true, fill: true);
        foreach (var (property, header, edit) in new[]
                 {
                     (nameof(ClassRow.Students), "Students", false), (nameof(ClassRow.Tuition), "Tuition", false),
                     (nameof(ClassRow.ExamsEach), "Exams each", true), (nameof(ClassRow.RegistrationEach), "Reg. each", true),
                     (nameof(ClassRow.ProspectusEach), "Prosp. each", true), (nameof(ClassRow.OtherEach), "Other each", true),
                     (nameof(ClassRow.Charged), "New total", false), (nameof(ClassRow.FineEach), "Fine each", true),
                     (nameof(ClassRow.Fines), "Fines", false), (nameof(ClassRow.BroughtForward), "Owing before", false),
                 })
        {
            var col = _grid.AddText(property, header, edit ? 95 : 90, readOnly: !edit, format: "N0");
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            if (edit) col.ToolTipText = "Type an amount for every student of this class. Blank: from the fee definition.";
        }
        _grid.AddText(nameof(ClassRow.Status), "Status", 150, readOnly: true);
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.ReadOnly = !_isAdmin;
        _grid.CanEditCell = (row, column) =>
            row >= 0 && _grid.Rows[row].DataBoundItem is ClassRow { IsTotal: false } && Choice(_grid.Columns[column].DataPropertyName)?.Checked == true;
        _grid.CellValueChanged += (_, _) => { if (!_computing) Compute(); };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null || _grid.Rows[e.RowIndex].DataBoundItem is not ClassRow r) return;
            var property = _grid.Columns[e.ColumnIndex].DataPropertyName;
            if (r.IsTotal) e.CellStyle.Font = Theme.Scaled(1f, FontStyle.Bold);
            if (Choice(property) is { Checked: false } && !r.IsTotal) { e.Value = ""; e.FormattingApplied = true; }
            if (property == nameof(ClassRow.Status))
                e.CellStyle.ForeColor = r.Status == "Ready" ? Theme.Good : Theme.Warning;
        };
    }

    /// <summary>The fee choice behind an editable "each" column (null for other columns).</summary>
    private FeeChoice? Choice(string property) => property switch
    {
        nameof(ClassRow.ExamsEach) => _exams,
        nameof(ClassRow.RegistrationEach) => _registration,
        nameof(ClassRow.ProspectusEach) => _prospectus,
        nameof(ClassRow.OtherEach) => _other,
        nameof(ClassRow.FineEach) => _fine,
        _ => null,
    };

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override void Reload() => _ = LoadListsAsync();
    public override string StatusText => _planned.Count == 0 && _fines.Count == 0 ? "Choose the period and classes, then press Preview."
        : $"{_planned.Count} challans for {_plannedPeriod} · Rs {_planned.Sum(p => p.Total):N0}" +
          (_fines.Count > 0 ? $" · fine on {_fines.Count} earlier challans · Rs {_fines.Sum(f => f.Fine):N0}" : "");

    private static bool HasUrdu(string text) => text.Any(c => c is >= '؀' and <= 'ۿ');

    private sealed record PeriodItem(DateTime Month)
    {
        public override string ToString() => Month.ToString("MMM yyyy");
    }

    private string Period => FeeChallanData.Period(((PeriodItem)_period.SelectedItem!).Month);

    private void PeriodChanged()
    {
        var month = ((PeriodItem)_period.SelectedItem!).Month;
        _issue.Value = month;
        _due.Value = month.AddDays(9);
    }

    /// <summary>The period, dates or classes changed: preview again before generating.</summary>
    private void Clear()
    {
        if (_rows is null && !_generate.Enabled) return;
        _candidates = [];
        _rows = null;
        _planned = [];
        _fines = [];
        _generate.Enabled = false;
        _grid.DataSource = null;
        _banner.Visible = false;
        ShowStep(1);
        RaiseStatusChanged();
    }

    private void ShowStep(int step)
    {
        string Step(int n, string text) => n == step ? $"[ {n} {text} ]" : $"{n} {text}";
        _steps.Text = $"{Step(1, "Choose period and classes")}      ›      {Step(2, "Preview")}      ›      {Step(3, "Generate and print challans")}";
    }

    private async Task LoadListsAsync()
    {
        try
        {
            var (classes, departments, schools) = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                return (db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).AsEnumerable()
                            .Select(c => (c.Id, c.DepartmentId, c.Name ?? "", c.SortName ?? c.Name ?? "")).ToList(),
                        db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.SchoolId),
                        db.Schools.AsNoTracking().OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList());
            });
            if (IsDisposed) return;
            (_classes, _departmentSchool) = (classes, departments);
            _filling = true;
            Fill(_school, "Both schools", schools);
            _filling = false;
            FillClasses();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the classes.", ex);
        }
        finally { _filling = false; }
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
        Fill(_class, "All classes", ClassesOf(school).Select(c => ((int?)c.Id, c.Name)));
        _filling = false;
    }

    private IEnumerable<(int Id, int? DepartmentId, string Name, string Sort)> ClassesOf(int? school) =>
        _classes.Where(c => school is null || c.DepartmentId is int d && _departmentSchool.GetValueOrDefault(d) == school);

    // ----- Preview -----

    /// <summary>Reads the students of the chosen classes, their fee definitions and unpaid challans. Nothing is saved.</summary>
    private async Task PreviewAsync()
    {
        if (_classes.Count == 0) return;
        if (_due.Value.Date < _issue.Value.Date) { Inform("The due date is before the issue date."); return; }
        var period = Period;
        var issue = _issue.Value.Date;
        var school = Selected(_school);
        var classId = Selected(_class);
        var classes = (classId is int one ? _classes.Where(c => c.Id == one) : ClassesOf(school)).ToList();
        var classIds = classes.Select(c => (int?)c.Id).ToList();
        UseWaitCursor = true;
        _preview.Enabled = false;
        try
        {
            var (candidates, sections) = await Task.Run(() => Read(period, issue, classIds));
            if (IsDisposed) return;
            _candidates = candidates;
            _plannedPeriod = period;
            _rows = new BindingList<ClassRow>(classes.Where(c => candidates.Any(x => x.ClassId == c.Id)).Select(c => new ClassRow
            {
                ClassId = c.Id, Class = c.Name, SortKey = c.Sort, Sections = sections.GetValueOrDefault(c.Id, ""),
                ExamsEach = Each(_exams), RegistrationEach = Each(_registration), ProspectusEach = Each(_prospectus),
                OtherEach = Each(_other), FineEach = Each(_fine),
            }).Append(new ClassRow { Class = "All classes", IsTotal = true }).ToList());
            _grid.DataSource = _rows;
            ShowStep(2);
            Compute();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not prepare the preview.", ex);
        }
        finally
        {
            _preview.Enabled = true;
            UseWaitCursor = false;
        }
    }

    /// <summary>The amount typed under a fee for everyone (null: from the fee definition).</summary>
    private static int? Each(FeeChoice choice) => choice.Amount > 0 ? choice.Amount : null;

    private (List<Candidate>, Dictionary<int, string>) Read(string period, DateTime issue, List<int?> classIds)
    {
        using var db = Ctx.Config.CreateContext(Ctx.Session);
        var students = db.Students.AsNoTracking().Where(s => s.OnRoll && classIds.Contains(s.ClassId))
            .Select(s => new { s.Id, ClassId = s.ClassId!.Value, s.SectionId, Flagged = s.Family != null && s.Family.Flag }).ToList();
        var ids = students.Select(s => (int?)s.Id).ToList();
        var definitions = db.FeeDefinitions.AsNoTracking().Where(d => ids.Contains(d.StudentId)).ToList()
            .GroupBy(d => d.StudentId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(d => d.Id)!);
        var key = FeeChallanData.PeriodKey(period);
        var challans = db.FeeChallans.AsNoTracking().Where(c => ids.Contains(c.StudentId))
            .Select(c => new { c.Id, StudentId = c.StudentId!.Value, c.Period, c.Paid, c.Fine, c.DueDate }).AsEnumerable()
            .Select(c => new { c.Id, c.StudentId, Same = FeeChallanData.PeriodKey(c.Period) == key, c.Paid, c.Fine, c.DueDate })
            .GroupBy(c => c.StudentId).ToDictionary(g => g.Key, g => g.ToList());
        var sectionNames = db.Sections.AsNoTracking().ToDictionary(s => s.Id, s => s.Name ?? "");
        var candidates = students.Select(s =>
        {
            var mine = challans.GetValueOrDefault(s.Id) ?? [];
            // Earlier unpaid challans: due before this month's issue date. A fine is added only where there is none yet.
            var earlier = mine.Where(c => !c.Paid && !c.Same && (c.DueDate is null || c.DueDate < issue)).ToList();
            return new Candidate(s.Id, s.ClassId, definitions.GetValueOrDefault(s.Id), mine.Any(c => c.Same), s.Flagged,
                earlier.Where(c => (c.Fine ?? 0) == 0).Select(c => c.Id).ToList(), earlier.Count > 0);
        }).ToList();
        var sections = students.GroupBy(s => s.ClassId).ToDictionary(g => g.Key, g =>
        {
            var names = g.Select(s => s.SectionId is int si ? sectionNames.GetValueOrDefault(si, "") : "").Where(n => n.Length > 0).Distinct().Order().ToList();
            return names.Count > 3 ? $"{names.Count} sections" : string.Join(", ", names);
        });
        return (candidates, sections);
    }

    /// <summary>A fee amount typed under a fee: every class gets it (it can then be changed per class).</summary>
    private void SetAll(FeeChoice choice, Action<ClassRow, int?> set)
    {
        if (_rows is not null)
            foreach (var r in _rows.Where(r => !r.IsTotal)) set(r, Each(choice));
        Compute();
    }

    /// <summary>Works out the challans and fines from what Preview read and the amounts chosen (no database).</summary>
    private void Compute()
    {
        if (_rows is null) { RaiseStatusChanged(); return; }
        _computing = true;
        var planned = new List<Planned>();
        var fines = new List<(int, int)>();
        int alreadyHave = 0, noFee = 0, flagged = 0;
        int Fee(bool include, int? each, int? definition) => include ? each ?? definition ?? 0 : 0;
        foreach (var row in _rows.Where(r => !r.IsTotal))
        {
            var mine = new List<Planned>();
            int skipped = 0, fined = 0, broughtForward = 0;
            foreach (var c in _candidates.Where(c => c.ClassId == row.ClassId))
            {
                if (_skipFlagged.Checked && c.Flagged) { flagged++; skipped++; continue; }
                if (_fine.Checked && (row.FineEach ?? 0) > 0)
                    foreach (var id in c.FineChallanIds) { fines.Add((id, row.FineEach!.Value)); fined++; }
                if (c.HasPeriod) { alreadyHave++; skipped++; continue; }
                var d = c.Definition;
                var p = new Planned(c.StudentId, _tuition.Checked ? d?.TuitionFee ?? 0 : 0,
                    Fee(_exams.Checked, row.ExamsEach, d?.ExamsFee), Fee(_registration.Checked, row.RegistrationEach, d?.RegistrationFee),
                    Fee(_prospectus.Checked, row.ProspectusEach, d?.ProspectusFee), Fee(_other.Checked, row.OtherEach, d?.Other));
                if (p.Total <= 0) { noFee++; skipped++; continue; }
                mine.Add(p);
                if (c.HasEarlierUnpaid) broughtForward++;
            }
            planned.AddRange(mine);
            row.Students = mine.Count;
            row.Tuition = mine.Sum(p => p.Tuition);
            row.Charged = mine.Sum(p => p.Total);
            row.FinedChallans = fined;
            row.Fines = fined * (_fine.Checked ? row.FineEach ?? 0 : 0);
            row.BroughtForward = broughtForward;
            row.Status = mine.Count == 0 && fined == 0 ? "Nothing to generate" : skipped == 0 ? "Ready" : $"{skipped} skipped";
        }
        var total = _rows.First(r => r.IsTotal);
        var classes = _rows.Where(r => !r.IsTotal).ToList();
        total.Students = classes.Sum(r => r.Students);
        total.Tuition = classes.Sum(r => r.Tuition);
        total.Charged = classes.Sum(r => r.Charged);
        total.FinedChallans = classes.Sum(r => r.FinedChallans);
        total.Fines = classes.Sum(r => r.Fines);
        total.BroughtForward = classes.Sum(r => r.BroughtForward);
        _planned = planned;
        _fines = fines;
        var notes = new List<string>();
        if (alreadyHave > 0) notes.Add($"{_plannedPeriod} fee already exists for {alreadyHave} students. They will be skipped.");
        if (noFee > 0) notes.Add($"{noFee} student{(noFee == 1 ? "" : "s")} with no fee to charge (no fee definition, or the included fees are 0): skipped.");
        if (flagged > 0) notes.Add($"{flagged} students of flagged families are skipped.");
        if (fines.Count > 0) notes.Add($"A fine is added to {fines.Count} earlier unpaid challans that have no fine yet.");
        _banner.Text = string.Join("\n", notes);
        _banner.Visible = notes.Count > 0;
        _generate.Enabled = _isAdmin && (_planned.Count > 0 || _fines.Count > 0);
        _grid.Invalidate();
        _computing = false;
        RaiseStatusChanged();
    }

    // ----- Generate and remove -----

    private void Generate()
    {
        if (!_isAdmin) { Inform("Only an Admin can generate fees."); return; }
        _grid.EndEdit();
        if (_rows is null || _plannedPeriod != Period) { Inform("Press Preview first."); return; }
        if (_planned.Count == 0 && _fines.Count == 0) { Inform("There is nothing to generate."); return; }
        var period = _plannedPeriod;
        var question = new List<string>();
        if (_planned.Count > 0) question.Add($"Generate {_planned.Count} challans for {period} (Rs {_planned.Sum(p => p.Total):N0}), due {_due.Value:dd/MM/yyyy}?");
        if (_fines.Count > 0) question.Add($"Add a fine to {_fines.Count} earlier unpaid challans (Rs {_fines.Sum(f => f.Fine):N0})?");
        question.Add("New challans can be removed again with Remove batch while they are unpaid; fines are changed in the Fee ledger.");
        if (KryptonMessageBox.Show(this, string.Join("\n\n", question), "Periodic fee update",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes) return;
        var issue = _issue.Value.Date;
        var due = _due.Value.Date;
        var message = _message.Text.Trim() is { Length: > 0 } m ? m : null;
        var now = DateTime.Now;
        int added, fined;
        try
        {
            UseWaitCursor = true;
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            // Someone may have added the period since the preview: skip those students too.
            var ids = _planned.Select(p => (int?)p.StudentId).ToList();
            var key = FeeChallanData.PeriodKey(period);
            var have = db.FeeChallans.Where(c => ids.Contains(c.StudentId)).Select(c => new { c.StudentId, c.Period }).AsEnumerable()
                .Where(c => FeeChallanData.PeriodKey(c.Period) == key).Select(c => c.StudentId!.Value).ToHashSet();
            var batch = _planned.Where(p => !have.Contains(p.StudentId)).Select(p => new FeeChallan
            {
                StudentId = p.StudentId, Period = period, IssueDate = issue, DueDate = due,
                TuitionFee = p.Tuition, ExamsFee = p.Exams, RegistrationFee = p.Registration, ProspectusFee = p.Prospectus, Other = p.Other,
                AdmissionFee = 0, Fine = 0, ReceiptNo = 0, ReceivedAmount = 0, DrCr = "DR", Remarks = message,
                EditedBy = Ctx.Session.UserName, DateEdited = now, CreatedOn = now, // one CreatedOn marks the batch for Remove batch
            }).ToList();
            db.FeeChallans.AddRange(batch);
            // Fines: only on challans still unpaid and still without a fine.
            var fineOf = _fines.ToDictionary(f => f.ChallanId, f => f.Fine);
            var fineIds = fineOf.Keys.ToList();
            var toFine = db.FeeChallans.Where(c => fineIds.Contains(c.Id) && !c.Paid && (c.Fine == null || c.Fine == 0)).ToList();
            foreach (var c in toFine)
            {
                c.Fine = fineOf[c.Id];
                c.EditedBy = Ctx.Session.UserName;
                c.DateEdited = now;
            }
            db.SaveChanges(); // one transaction: all or nothing
            (added, fined) = (batch.Count, toFine.Count);
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("Could not generate the fees. Nothing was saved.", ex);
            return;
        }
        finally { UseWaitCursor = false; }
        Clear();
        ShowStep(3);
        var done = $"{added} challans for {period} were generated" + (fined > 0 ? $", and a fine was added to {fined} earlier challans." : ".");
        if (KryptonMessageBox.Show(this, done + "\n\nPrint the challans now?", "Periodic fee update",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Information) == DialogResult.Yes)
            Ctx.OpenScreenAt("challans", period);
    }

    private sealed record Batch(DateTime CreatedOn, string Period, int Count, int Paid, string By)
    {
        public override string ToString() => $"{Period}  ·  made {CreatedOn:dd/MM/yyyy HH:mm} by {By}  ·  {Count} challans, {Paid} paid";
    }

    /// <summary>Removes the unpaid challans of a generated batch (they are marked deleted, as when deleting one challan).</summary>
    private void RemoveBatch()
    {
        if (!_isAdmin) { Inform("Only an Admin can remove a batch."); return; }
        List<Batch> batches;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var since = DateTime.Today.AddMonths(-4);
            batches = db.FeeChallans.AsNoTracking().Where(c => c.CreatedOn != null && c.CreatedOn >= since)
                .GroupBy(c => new { c.CreatedOn, c.Period })
                .Select(g => new { g.Key.CreatedOn, g.Key.Period, Count = g.Count(), Paid = g.Count(c => c.Paid), By = g.Max(c => c.EditedBy) })
                .Where(g => g.Count >= 10).OrderByDescending(g => g.CreatedOn).Take(12).AsEnumerable()
                .Select(g => new Batch(g.CreatedOn!.Value, g.Period ?? "", g.Count, g.Paid, g.By ?? "")).ToList();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not list the batches.", ex); return; }
        if (batches.Count == 0) { Inform("No generated batches in the last four months."); return; }

        using var form = new KryptonForm
        {
            Text = "Remove batch", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ShowInTaskbar = false, ClientSize = new Size(560, 330),
        };
        form.Shown += (_, _) => Theme.FitButtons(form);
        var list = new KryptonListBox { Dock = DockStyle.Fill };
        foreach (var b in batches) list.Items.Add(b);
        list.SelectedIndex = 0;
        var info = new KryptonLabel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8), Text = "Pick the batch to remove. Only its unpaid challans are removed; paid ones stay." };
        var ok = new KryptonButton { Text = "Remove", DialogResult = DialogResult.OK, Width = 110 };
        var cancel = new KryptonButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 110 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange([cancel, ok]);
        var host = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(list);
        host.Controls.Add(info);
        host.Controls.Add(buttons);
        form.Controls.Add(host);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        if (form.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not Batch batch) return;
        if (KryptonMessageBox.Show(this, $"Remove the {batch.Count - batch.Paid} unpaid challans of {batch.Period} made on {batch.CreatedOn:dd/MM/yyyy HH:mm}?",
                "Remove batch", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var rows = db.FeeChallans.Where(c => c.CreatedOn == batch.CreatedOn && c.Period == batch.Period && !c.Paid).ToList();
            var now = DateTime.Now;
            foreach (var c in rows)
            {
                c.IsDeleted = true;
                c.DeletedOn = now;
                c.EditedBy = Ctx.Session.UserName;
                c.DateEdited = now;
            }
            db.SaveChanges();
            Inform($"{rows.Count} challans of {batch.Period} were removed.");
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("Could not remove the batch. Nothing was changed.", ex);
        }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Periodic fee update", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    /// <summary>A tick box with an amount beside it: "include this fee, Rs …".</summary>
    private sealed class FeeChoice : Panel
    {
        private readonly KryptonCheckBox _check;
        private readonly KryptonNumericUpDown? _amount;

        public event Action? Changed;

        public FeeChoice(string text, bool amount = true)
        {
            BackColor = Color.Transparent;
            Height = Theme.RowHeight + 2;
            _check = new KryptonCheckBox { Text = text, AutoSize = true, Location = new Point(0, 3) };
            _check.CheckedChanged += (_, _) => { if (_amount is not null) _amount.Enabled = _check.Checked; Changed?.Invoke(); };
            Controls.Add(_check);
            if (!amount) return;
            _amount = new KryptonNumericUpDown { Maximum = 1_000_000, Increment = 50, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right, Enabled = false };
            _amount.ValueChanged += (_, _) => Changed?.Invoke();
            Controls.Add(_amount);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked { get => _check.Checked; set => _check.Checked = value; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Amount { get => (int)(_amount?.Value ?? 0); set { if (_amount is not null) _amount.Value = value; } }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_amount is null) return;
            var left = _check.PreferredSize.Width + 6;
            _amount.SetBounds(left, 0, Math.Max(60, Width - left), _amount.Height);
        }
    }
}
