using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// One student's fee challans by year, with the receive-fee panel (see ScreenDesigns\Student fee ledger – receive fee@2x.png).
/// Replaces the Access Ledger form. A receipt pays whole challans; one receipt number can pay several challans, also of the family.
/// Receipt numbers run in series per counter: Cash Receipt Boys has its own series, Back Receipt its own, the rest share the girls series.
/// </summary>
public sealed class FeeLedgerScreen : ScreenBase
{
    private const string Title = "Fee ledger";

    // A student in the find box.
    private sealed record StudentItem(int Cc, string Name, int? ClassId, int? SectionId, bool OnRoll);

    private readonly bool _canReceive, _isAdmin;
    private readonly KryptonTextBox _find = new() { CueHint = { CueHintText = "Find student: CC or name" } };
    private readonly LedgerHeader _header = new();
    private readonly ComboBox _year = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly KryptonCheckBox _showDeleted = new() { Text = "Show deleted entries", AutoSize = true };
    private readonly AccessGrid _grid = new()
    {
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, ShowFilters = false, AllowUserToOrderColumns = false, RowHeadersVisible = false,
    };

    // Receive fee panel.
    private readonly KryptonTextBox _receiptNo = new();
    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private readonly KryptonCheckedListBox _against = new() { CheckOnClick = true };
    private readonly KryptonTextBox _amount = new() { ReadOnly = true, TextAlign = HorizontalAlignment.Right };
    private readonly KryptonNumericUpDown _fine = new() { Maximum = 1_000_000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
    private readonly KryptonComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonTextBox _remarks = new();
    private readonly KryptonCheckBox _printReceipt = new() { Text = "Print receipt", Checked = true, AutoSize = true };
    private readonly KryptonCheckBox _whatsApp = new() { Text = "Send receipt on WhatsApp (phase 5)", Enabled = false, AutoSize = true };
    private readonly KryptonCheckBox _email = new() { Text = "Email receipt (phase 5)", Enabled = false, AutoSize = true };
    private readonly KryptonButton _clear = new() { Text = "Clear" }, _saveReceipt = new() { Text = "Save receipt" };
    private readonly Card _receiveCard = new("Receive fee");

    // Family panel.
    private readonly KryptonLabel _familyText = new() { AutoSize = true };
    private readonly KryptonLabel _familyBalance = new() { AutoSize = true, LabelStyle = LabelStyle.BoldControl };
    private readonly KryptonButton _openFamily = new() { Text = "Open family" }, _receiveFamily = new() { Text = "Receive for family" };

    private readonly IReadOnlyList<ScreenCommandGroup> _commands;

    private ArqamDbContext? _db;
    private bool _loading;
    private List<StudentItem> _students = [];
    private Dictionary<string, int> _findTexts = new(StringComparer.CurrentCultureIgnoreCase);
    private Dictionary<int, (string Name, string School)> _classes = [];
    private Dictionary<int, string> _sections = [];
    private List<CollectionType> _types = [];
    private Student? _student;
    private List<FeeChallan> _challans = [];
    private List<Student> _family = [];
    private List<FeeChallan> _familyUnpaid = [];
    private FeeChallan? _totalRow;
    private bool _familyMode;
    private int? _pendingCc;
    private (int Receipts, int Amount) _today;

    public FeeLedgerScreen(ScreenContext ctx) : base(ctx)
    {
        _canReceive = ctx.Session.CanEdit(AppArea.Fees);
        _isAdmin = ctx.Session.IsAdmin;

        // Header: find box, then the student and the totals (drawn).
        _find.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindStudent(); } };
        _find.TextChanged += (_, _) => { if (_findTexts.TryGetValue(_find.Text.Trim(), out var cc)) _ = LoadStudentAsync(cc); };
        _header.Controls.Add(_find);
        var headerCard = new Card();
        headerCard.AddTop(_header);

        // Left: year, grid, note.
        _year.SelectedIndexChanged += (_, _) => ShowChallans();
        _showDeleted.CheckedChanged += (_, _) => { if (_student is not null) _ = LoadStudentAsync(_student.Id); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        var hint = new KryptonLabel
        {
            AutoSize = true, Margin = new Padding(16, 6, 0, 0),
            Text = _canReceive ? "Unpaid challans are shaded · type in an unpaid challan to change it · right-click for more" : "Right-click for more",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        _showDeleted.Margin = new Padding(12, 6, 0, 0);
        bar.Controls.AddRange([_year, _showDeleted, hint]);
        var note = new KryptonLabel
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 8, Padding = new Padding(0, 6, 0, 0),
            Text = "Exams column holds exam fee and Annual Fund. Registration is stored in the old Misc column.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        _grid.ReadOnly = !_canReceive;
        _grid.CellFormatting += FormatCell;
        _grid.CellValueChanged += (_, _) => { if (!_loading) { ShowHeader(); RaiseStatusChanged(); } };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        };
        _grid.CellContextMenuStripNeeded += (_, e) => { if (e.RowIndex >= 0) e.ContextMenuStrip = RowMenu(); };
        var left = new KryptonPanel { Dock = DockStyle.Fill };
        left.Controls.Add(_grid);
        left.Controls.Add(note);
        left.Controls.Add(bar);

        // Right: receive fee and family.
        BuildReceivePanel();
        var familyCard = new Card("Family");
        var familyLine = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Color.Transparent };
        familyLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        familyLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        familyLine.Controls.Add(_familyText, 0, 0);
        familyLine.Controls.Add(_familyBalance, 1, 0);
        var familyButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.RowHeight + 16, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        familyButtons.Controls.AddRange([_openFamily, _receiveFamily]);
        _openFamily.Click += (_, _) => { if (_student?.FamilyId is int id) Ctx.OpenScreenAt("family", id); };
        _receiveFamily.Click += (_, _) => { _familyMode = !_familyMode; ShowAgainst(); };
        _receiveFamily.Enabled = _canReceive;
        familyCard.AddTop(familyLine, familyButtons);
        var rightStack = new StackPanel();
        rightStack.AddTop(_receiveCard, familyCard);
        var right = new KryptonPanel { Dock = DockStyle.Right, Width = 440, AutoScroll = true, Padding = new Padding(12, 0, 0, 0) };
        right.Controls.Add(rightStack);

