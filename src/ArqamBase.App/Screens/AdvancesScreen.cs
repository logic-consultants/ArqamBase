using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Advances to staff and the Advance Request Form: who, how much, in how many equal instalments, the witnesses and the office details.
/// The salary sheet deducts the instalments; what is left comes from those deductions, oldest advance first.
/// </summary>
public sealed class AdvancesScreen : ScreenBase
{
    public sealed class Row
    {
        public required StaffAdvance Advance { get; init; }
        public DateTime Date => Advance.Date;
        public string Name { get; init; } = "";
        public decimal Amount => Advance.Amount;
        public int Installments => Advance.Installments;
        public decimal Each => Advance.Installment;
        public string From => Payroll.PeriodText(Payroll.PeriodMonth(Advance.FirstPeriod));
        public decimal Deducted => Advance.Amount - Left;
        public decimal Left { get; init; }
        public string Status => Left > 0 ? "Open" : "Paid back";
    }

    private sealed record Person(int Id, string Name, string Father, string? Salute, int? BranchId);

    private readonly KryptonTextBox _search = new() { Width = 240, CueHint = { CueHintText = "Search name" } };
    private readonly ComboBox _show = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AccessGrid _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
    };
    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly ComboBox _staff = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly KryptonTextBox _father = new() { ReadOnly = true };
    private readonly KryptonTextBox _school = new() { ReadOnly = true };
    private readonly NumericUpDown _amount = new() { Maximum = 100_000_000, Increment = 1000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
    private readonly NumericUpDown _installments = new() { Minimum = 1, Maximum = 120, Value = 1 };
    private readonly KryptonTextBox _each = new() { ReadOnly = true, TextAlign = HorizontalAlignment.Right };
    private readonly ComboBox _first = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonTextBox _witness1 = new(), _witness2 = new(), _witness3 = new();
    private readonly KryptonTextBox _paidThrough = new(), _recommended = new(), _approved = new(), _remarks = new();
    private readonly KryptonLabel _note = new() { Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0), StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly Card _form = new("Advance request form");
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private FilterBindingList<Row>? _list;
    private List<Person> _people = [];
    private Dictionary<int, string> _schools = [];
    private StaffAdvance? _current;
    private bool _showing, _dirty, _loading;

    public AdvancesScreen(ScreenContext ctx) : base(ctx)
    {
        _show.Items.AddRange(["Open advances", "All advances"]);
        _show.SelectedIndex = 0;
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        foreach (var m in Enumerable.Range(0, 7).Select(month.AddMonths)) _first.Items.Add(new Inputs.Item<string>(Payroll.PeriodKey(m), Payroll.PeriodText(m)));
        _first.DisplayMember = nameof(Inputs.Item<string>.Text);
        BuildGrid();

        var fields = new FieldPanel(4);
        fields.Add("Date", _date);
        fields.Add("Employee's name", _staff);
        fields.Add("Father / spouse's name", _father);
        fields.Add("School", _school);
        fields.Add("Advance amount (Rs)", _amount);
        fields.Add("No. of instalments", _installments);
        fields.Add("Each instalment (Rs)", _each);
        fields.Add("First deduction from salary of", _first);
        fields.Add("Witness 1", _witness1);
        fields.Add("Witness 2", _witness2);
        fields.Add("Witness 3", _witness3);
        fields.Add("Advance paid through", _paidThrough);
        fields.Add("Recommended by", _recommended);
        fields.Add("Approved by", _approved);
        fields.Add("Remarks", _remarks, 2);
        _form.AddTop(fields);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 6) };
        bar.Controls.AddRange([_search, _show]);
        _note.Text = "Save the advance, then press Print form. Each month's salary sheet deducts the instalment; you can change it there for one month.";
        Controls.Add(_grid);
        Controls.Add(_form);
        Controls.Add(_note);
        Controls.Add(bar);
        _form.Dock = DockStyle.Bottom;

        _search.TextChanged += (_, _) => ApplyFilters();
        _show.SelectedIndexChanged += (_, _) => ApplyFilters();
        _grid.SelectionChanged += (_, _) => { if (!_loading && _grid.CurrentRow?.DataBoundItem is Row r && !ReferenceEquals(r.Advance, _current)) Pick(r.Advance); };
        _date.ValueChanged += (_, _) => Touched();
        _staff.SelectedIndexChanged += (_, _) => { ShowPerson(); Touched(); };
        _amount.ValueChanged += (_, _) => { ShowEach(); Touched(); };
        _installments.ValueChanged += (_, _) => { ShowEach(); Touched(); };
        _first.SelectedIndexChanged += (_, _) => Touched();
        foreach (var box in new[] { _witness1, _witness2, _witness3, _paidThrough, _recommended, _approved, _remarks })
            box.TextChanged += (_, _) => Touched();

        _commands =
        [
            new("Advance", new ScreenCommand("Print form", Icons.Print, PrintForm), new ScreenCommand("Blank form", Icons.Document, PrintBlank)),
        ];
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New advance";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => _dirty;

    public override string StatusText
    {
        get
        {
            var rows = _list?.AllItems ?? [];
            return $"{rows.Count(r => r.Left > 0):N0} open advances · Rs {rows.Sum(r => r.Left):N0} still owed{(_dirty ? "  ·  unsaved changes" : "")}";
        }
    }

    private void Touched()
    {
        if (_showing) return;
        _dirty = true;
        RaiseStatusChanged();
    }

    public override void Reload()
    {
        var keep = _current?.Id;
        _loading = true;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            _people = db.Staff.AsNoTracking().OrderBy(s => s.Name).AsEnumerable()
                .Select(s => new Person(s.Id, s.Name?.Trim() ?? "", s.FatherName?.Trim() ?? "", s.Salute, s.BranchId)).ToList();
            _schools = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => s.Name?.Trim() ?? "");
            var advances = db.StaffAdvances.AsNoTracking().ToList();
            var deducted = db.SalaryLines.AsNoTracking().GroupBy(l => l.StaffId)
                .Select(g => new { g.Key, Sum = g.Sum(l => l.AdvanceDeducted ?? 0) }).ToDictionary(x => x.Key, x => x.Sum);
            var names = _people.ToDictionary(p => p.Id, p => p.Name);
            var rows = advances.GroupBy(a => a.StaffId)
                .SelectMany(g => Payroll.AdvancesLeft(g, deducted.GetValueOrDefault(g.Key)))
                .Select(x => new Row { Advance = x.Advance, Name = names.GetValueOrDefault(x.Advance.StaffId, ""), Left = x.Left })
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.Advance.Id).ToList();
            _showing = true;
            _staff.DataSource = _people.Select(p => new Inputs.Item<int>(p.Id, p.Name)).ToList();
            _staff.DisplayMember = nameof(Inputs.Item<int>.Text);
            _showing = false;
            _list = new FilterBindingList<Row>(rows);
            ApplyFilters();
            _grid.Bind(_list);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load advances. Has sql/staff_payroll_install.sql been run on this database?", ex);
        }
        finally { _loading = false; }
        var row = _list?.Cast<Row>().FirstOrDefault(r => r.Advance.Id == keep) ?? _list?.Cast<Row>().FirstOrDefault();
        if (row is not null) Pick(row.Advance); else NewForm();
        RaiseStatusChanged();
    }

    private void ApplyFilters()
    {
        if (_list is null) return;
        var term = _search.Text.Trim();
        var open = _show.SelectedIndex == 0;
        _list.SetSearch(r => (!open || r.Left > 0) && (term.Length == 0 || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void Pick(StaffAdvance advance)
    {
        if (!ConfirmLeave()) return;
        _dirty = false;
        ShowForm(advance);
    }

    private void NewForm()
    {
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ShowForm(new StaffAdvance { Date = DateTime.Today, FirstPeriod = Payroll.PeriodKey(month) });
    }

    private void ShowForm(StaffAdvance a)
    {
        _showing = true;
        _current = a;
        _date.Value = a.Date;
        _staff.SelectedIndex = _people.FindIndex(p => p.Id == a.StaffId);
        _amount.Value = Math.Min(a.Amount, _amount.Maximum);
        _installments.Value = Math.Clamp(a.Installments, 1, 120);
        var i = _first.Items.Cast<Inputs.Item<string>>().ToList().FindIndex(x => x.Value == a.FirstPeriod);
        if (i < 0) i = _first.Items.Add(new Inputs.Item<string>(a.FirstPeriod, Payroll.PeriodText(Payroll.PeriodMonth(a.FirstPeriod))));
        _first.SelectedIndex = i;
        (_witness1.Text, _witness2.Text, _witness3.Text) = (a.Witness1 ?? "", a.Witness2 ?? "", a.Witness3 ?? "");
        (_paidThrough.Text, _recommended.Text, _approved.Text, _remarks.Text) = (a.PaidThrough ?? "", a.RecommendedBy ?? "", a.ApprovedBy ?? "", a.Remarks ?? "");
        ShowPerson();
        ShowEach();
        _showing = false;
    }

    private Person? Chosen => _staff.SelectedItem is Inputs.Item<int> item ? _people.FirstOrDefault(p => p.Id == item.Value) : null;

    private string SchoolOf(Person? p) => p?.BranchId is int b && _schools.TryGetValue(b, out var name) ? name : PrintKit.DefaultSchoolName;

    private void ShowPerson()
    {
        var p = Chosen;
        _father.Text = p?.Father ?? "";
        _school.Text = p is null ? "" : SchoolOf(p);
    }

    private void ShowEach() =>
        _each.Text = new StaffAdvance { Amount = _amount.Value, Installments = (int)_installments.Value }.Installment.ToString("N0");

    public override void NewRecord()
    {
        if (!ConfirmLeave()) return;
        _dirty = false;
        NewForm();
        _staff.Focus();
    }

    public override bool SaveChanges()
    {
        if (!_dirty || _current is null) return true;
        if (Chosen is not { } person) { Inform("Choose the employee."); return false; }
        if (_amount.Value <= 0) { Inform("Type the advance amount."); return false; }
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var a = _current.Id == 0 ? new StaffAdvance { CreatedBy = Ctx.Session.UserName } : db.StaffAdvances.First(x => x.Id == _current.Id);
            if (_current.Id == 0) db.StaffAdvances.Add(a);
            a.Date = _date.Value.Date;
            a.StaffId = person.Id;
            a.Amount = _amount.Value;
            a.Installments = (int)_installments.Value;
            a.FirstPeriod = ((Inputs.Item<string>)_first.SelectedItem!).Value!;
            a.Witness1 = Clean(_witness1);
            a.Witness2 = Clean(_witness2);
            a.Witness3 = Clean(_witness3);
            a.PaidThrough = Clean(_paidThrough);
            a.RecommendedBy = Clean(_recommended);
            a.ApprovedBy = Clean(_approved);
            a.Remarks = Clean(_remarks);
            db.SaveChanges();
            _current = a;
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or DbUpdateException or InvalidOperationException)
        {
            Program.ShowError("Could not save the advance.", ex);
            return false;
        }
        _dirty = false;
        Reload();
        return true;
    }

    private static string? Clean(KryptonTextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    public override void DeleteRecord()
    {
        if (_current is not { Id: > 0 } a) return;
        var row = _list?.AllItems.FirstOrDefault(r => r.Advance.Id == a.Id);
        if (row is { Deducted: > 0 }) { Inform("Some of this advance has been deducted from salary, so it cannot be deleted."); return; }
        if (KryptonMessageBox.Show($"Delete the advance of Rs {a.Amount:N0} to {row?.Name}?", "Delete advance",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            db.StaffAdvances.Remove(db.StaffAdvances.First(x => x.Id == a.Id));
            db.SaveChanges();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or DbUpdateException) { Program.ShowError("Could not delete the advance.", ex); return; }
        _current = null;
        _dirty = false;
        Reload();
    }

    public override void FocusSearch() => _search.Focus();

    private void PrintForm()
    {
        if (_dirty && !SaveChanges()) return;
        if (_current is not { Id: > 0 } || Chosen is not { } p) { Inform("Save the advance first, or press Blank form."); return; }
        var form = new AdvanceFormPrint(_date.Value.Date, p.Salute, p.Name, p.Father, SchoolOf(p), _amount.Value, (int)_installments.Value,
            Clean(_witness1), Clean(_witness2), Clean(_witness3), Clean(_paidThrough), Clean(_recommended), Clean(_approved));
        var school = SchoolDetails();
        Shell.PrintPreviewForm.Show(this, () => AdvanceForms.Create(form, school), $"Advance form · {p.Name}", $"Advance form {p.Name} {_date.Value:yyyy-MM-dd}");
    }

    /// <summary>The form with the names left empty, to fill by hand.</summary>
    private void PrintBlank()
    {
        const string line = "____________________";
        var form = new AdvanceFormPrint(null, null, line, line, PrintKit.DefaultSchoolName, 0, 0, null, null, null, null, null, null);
        var school = SchoolDetails();
        Shell.PrintPreviewForm.Show(this, () => AdvanceForms.Create(form, school), "Advance form", "Advance form");
    }

    private void BuildGrid()
    {
        _grid.AddText(nameof(Row.Date), "Date", 90, format: "dd/MM/yyyy");
        _grid.AddText(nameof(Row.Name), "Employee", 200, fill: true);
        foreach (var (p, h) in new[] { (nameof(Row.Amount), "Advance"), (nameof(Row.Each), "Each instalment"), (nameof(Row.Deducted), "Deducted"), (nameof(Row.Left), "Left") })
            _grid.AddText(p, h, 105, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Installments), "Instalments", 85);
        _grid.AddText(nameof(Row.From), "From", 85);
        _grid.AddText(nameof(Row.Status), "Status", 90);
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Advances", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
