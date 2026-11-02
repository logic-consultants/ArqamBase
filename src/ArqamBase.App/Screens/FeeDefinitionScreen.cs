using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// What each student is charged every month (replaces frmFeeDefinition and frmClassFeeIncrease). Only an Admin can change it.
/// A changed monthly fee is logged in tblFeeUpdateList; every change is in the audit log.
/// </summary>
public sealed class FeeDefinitionScreen : ScreenBase
{
    /// <summary>One student and their newest fee definition (created when an amount is first typed).</summary>
    public sealed class Row(Student student, FeeDefinition? definition, Func<Row, FeeDefinition> create)
    {
        public Student Student { get; } = student;
        public FeeDefinition? Definition { get; set; } = definition;
        public bool Tick { get; set; }
        public int Cc => Student.Id;
        public string Name => Student.Name ?? "";
        public string FatherName => Student.Family?.FatherName ?? "";
        public int? ClassId => Student.ClassId;
        public int? SectionId => Student.SectionId;
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public int? SchoolId { get; init; }
        public string Status => Student.StatusText;

        private FeeDefinition Def => Definition ??= create(this);
        public int? TuitionFee { get => Definition?.TuitionFee; set => Def.TuitionFee = value; }
        public int? ExamsFee { get => Definition?.ExamsFee; set => Def.ExamsFee = value; }
        public int? RegistrationFee { get => Definition?.RegistrationFee; set => Def.RegistrationFee = value; }
        public int? ProspectusFee { get => Definition?.ProspectusFee; set => Def.ProspectusFee = value; }
        public int? Other { get => Definition?.Other; set => Def.Other = value; }
        public int? AcFee { get => Definition?.AcFee; set => Def.AcFee = value; }
        public int? AdmissionFee { get => Definition?.AdmissionFee; set => Def.AdmissionFee = value; }
        /// <summary>What the periodic fee update charges every month.</summary>
        public int Monthly => (TuitionFee ?? 0) + (ExamsFee ?? 0) + (RegistrationFee ?? 0) + (ProspectusFee ?? 0) + (Other ?? 0);
        public DateTime? RevisionDate => Definition?.RevisionDate ?? Definition?.CreatedDate;
        public int? RevisionNo => Definition?.RevisionNo;
    }