        var body = new KryptonPanel { Dock = DockStyle.Fill };
        body.Controls.Add(left);
        body.Controls.Add(right);
        Controls.Add(body);
        Controls.Add(headerCard);

        _commands =
        [
            new("Fee",
                new("Receive fee", Icons.Money, () => (_against.Items.Count > 0 ? (Control)_saveReceipt : _find).Focus()),
                new("Add fee", Icons.Add, () => NotYet("Adding a fee comes with the New fee screen (next in phase 3).")),
                new("Auto insert", Icons.Refresh, () => NotYet("Auto insert comes with the Periodic fee update screen (phase 3)."))),
            new("Print",
                new("Receipt", Icons.Receipt, ReprintReceipt),
                new("Challan", Icons.Print, () => NotYet("Printing challans comes with the Print challans screen (phase 3).")),
                new("Ledger", Icons.Document, PrintLedger)),
            new("Send",
                new("WhatsApp", Icons.Chat, () => NotYet("Sending WhatsApp messages comes with the Messages screens (phase 5).")),
                new("Email", Icons.Mail, () => NotYet("Sending email comes with the Messages screens (phase 5)."))),
        ];
        SizeControls();
        Theme.Changed += OnThemeChanged;
        HandleCreated += (_, _) => _ = LoadListsAsync();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "Add fee";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;

    public override string StatusText
    {
        get
        {
            var today = $"Today: {_today.Receipts} receipt{(_today.Receipts == 1 ? "" : "s")} · Rs {_today.Amount:N0}";
            if (_student is null) return today;
            var unpaid = _challans.Count(c => !c.Paid && !c.IsDeleted);
            return $"CC {_student.Id} · {_challans.Count(c => !c.IsDeleted)} challans · {unpaid} unpaid{(HasChanges ? " · unsaved changes" : "")}    {today}";
        }
    }

    private void NotYet(string message) =>
        KryptonMessageBox.Show(this, message, Title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    // ----- Layout -----

    private void BuildReceivePanel()
    {
        var fields = new FieldPanel(2);
        fields.Add("Receipt no.", _receiptNo);
        fields.Add("Date", _date);
        fields.Add("Against (whole challans)", _against, 2);
        fields.Add("Amount", _amount);
        fields.Add("Fine", _fine);
        fields.Add("Mode", _mode, 2);
        fields.Add("Remarks", _remarks, 2);
        fields.Add("", _printReceipt, 2);
        fields.Add("", _whatsApp, 2);
        fields.Add("", _email, 2);
        var buttons = new Panel { Height = Theme.RowHeight + 10, BackColor = Color.Transparent };
        _clear.Dock = DockStyle.Left;
        _saveReceipt.Dock = DockStyle.Right;
        buttons.Controls.AddRange([_clear, _saveReceipt]);
        fields.Add("", buttons, 2);
        fields.Enabled = _canReceive;
        _receiveCard.AddTop(fields);

        _against.ItemCheck += (_, _) => BeginInvoke(ShowAmount); // the checked state changes after this event
        _fine.ValueChanged += (_, _) => ShowAmount();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            if (_mode.SelectedItem is ModeItem m) { Ctx.Settings.FeeCollectionTypeId = m.Type.Id; TrySaveSettings(); }
            SuggestReceiptNo();
        };
        _clear.Click += (_, _) => ShowAgainst();
        _saveReceipt.Click += (_, _) => SaveReceipt();
    }

    // Sized from the text so they fit at every font size (Krypton's AutoSize buttons came out too wide).
    private void SizeControls()
    {
        int W(string text) => TextRenderer.MeasureText(text, Theme.BaseFont).Width + 28;
        var h = Theme.RowHeight + 2;
        foreach (var b in new[] { _clear, _saveReceipt, _openFamily, _receiveFamily })
            b.Size = new Size(W(b == _receiveFamily ? "Receive for family" : b.Text), h);
        _receiveFamily.Width = Math.Max(W("Receive for family"), W("Only this student"));
        _against.Height = Theme.RowHeight * 4;
        _find.Width = TextRenderer.MeasureText("0000 · MUHAMMAD ABDULLAH KHAN", Theme.BaseFont).Width + 20;
        _year.Width = W("All years") + 20;
        _header.Height = Theme.Scaled(1.6f).Height + Theme.Scaled(0.85f).Height + 34;
        _header.Invalidate();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SizeControls();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
            _db?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void TrySaveSettings()
    {
        try { Ctx.Settings.Save(); } catch (IOException) { }
    }

    // ----- Loading -----

    private sealed record Lists(List<StudentItem> Students, Dictionary<int, (string, string)> Classes, Dictionary<int, string> Sections,
        List<CollectionType> Types, (int, int) Today);

    /// <summary>The find list, class names and collection types (once, in the background).</summary>
    private async Task LoadListsAsync()
    {
        UseWaitCursor = true;
        try
        {
            var lists = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                var students = db.Students.OrderBy(s => s.Name)
                    .Select(s => new StudentItem(s.Id, s.Name ?? "", s.ClassId, s.SectionId, s.OnRoll)).ToList();
                var classes = (from c in db.Classes
                               join d in db.Departments on c.DepartmentId equals d.Id into ds
                               from d in ds.DefaultIfEmpty()
                               join sc in db.Schools on d.SchoolId equals sc.Id into ss
                               from sc in ss.DefaultIfEmpty()
                               select new { c.Id, c.Name, School = sc.Name }).AsEnumerable()
                    .ToDictionary(x => x.Id, x => (x.Name ?? "", x.School ?? ""));
                var sections = db.Sections.ToDictionary(s => s.Id, s => s.Name ?? "");
                var types = db.CollectionTypes.Where(t => t.Active != false).OrderBy(t => t.Id).ToList();
                return new Lists(students, classes, sections, types, TodayTotals(db));
            });
            if (IsDisposed) return;
            (_students, _classes, _sections, _types, _today) = (lists.Students, lists.Classes, lists.Sections, lists.Types, lists.Today);

            _findTexts = new(StringComparer.CurrentCultureIgnoreCase);
            var source = new AutoCompleteStringCollection();
            foreach (var s in _students)
            {
                var cls = ClassText(s.ClassId, s.SectionId);
                foreach (var text in new[] { $"{s.Cc} · {s.Name}{(cls.Length > 0 ? " · " + cls : "")}", $"{s.Name} · {s.Cc}{(cls.Length > 0 ? " · " + cls : "")}" })
                    if (_findTexts.TryAdd(text, s.Cc)) source.Add(text);
            }
            _find.AutoCompleteCustomSource = source;
            _find.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _find.AutoCompleteMode = AutoCompleteMode.Suggest;

            _mode.Items.Clear();
            foreach (var t in _types) _mode.Items.Add(new ModeItem(t));
            var saved = _types.FindIndex(t => t.Id == Ctx.Settings.FeeCollectionTypeId);
            _mode.SelectedIndex = _types.Count == 0 ? -1 : Math.Max(0, saved);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the student list.", ex);
        }
        finally { UseWaitCursor = false; }