    private readonly KryptonTextBox _search = new() { Width = 240, CueHint = { CueHintText = "Search name or CC" } };
    private readonly ComboBox _school = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _class = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _section = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonCheckBox _left = new() { Text = "Show left students", AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly AccessGrid _grid = new();
    private readonly bool _isAdmin;
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private ArqamDbContext? _db;
    private FilterBindingList<Row>? _list;
    private List<(int Id, int? DepartmentId, string Name)> _classes = [];
    private Dictionary<int, int?> _departmentSchool = [];
    private List<(int Id, int? ClassId, string Name)> _sections = [];
    private bool _filling, _loading;

    public FeeDefinitionScreen(ScreenContext ctx) : base(ctx)
    {
        _isAdmin = ctx.Session.IsAdmin;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _school, _class, _section, _left]);
        if (_isAdmin)
        {
            foreach (var (text, action) in new (string, Action)[] { ("Tick all shown", () => Tick(true)), ("Untick all", () => Tick(false)), ("Increase fee…", IncreaseFee) })
            {
                var b = new KryptonButton { Text = text, Margin = new Padding(8, 2, 0, 0) };
                b.Size = new Size(TextRenderer.MeasureText(text, Theme.BaseFont).Width + 28, Theme.RowHeight + 2);
                b.Click += (_, _) => action();
                bar.Controls.Add(b);
            }
        }
        _search.TextChanged += (_, _) => ApplyFilters();
        _school.SelectedIndexChanged += (_, _) => { if (!_filling) { FillClasses(); ApplyFilters(); } };
        _class.SelectedIndexChanged += (_, _) => { if (!_filling) { FillSections(); ApplyFilters(); } };
        _section.SelectedIndexChanged += (_, _) => { if (!_filling) ApplyFilters(); };
        _left.CheckedChanged += (_, _) => ApplyFilters();

        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 8, Padding = new Padding(0, 6, 0, 0),
            Text = _isAdmin
                ? "The periodic fee update charges Monthly fee + Exams/AF + Registration + Prospectus + Other every month. Type to change; tick students and press Increase fee… for a class-wise increase."
                : "The periodic fee update charges Monthly fee + Exams/AF + Registration + Prospectus + Other every month. Only an Admin can change fee definitions.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = !_isAdmin;
        if (_isAdmin) _grid.AddCheck(nameof(Row.Tick), "Tick", 45);
        _grid.AddText(nameof(Row.Cc), "CC", 60, readOnly: true);
        _grid.AddText(nameof(Row.Name), "Name", 190, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.FatherName), "Father name", 170, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.ClassName), "Class", 100, readOnly: true);
        _grid.AddText(nameof(Row.SectionName), "Section", 90, readOnly: true);
        foreach (var (property, header) in new[]
                 {
                     (nameof(Row.TuitionFee), "Monthly fee"), (nameof(Row.ExamsFee), "Exams/AF"), (nameof(Row.RegistrationFee), "Registration"),
                     (nameof(Row.ProspectusFee), "Prospectus"), (nameof(Row.Other), "Other"), (nameof(Row.AcFee), "A/C"), (nameof(Row.AdmissionFee), "Admission"),
                 })
            AlignRight(_grid.AddText(property, header, 85, format: "N0"));
        AlignRight(_grid.AddText(nameof(Row.Monthly), "Charged monthly", 110, readOnly: true, format: "N0"));
        _grid.AddText(nameof(Row.RevisionDate), "Revised", 90, readOnly: true, format: "dd/MM/yyyy");
        _grid.AddText(nameof(Row.RevisionNo), "Rev.", 45, readOnly: true);
        _grid.AddText(nameof(Row.Status), "Status", 80, readOnly: true);
        _grid.CellValueChanged += (_, _) => { if (!_loading) RaiseStatusChanged(); };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.FilterChanged += (_, _) => RaiseStatusChanged();

        var body = new KryptonPanel { Dock = DockStyle.Fill };
        body.Controls.Add(_grid);
        body.Controls.Add(hint);
        body.Controls.Add(bar);
        Controls.Add(body);

        _commands = _isAdmin ? [new("Fee", new ScreenCommand("Increase fee", Icons.Up, IncreaseFee))] : [];
        HandleCreated += (_, _) => _ = LoadAsync();
    }

    private static void AlignRight(DataGridViewColumn c) => c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

    public override bool SupportsRecords => true;
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;
    public override void Reload() { if (ConfirmLeave()) _ = LoadAsync(); }
    public override void SortAscending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Descending);
    public override void ClearFilters() => _grid.ClearAllFilters();
    public override void FocusSearch() => _search.Focus();
    protected override void CommitPendingEdits() => _grid.EndEdit();
    public override void NewRecord() => Inform("Fee definitions belong to students: add the student on the Students screen, then type the fees here.");
    public override void DeleteRecord() => Inform("To stop charging a student, set the amounts to 0, or mark the student as left on the Students screen.");

    public override string StatusText
    {
        get
        {
            var shown = Shown().ToList();
            var text = $"{shown.Count} students · {shown.Count(r => r.Definition is null)} without a fee definition";
            if (_isAdmin)
            {
                var fees = shown.Where(r => (r.TuitionFee ?? 0) > 0).Select(r => r.TuitionFee!.Value).ToList();
                text += $" · monthly fee total Rs {fees.Sum():N0} · average Rs {(fees.Count == 0 ? 0 : fees.Average()):N0} · {shown.Count(r => r.Tick)} ticked";
            }
            return HasChanges ? text + " · unsaved changes" : text;
        }
    }

    private sealed record Loaded(List<Student> Students, List<FeeDefinition> Definitions, List<(int, int?, string)> Classes,
        Dictionary<int, int?> DepartmentSchool, List<(int, int?, string)> Sections, List<(int?, string)> Schools);

    private async Task LoadAsync()
    {
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        UseWaitCursor = true;
        _loading = true;
        try
        {
            var data = await Task.Run(() => new Loaded(
                db.Students.AsNoTracking().Include(s => s.Family).OrderBy(s => s.Name).ToList(),
                db.FeeDefinitions.Where(d => d.StudentId != null).ToList(),
                db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).AsEnumerable().Select(c => (c.Id, c.DepartmentId, c.Name ?? "")).ToList(),
                db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.SchoolId),
                db.Sections.AsNoTracking().OrderBy(s => s.Name).AsEnumerable().Select(s => (s.Id, s.ClassId, s.Name ?? "")).ToList(),
                db.Schools.AsNoTracking().OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList()));
            if (!ReferenceEquals(db, _db) || IsDisposed) return;
            (_classes, _departmentSchool, _sections) = (data.Classes, data.DepartmentSchool, data.Sections);
            var newest = data.Definitions.GroupBy(d => d.StudentId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(d => d.Id)!);
            var className = _classes.ToDictionary(c => c.Id, c => (c.Name, c.DepartmentId));
            var sectionName = _sections.ToDictionary(s => s.Id, s => s.Name);
            var rows = data.Students.Select(s =>
            {
                var cls = s.ClassId is int ci && className.TryGetValue(ci, out var cn) ? cn : ("", (int?)null);
                return new Row(s, newest.GetValueOrDefault(s.Id), CreateDefinition)
                {
                    ClassName = cls.Item1,
                    SectionName = s.SectionId is int si && sectionName.TryGetValue(si, out var sn) ? sn : "",
                    SchoolId = cls.Item2 is int d && _departmentSchool.TryGetValue(d, out var school) ? school : null,
                };
            }).ToList();
            _filling = true;
            Fill(_school, "All schools", data.Schools);
            _filling = false;
            FillClasses();
            _list = new FilterBindingList<Row>(rows);
            _grid.Bind(_list);
            ApplyFilters();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the fee definitions.", ex);
        }
        finally
        {
            _loading = false;
            _filling = false;
            UseWaitCursor = false;
            RaiseStatusChanged();
        }
    }

    private FeeDefinition CreateDefinition(Row row)
    {
        var d = new FeeDefinition { StudentId = row.Cc, DsNo = row.Student.DsNo, CreatedDate = DateTime.Today, RevisionNo = 0 };
        _db!.FeeDefinitions.Add(d);
        return d;
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
        var left = _left.Checked;
        var text = _search.Text.Trim();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _list.SetSearch(r => (left || r.Student.OnRoll) && (school is null || r.SchoolId == school) && (cls is null || r.ClassId == cls)
                             && (section is null || r.SectionId == section)
                             && (words.Length == 0 || r.Cc.ToString().StartsWith(text, StringComparison.Ordinal)
                                 || words.All(w => r.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase))));
        RaiseStatusChanged();
    }

    private IEnumerable<Row> Shown() => _list?.Cast<Row>() ?? [];

    private void Tick(bool on)
    {
        _grid.EndEdit();
        foreach (var r in on ? Shown() : _list?.AllItems ?? []) r.Tick = on;
        _grid.Invalidate();
        RaiseStatusChanged();
    }

    /// <summary>Adds an amount to the monthly fee of the ticked students (the class-wise fee increase).</summary>
    private void IncreaseFee()
    {
        if (!_isAdmin || _list is null) return;
        _grid.EndEdit();
        var ticked = _list.AllItems.Where(r => r.Tick && r.Definition is not null).ToList();
        if (_list.AllItems.Any(r => r.Tick && r.Definition is null))
            Inform("Students without a fee definition are left out: type their fees first.");
        if (ticked.Count == 0) { Inform("Tick the students whose monthly fee should go up (Tick all shown ticks the students in the list), then press Increase fee… again."); return; }
        using var dialog = new IncreaseFeeForm(ticked.Count);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var r in ticked)
        {
            r.TuitionFee = Math.Max(0, (r.TuitionFee ?? 0) + dialog.Amount);
            _increaseReasons[r.Cc] = dialog.Reason;
        }
        if (SaveChanges())
        {
            Tick(false);
            Inform($"The monthly fee of {ticked.Count} students was changed by Rs {dialog.Amount:N0}.");
        }
    }

    // Reason logged with the next save for students changed by Increase fee; others get the default reason.
    private readonly Dictionary<int, string> _increaseReasons = [];

    public override bool SaveChanges()
    {
        if (_db is null || _loading) return true;
        _grid.EndEdit();
        if (!_isAdmin) return true;
        var today = DateTime.Today;
        var now = DateTime.Now;
        foreach (var e in _db.ChangeTracker.Entries<FeeDefinition>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            var d = e.Entity;
            if (e.State == EntityState.Added && new[] { d.TuitionFee, d.ExamsFee, d.RegistrationFee, d.ProspectusFee, d.Other, d.AcFee, d.AdmissionFee }.All(v => (v ?? 0) == 0))
            {
                e.State = EntityState.Detached; // a cell was opened but nothing was charged
                foreach (var r in _list?.AllItems.Where(r => ReferenceEquals(r.Definition, d)) ?? []) r.Definition = null;
                continue;
            }
            var oldFee = e.State == EntityState.Added ? 0 : e.Property(x => x.TuitionFee).OriginalValue ?? 0;
            if (e.State == EntityState.Modified && !e.Properties.Any(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
            {
                e.State = EntityState.Unchanged; // typed the same value again
                continue;
            }
            if (e.State == EntityState.Modified)
            {
                d.RevisionDate = today;
                d.RevisionNo = (d.RevisionNo ?? 0) + 1;
            }
            var newFee = d.TuitionFee ?? 0;
            if (newFee != oldFee && d.StudentId is int cc)
                _db.FeeIncreases.Add(new FeeIncrease
                {
                    StudentId = cc, CurrentFee = oldFee, IncreasedAmount = newFee - oldFee, NewFee = newFee, UpdatedOn = now,
                    Reason = _increaseReasons.GetValueOrDefault(cc) ?? $"Changed in Fee definition by {Ctx.Session.UserName}",
                });
        }
        try
        {
            _db.SaveChanges();
            _increaseReasons.Clear();
            _grid.Invalidate();
            RaiseStatusChanged();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            Inform("Someone else changed these fee definitions while you were editing. Your changes were not saved; the list will reload.");
            _ = LoadAsync();
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save the fee definitions.", ex);
            return false;
        }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Fee definition", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    /// <summary>Asks how much to add to the monthly fee, and why.</summary>
    private sealed class IncreaseFeeForm : KryptonForm
    {
        private readonly KryptonNumericUpDown _amount = new() { Minimum = -50000, Maximum = 50000, Increment = 50, ThousandsSeparator = true, Width = 140 };
        private readonly KryptonTextBox _reason = new() { Width = 320, Text = $"Annual fee increase {DateTime.Today:yyyy}" };

        public int Amount => (int)_amount.Value;
        public string Reason => _reason.Text.Trim();

        public IncreaseFeeForm(int students)
        {
            Text = "Increase fee";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            Shown += (_, _) => Core.Theme.FitButtons(this); // buttons as wide as their text at any font size
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            // Sized from the font, so it stays readable with the larger text sizes in Settings.
            var row = Theme.RowHeight;
            ClientSize = new Size(Math.Max(520, TextRenderer.MeasureText(new string('M', 40), Theme.BaseFont).Width), row * 9);
            var fields = new FieldPanel(1) { Padding = new Padding(20, 16, 20, 0) };
            var info = new KryptonLabel { Text = $"Add to the monthly fee of {students} ticked students.\nA minus amount lowers it.", AutoSize = true };
            _amount.Height = _reason.Height = row + 2;
            fields.Add("", info);
            fields.Add("Amount (Rs)", _amount);
            fields.Add("Reason (kept in the fee increase log)", _reason);
            var ok = new KryptonButton { Text = "Increase", DialogResult = DialogResult.OK, Size = new Size(120, row + 8) };
            var cancel = new KryptonButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(120, row + 8) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = row + 28, Padding = new Padding(16, 8, 16, 8), BackColor = Color.Transparent };
            buttons.Controls.AddRange([cancel, ok]);
            Theme.MainButton(ok);
            ok.Click += (_, e) => { if (Amount == 0) { DialogResult = DialogResult.None; _amount.Focus(); } };
            AcceptButton = ok;
            CancelButton = cancel;
            var host = new KryptonPanel { Dock = DockStyle.Fill };
            host.Controls.Add(fields);
            host.Controls.Add(buttons);
            Controls.Add(host);
            _amount.Value = 300;
        }
    }
}