        ShowStudent();
        if (_pendingCc is int cc) { _pendingCc = null; await LoadStudentAsync(cc); }
        RaiseStatusChanged();
    }

    private sealed record ModeItem(CollectionType Type)
    {
        public override string ToString() => Type.Name ?? $"Type {Type.Id}";
    }

    private string ClassText(int? classId, int? sectionId)
    {
        var cls = classId is int c && _classes.TryGetValue(c, out var ci) ? ci.Name : "";
        var sec = sectionId is int s && _sections.TryGetValue(s, out var sn) ? sn : "";
        return sec.Length > 0 ? $"{cls} · {sec}" : cls;
    }

    /// <summary>Receipts and money received today (by everyone).</summary>
    private static (int, int) TodayTotals(ArqamDbContext db)
    {
        var today = DateTime.Today;
        var rows = db.FeeChallans.Where(c => c.Paid && c.ReceivedOn == today)
            .Select(c => new { c.CollectionTypeId, c.ReceiptNo, c.ReceivedAmount }).ToList();
        return (rows.Select(r => (r.CollectionTypeId, r.ReceiptNo)).Distinct().Count(), rows.Sum(r => r.ReceivedAmount ?? 0));
    }

    private void FindStudent()
    {
        var text = _find.Text.Trim();
        if (text.Length == 0) return;
        if (_findTexts.TryGetValue(text, out var exact)) { _ = LoadStudentAsync(exact); return; }
        // A CC typed or scanned from a challan, or the start of a name.
        var digits = text.Split(' ', '·')[0];
        if (int.TryParse(digits, out var cc) && _students.Any(s => s.Cc == cc)) { _ = LoadStudentAsync(cc); return; }
        var byName = _students.Where(s => s.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (byName.Count(s => s.OnRoll) == 1) _ = LoadStudentAsync(byName.First(s => s.OnRoll).Cc);
        else if (byName.Count == 1) _ = LoadStudentAsync(byName[0].Cc);
        else NotYet(byName.Count == 0 ? $"No student matches \"{text}\"." : $"{byName.Count} students match \"{text}\". Pick one from the list as you type, or enter the CC.");
    }

    public override void Navigate(object target)
    {
        if (target is not int cc) return;
        if (_students.Count == 0) _pendingCc = cc;
        else _ = LoadStudentAsync(cc);
    }

    public override void Reload()
    {
        if (_student is not null) _ = LoadStudentAsync(_student.Id, askToSave: false);
    }

    private sealed record StudentData(Student? Student, List<FeeChallan> Challans, List<Student> Family, List<FeeChallan> FamilyUnpaid,
        int? MonthlyFee, (int, int) Today);

    private async Task LoadStudentAsync(int cc, bool askToSave = true)
    {
        if (_loading) return;
        if (askToSave && _student?.Id != cc && !ConfirmLeave()) return;
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        var showDeleted = _showDeleted.Checked;
        UseWaitCursor = true;
        try
        {
            _loading = true;
            StudentData data;
            try
            {
                data = await Task.Run(() =>
                {
                    var s = db.Students.Include(x => x.Family).FirstOrDefault(x => x.Id == cc);
                    var challans = (showDeleted ? db.FeeChallans.IgnoreQueryFilters() : db.FeeChallans)
                        .Where(c => c.StudentId == cc).OrderBy(c => c.IssueDate).ThenBy(c => c.Id).ToList();
                    var family = s?.FamilyId is int f
                        ? db.Students.Where(x => x.FamilyId == f && (x.OnRoll || x.Id == cc)).OrderBy(x => x.Name).ToList()
                        : s is null ? [] : [s];
                    var ids = family.Select(x => (int?)x.Id).ToList();
                    var unpaid = db.FeeChallans.Where(c => !c.Paid && ids.Contains(c.StudentId)).OrderBy(c => c.IssueDate).ThenBy(c => c.Id).ToList();
                    return new StudentData(s, challans, family, unpaid, db.MonthlyFeeOf(cc), TodayTotals(db));
                });
            }
            finally { _loading = false; }
            if (!ReferenceEquals(db, _db) || IsDisposed) return;

            _student = data.Student;
            _challans = data.Challans;
            _family = data.Family;
            _familyUnpaid = data.FamilyUnpaid;
            _header.MonthlyFee = data.MonthlyFee;
            _today = data.Today;
            if (_student is null) NotYet($"There is no student with CC {cc}.");
            _familyMode = false;
            FillYears();
            ShowStudent();
            _find.Text = "";
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the fee ledger.", ex);
        }
        finally { UseWaitCursor = false; }
        RaiseStatusChanged();
    }

    // ----- Showing the student -----

    /// <summary>Academic years (July to June) of the student's challans, newest first; the current year is chosen.</summary>
    private void FillYears()
    {
        var years = _challans.Where(c => c.IssueDate is not null).Select(c => YearOf(c.IssueDate!.Value)).Distinct().OrderDescending().ToList();
        var current = YearOf(DateTime.Today);
        _year.Items.Clear();
        foreach (var y in years) _year.Items.Add(YearText(y));
        _year.Items.Add("All years");
        var pick = years.Contains(current) ? years.IndexOf(current) : 0;
        _year.SelectedIndex = years.Count == 0 ? _year.Items.Count - 1 : pick; // shows the challans
    }

    private static int YearOf(DateTime d) => d.Month >= 7 ? d.Year : d.Year - 1;
    private static string YearText(int y) => $"{y}-{(y + 1) % 100:00}";

    private bool InSelectedYear(FeeChallan c) =>
        _year.SelectedItem is not string text || text == "All years" || (c.IssueDate is { } d && YearText(YearOf(d)) == text);

    private void ShowStudent()
    {
        ShowChallans();
        ShowAgainst();
        ShowFamily();
    }

    private void ShowChallans()
    {
        var rows = _challans.Where(InSelectedYear).ToList();
        _totalRow = new FeeChallan
        {
            Period = "Total",
            TuitionFee = Sum(c => c.TuitionFee), ExamsFee = Sum(c => c.ExamsFee), RegistrationFee = Sum(c => c.RegistrationFee),
            AdmissionFee = Sum(c => c.AdmissionFee), ProspectusFee = Sum(c => c.ProspectusFee), Fine = Sum(c => c.Fine), Other = Sum(c => c.Other),
            ReceivedAmount = Sum(c => c.Paid ? c.ReceivedAmount : 0),
        };
        int Sum(Func<FeeChallan, int?> f) => rows.Where(c => !c.IsDeleted).Sum(c => f(c) ?? 0);
        if (rows.Count > 0) rows.Add(_totalRow);

        _loading = true;
        _grid.ResetColumns();
        _grid.AddText(nameof(FeeChallan.PeriodText), "Period", 90, readOnly: true);
        _grid.AddText(nameof(FeeChallan.IssueDate), "Issued", 90, readOnly: true, format: "dd/MM/yyyy");
        _grid.AddText(nameof(FeeChallan.DueDate), "Due", 70, readOnly: true, format: "dd/MM");
        foreach (var (property, header) in new[]
                 {
                     (nameof(FeeChallan.TuitionFee), "Tuition"), (nameof(FeeChallan.ExamsFee), "Exams/AF"), (nameof(FeeChallan.RegistrationFee), "Registration"),
                     (nameof(FeeChallan.AdmissionFee), "Admission"), (nameof(FeeChallan.ProspectusFee), "Prospectus"), (nameof(FeeChallan.Fine), "Fine"),
                     (nameof(FeeChallan.Other), "Other"),
                 })
            Amount(_grid.AddText(property, header, 82, format: "N0"));
        Amount(_grid.AddText(nameof(FeeChallan.Total), "Total", 82, readOnly: true, format: "N0"));
        Amount(_grid.AddText(nameof(FeeChallan.ReceivedAmount), "Received", 82, readOnly: true, format: "N0"));
        _grid.AddText(nameof(FeeChallan.ReceivedOn), "On", 90, readOnly: true, format: "dd/MM/yyyy");
        _grid.AddText(nameof(FeeChallan.ReceiptNo), "Receipt", 70, readOnly: true);
        var mode = _grid.AddCombo(nameof(FeeChallan.CollectionTypeId), "Mode", _types.Select(t => ((int?)t.Id, t.Code ?? t.Name ?? "")), 70);
        mode.ReadOnly = true;
        mode.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        _grid.AddText(nameof(FeeChallan.EditedBy), "By", 110, readOnly: true, fill: true);
        foreach (DataGridViewColumn col in _grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
        // Only unpaid challans can be changed; paid ones are cancelled first (Admin).
        _grid.CanEditCell = (row, _) => _canReceive && row >= 0 && _grid.Rows[row].DataBoundItem is FeeChallan c
                                        && !ReferenceEquals(c, _totalRow) && !c.Paid && !c.IsDeleted;
        _grid.Bind(new FilterBindingList<FeeChallan>(rows));
        _loading = false;
        if (_grid.Rows.Count > 1)
        {
            var last = _grid.Rows.Count - 2; // the newest challan, above the total
            _grid.CurrentCell = _grid.Rows[last].Cells[0];
            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, last - 8);
        }
        ShowHeader();
        RaiseStatusChanged();

        static void Amount(DataGridViewColumn c) => c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not FeeChallan c || e.CellStyle is null) return;
        if (ReferenceEquals(c, _totalRow))
        {
            e.CellStyle.Font = Theme.Scaled(1f, FontStyle.Bold);
            e.CellStyle.BackColor = Theme.IsDark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xF2, 0xF2, 0xF2);
            if (_grid.Columns[e.ColumnIndex].DataPropertyName is nameof(FeeChallan.IssueDate) or nameof(FeeChallan.DueDate)) e.Value = "";
            return;
        }
        if (c.IsDeleted)
        {
            e.CellStyle.ForeColor = SystemColors.GrayText;
            e.CellStyle.Font = Theme.Scaled(1f, FontStyle.Strikeout);
        }
        else if (!c.Paid)
            e.CellStyle.BackColor = Theme.IsDark ? Color.FromArgb(0x4A, 0x30, 0x30) : Color.FromArgb(0xFB, 0xE3, 0xE3);
    }

    private void ShowHeader()
    {
        var s = _student;
        _header.Student = s;
        if (s is not null)
        {
            var year = _challans.Where(c => !c.IsDeleted && InSelectedYear(c)).ToList();
            _header.ClassText = ClassText(s.ClassId, s.SectionId);
            _header.Charged = year.Sum(c => c.Total);
            _header.Received = year.Where(c => c.Paid).Sum(c => c.ReceivedAmount ?? 0);
            _header.Balance = _challans.Where(c => !c.Paid && !c.IsDeleted).Sum(c => c.Total);
            _header.YearText = _year.SelectedItem as string ?? "";
        }
        _header.Invalidate();
    }

    /// <summary>The unpaid challans the receipt can pay: this student's, or the whole family's. All are ticked to start with.</summary>
    private void ShowAgainst()
    {
        _against.Items.Clear();
        var list = _familyMode ? _familyUnpaid : _familyUnpaid.Where(c => c.StudentId == _student?.Id).ToList();
        foreach (var c in list.Where(c => !c.IsDeleted && !c.Paid))
            _against.SetItemChecked(_against.Items.Add(new AgainstItem(c, _familyMode ? NameOf(c.StudentId) : null)), true);
        _receiveFamily.Text = _familyMode ? "Only this student" : "Receive for family";
        _receiveFamily.Enabled = _canReceive && _family.Count > 1;
        _fine.Value = 0;
        _remarks.Text = "";
        _date.Value = DateTime.Today;
        SuggestReceiptNo();
        ShowAmount();
        _receiveCard.Enabled = _student is not null;
    }

    private string NameOf(int? cc) => _family.FirstOrDefault(s => s.Id == cc)?.Name ?? $"CC {cc}";

    private sealed record AgainstItem(FeeChallan Challan, string? StudentName)
    {
        public override string ToString() =>
            $"{(StudentName is null ? "" : StudentName + " · ")}{Challan.PeriodText} · Rs {Challan.Total:N0}";
    }

    private List<FeeChallan> Ticked() => _against.CheckedItems.Cast<AgainstItem>().Select(i => i.Challan).ToList();

    private void ShowAmount() => _amount.Text = (Ticked().Sum(c => c.Total) + (int)_fine.Value).ToString("N0");

    private void ShowFamily()
    {
        var onRoll = _family.Count(s => s.OnRoll);
        var balance = _familyUnpaid.Where(c => !c.IsDeleted).Sum(c => c.Total);
        _familyText.Text = _student is null ? "" : $"{onRoll} {(onRoll == 1 ? "child" : "children")} · family balance";
        _familyBalance.Text = _student is null ? "" : $"Rs {balance:N0}";
        _familyBalance.StateCommon.ShortText.Color1 = balance > 0 ? Theme.Warning : Theme.Good;
        _openFamily.Enabled = _student?.FamilyId is not null;
    }

    // ----- Receipt numbers -----

    /// <summary>The collection types that share a receipt number series with this one (see the class summary).</summary>
    private List<int?> SeriesOf(CollectionType type)
    {
        static string Key(CollectionType t) => (t.Code ?? "").Trim().ToUpperInvariant() switch { "CRB" => "boys", "BR" => "back", _ => "girls" };
        var key = Key(type);
        return _types.Where(t => Key(t) == key).Select(t => (int?)t.Id).ToList();
    }

    /// <summary>The start of the academic year (1 July): receipt books start again about then.</summary>
    private static DateTime YearStart => new(YearOf(DateTime.Today), 7, 1);

    /// <summary>
    /// Suggests the number after the highest one used on the latest day with receipts in this series
    /// (not the highest ever: receipt books start again, e.g. 2899 in June and 952 in October).
    /// </summary>
    private void SuggestReceiptNo()
    {
        if (_db is null || _loading || _mode.SelectedItem is not ModeItem m) { _receiptNo.Text = ""; return; }
        try
        {
            var series = SeriesOf(m.Type);
            var paid = _db.FeeChallans.Where(c => c.Paid && c.ReceivedOn <= DateTime.Today && series.Contains(c.CollectionTypeId));
            var lastDay = paid.Max(c => c.ReceivedOn);
            var last = lastDay is null ? 0 : paid.Where(c => c.ReceivedOn == lastDay).Max(c => c.ReceiptNo) ?? 0;
            _receiptNo.Text = (last + 1).ToString();
        }
        catch (Microsoft.Data.SqlClient.SqlException) { _receiptNo.Text = ""; }
    }

    // ----- Receiving fee -----

    private void SaveReceipt()
    {
        if (_db is null || _student is null || _loading || !_canReceive) return;
        var ticked = Ticked();
        string? problem = null;
        if (ticked.Count == 0) problem = "Tick the challans this receipt pays.";
        else if (!int.TryParse(_receiptNo.Text.Trim(), out var n) || n <= 0) problem = "Enter the receipt number.";
        else if (_mode.SelectedItem is not ModeItem) problem = "Choose the mode.";
        else if (_date.Value.Date > DateTime.Today) problem = "The date cannot be in the future.";
        if (problem is not null) { NotYet(problem); return; }

        var receiptNo = int.Parse(_receiptNo.Text.Trim());
        var mode = ((ModeItem)_mode.SelectedItem!).Type;
        var date = _date.Value.Date;
        var series = SeriesOf(mode);
        var since = YearStart;
        try
        {
            var used = _db.FeeChallans.Where(c => c.Paid && c.ReceiptNo == receiptNo && c.ReceivedOn >= since && series.Contains(c.CollectionTypeId))
                .Select(c => new { c.ReceivedOn, c.StudentId }).FirstOrDefault();
            if (used is not null && KryptonMessageBox.Show(this,
                    $"Receipt no. {receiptNo} was already used on {used.ReceivedOn:dd/MM/yyyy} (CC {used.StudentId}).\n\nUse it again for this receipt?",
                    Title, KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            // A fine goes on the newest challan paid.
            var fine = (int)_fine.Value;
            if (fine > 0)
            {
                var newest = ticked.OrderByDescending(c => c.IssueDate).ThenByDescending(c => c.Id).First();
                newest.Fine = (newest.Fine ?? 0) + fine;
            }
            var remarks = _remarks.Text.Trim();
            foreach (var c in ticked)
            {
                c.Paid = true;
                c.ReceivedOn = date;
                c.ReceivedAmount = c.Total;
                c.ReceiptNo = receiptNo;
                c.CollectionTypeId = mode.Id;
                if (remarks.Length > 0) c.Remarks = remarks;
            }
            if (!Save()) return;

            if (_printReceipt.Checked) ShowReceipt(receiptNo, date, mode, ticked);
            _ = LoadStudentAsync(_student.Id, askToSave: false);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not save the receipt.", ex);
        }
    }

    /// <summary>Saves changes, stamping who changed each challan and when.</summary>
    private bool Save()
    {
        if (_db is null) return true;
        foreach (var entry in _db.ChangeTracker.Entries<FeeChallan>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.DateEdited = DateTime.Now;
            entry.Entity.EditedBy = Ctx.Session.UserName;
        }
        try
        {
            _db.SaveChanges();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            KryptonMessageBox.Show(this, "Someone else changed these challans at the same time (another receipt may have been saved). Nothing was saved; the ledger will now reload.",
                Title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            _ = LoadStudentAsync(_student!.Id, askToSave: false);
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save.", ex);
            return false;
        }
    }

    public override bool SaveChanges()
    {
        _grid.EndEdit();
        if (!HasChanges) return true;
        if (!Save()) return false;
        ShowStudent();
        RaiseStatusChanged();
        return true;
    }

    protected override void CommitPendingEdits() => _grid.EndEdit();

    public override void NewRecord() => NotYet("Adding a fee comes with the New fee screen (next in phase 3).");

    public override void DeleteRecord() => DeleteChallan();

    // ----- Row menu: cancel payment, delete, restore, receipt -----

    private FeeChallan? CurrentChallan() =>
        _grid.CurrentRow?.DataBoundItem is FeeChallan c && !ReferenceEquals(c, _totalRow) ? c : null;

    private ContextMenuStrip? RowMenu()
    {
        if (CurrentChallan() is not { } c) return null;
        var menu = new ContextMenuStrip();
        if (c.Paid) menu.Items.Add($"Print receipt {c.ReceiptNo}", null, (_, _) => ReprintReceipt());
        if (c.Paid && !c.IsDeleted) menu.Items.Add("Cancel payment…", null, (_, _) => CancelPayment()).Enabled = _isAdmin;
        if (!c.Paid && !c.IsDeleted) menu.Items.Add("Delete challan…", null, (_, _) => DeleteChallan()).Enabled = _isAdmin;
        if (c.IsDeleted) menu.Items.Add("Restore challan", null, (_, _) => RestoreChallan()).Enabled = _isAdmin;
        if (!_isAdmin) menu.Items.Add(new ToolStripLabel("Only an Admin can cancel, delete or restore") { ForeColor = SystemColors.GrayText });
        return menu;
    }

    private bool Ask(string question) =>
        KryptonMessageBox.Show(this, question, Title, KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) == DialogResult.Yes;

    private void CancelPayment()
    {
        if (!_isAdmin || CurrentChallan() is not { Paid: true } c) return;
        if (!Ask($"Cancel the payment of {c.PeriodText} (receipt {c.ReceiptNo}, Rs {c.ReceivedAmount:N0})? The challan becomes unpaid again.")) return;
        c.Paid = false;
        c.ReceivedOn = null;
        c.ReceivedAmount = 0;
        c.ReceiptNo = 0; // as on challans never paid
        c.CollectionTypeId = null;
        if (Save()) _ = LoadStudentAsync(_student!.Id, askToSave: false);
    }

    private void DeleteChallan()
    {
        if (CurrentChallan() is not { } c) return;
        if (!_isAdmin) { NotYet("Only an Admin can delete a challan."); return; }
        if (c.Paid) { NotYet("This challan is paid. Cancel the payment first."); return; }
        if (c.IsDeleted || !Ask($"Delete the {c.PeriodText} challan (Rs {c.Total:N0})? It can be shown again with \"Show deleted entries\".")) return;
        c.IsDeleted = true;
        c.DeletedOn = DateTime.Now;
        if (Save()) _ = LoadStudentAsync(_student!.Id, askToSave: false);
    }

    private void RestoreChallan()
    {
        if (!_isAdmin || CurrentChallan() is not { IsDeleted: true } c) return;
        c.IsDeleted = false;
        c.DeletedOn = null;
        if (Save()) _ = LoadStudentAsync(_student!.Id, askToSave: false);
    }

    // ----- Printing -----

    private void ReprintReceipt()
    {
        if (_db is null || CurrentChallan() is not { Paid: true, ReceiptNo: int no, ReceivedOn: DateTime date } c)
        {
            NotYet("Select a paid challan to print its receipt.");
            return;
        }
        var mode = _types.FirstOrDefault(t => t.Id == c.CollectionTypeId);
        try
        {
            List<int?> series = mode is null ? [c.CollectionTypeId] : SeriesOf(mode);
            var challans = _db.FeeChallans.AsNoTracking()
                .Where(x => x.Paid && x.ReceiptNo == no && x.ReceivedOn == date && series.Contains(x.CollectionTypeId))
                .OrderBy(x => x.StudentId).ThenBy(x => x.IssueDate).ToList();
            ShowReceipt(no, date, mode, challans);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not read the receipt.", ex);
        }
    }

    private void ShowReceipt(int receiptNo, DateTime date, CollectionType? mode, List<FeeChallan> challans)
    {
        var ids = challans.Select(c => c.StudentId ?? 0).Distinct().ToList();
        var students = _students.Where(s => ids.Contains(s.Cc)).ToDictionary(s => s.Cc);
        var lines = challans.Select(c =>
        {
            var s = students.GetValueOrDefault(c.StudentId ?? 0);
            return new Printing.ReceiptLine(c.StudentId ?? 0, s?.Name ?? "", s is null ? "" : ClassText(s.ClassId, s.SectionId),
                c.PeriodText, c.Total - (c.Fine ?? 0), c.Fine ?? 0, c.ReceivedAmount ?? c.Total);
        }).ToList();
        var first = students.GetValueOrDefault(challans.FirstOrDefault()?.StudentId ?? 0);
        var school = first?.ClassId is int cls && _classes.TryGetValue(cls, out var ci) && ci.School.Length > 0 ? ci.School : Printing.PrintKit.DefaultSchoolName;
        var by = challans.Select(c => c.EditedBy).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b)) ?? Ctx.Session.UserName;
        var receipt = new Printing.ReceiptPrint(receiptNo, date, mode?.Name ?? "", by, school,
            challans.Select(c => c.Remarks).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)), lines);
        var details = SchoolDetails();
        PrintPreviewForm.Show(this, () => Printing.Receipts.Create(receipt, details), $"Receipt {receiptNo}", $"Receipt {receiptNo}");
    }

    private void PrintLedger()
    {
        if (_student is null) { NotYet("Find a student first."); return; }
        var title = $"Fee ledger · {_student.Id} {_student.Name} · {_year.SelectedItem}";
        PrintPreviewForm.Show(this, () => Printing.ListExport.From(_grid, title).ToPdf(), "Fee ledger", $"Fee ledger {_student.Id} {_student.Name}");
    }

    // ----- Header -----

    /// <summary>The find box, then the student (initials, name, father, family, class, monthly fee, status) and the year's totals.</summary>
    private sealed class LedgerHeader : Panel
    {
        public Student? Student;
        public string ClassText = "";
        public string YearText = "";
        public int? MonthlyFee;
        public int Charged;
        public int Received;
        public int Balance;

        public LedgerHeader()
        {
            Dock = DockStyle.Top;
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private Control? Find => Controls.Count > 0 ? Controls[0] : null;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Find is { } f) f.Location = new Point(0, (Height - f.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var big = Theme.Scaled(1.15f, FontStyle.Bold);
            var small = Theme.Scaled(0.85f);
            var value = Theme.Scaled(1f);
            var balanceFont = Theme.Scaled(1.6f, FontStyle.Bold);
            var x = (Find?.Right ?? 0) + 24;
            if (Student is not { } s)
            {
                TextRenderer.DrawText(g, "Find a student by CC or name to see the fee ledger.", value, new Rectangle(x, 0, Width - x, Height), Theme.Muted,
                    flags | TextFormatFlags.VerticalCenter);
                return;
            }

            // Initials in a circle.
            var d = Height - 20;
            var circle = new Rectangle(x, 10, d, d);
            using (var fill = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillEllipse(fill, circle);
            var initials = string.Concat((s.Name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0]));
            TextRenderer.DrawText(g, initials, Theme.Scaled(1f, FontStyle.Bold), circle, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x = circle.Right + 14;

            // Totals on the right, measured first so the left side knows where to stop.
            var items = new List<(string Caption, string Text, Font Font, Color Color)>
            {
                ($"Charged {YearText}", $"Rs {Charged:N0}", value, Theme.Text),
                ("Received", $"Rs {Received:N0}", value, Theme.Text),
                ("Balance", $"Rs {Balance:N0}", balanceFont, Balance > 0 ? Theme.Warning : Theme.Good),
            };
            var right = Width;
            var top = (Height - small.Height - balanceFont.Height) / 2;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var (caption, text, font, color) = items[i];
                var w = Math.Max(TextRenderer.MeasureText(caption, small).Width, TextRenderer.MeasureText(text, font).Width);
                var left = right - w;
                TextRenderer.DrawText(g, caption, small, new Rectangle(left, top, w, small.Height), Theme.Muted, flags | TextFormatFlags.Right);
                var vy = top + small.Height + (font == balanceFont ? 0 : balanceFont.Height - font.Height);
                TextRenderer.DrawText(g, text, font, new Rectangle(left, vy, w, font.Height), color, flags | TextFormatFlags.Right);
                right = left - 24;
            }

            // Name and father, then class, monthly fee and status.
            var nameTop = (Height - big.Height - small.Height - 2) / 2;
            var nameWidth = Math.Min(320, Math.Max(120, right - x));
            TextRenderer.DrawText(g, s.Name ?? "", big, new Rectangle(x, nameTop, nameWidth, big.Height), Theme.Text, flags);
            var sub = $"{(s.Sex == "FEMALE" ? "D/o" : "S/o")} {s.FatherName} · Family {s.FamilyId}";
            TextRenderer.DrawText(g, sub, small, new Rectangle(x, nameTop + big.Height + 2, nameWidth, small.Height), Theme.Muted, flags);
            x += nameWidth + 24;
            foreach (var (caption, text, color) in new[]
                     {
                         ("Class", ClassText, Theme.Text),
                         ("Monthly fee", MonthlyFee is int f ? $"Rs {f:N0}" : "—", Theme.Text),
                         ("Status", s.StatusText, s.OnRoll ? Theme.Good : Theme.Muted),
                     })
            {
                var font = color == Theme.Good ? Theme.Scaled(1f, FontStyle.Bold) : value;
                var w = Math.Max(TextRenderer.MeasureText(caption, small).Width, TextRenderer.MeasureText(text, font).Width);
                if (x + w > right) break;
                TextRenderer.DrawText(g, caption, small, new Rectangle(x, nameTop, w, small.Height), Theme.Muted, flags);
                TextRenderer.DrawText(g, text, font,
                    new Rectangle(x, nameTop + small.Height + 2, w + 4, value.Height), color, flags);
                x += w + 24;
            }
        }
    }
}
