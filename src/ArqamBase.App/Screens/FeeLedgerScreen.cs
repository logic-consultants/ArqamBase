using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// One student's fee challans with the receive-fee panel (see ScreenDesigns\Student fee ledger – receive fee@2x.png).
/// Replaces the Access Ledger form. It opens on the unpaid challans; the last 12 months or everything since admission can be shown.
/// A receipt pays whole challans; one receipt number can pay several challans, also of the family. Only an Admin can change a challan.
/// Receipt numbers run in series: Cash Receipt Girls and Online Deposit Girls share one, Cash Receipt Boys and Online Deposit Boys
/// another (Online Deposit Boys used the girls numbers before <see cref="AppConfig.OnlineBoysOwnSeriesFrom"/>), Back Receipt its own.
/// </summary>
public sealed class FeeLedgerScreen : ScreenBase
{
    private const string Title = "Fee ledger";
    private const string ViewUnpaid = "Unpaid challans", ViewYear = "Last 12 months", ViewSinceAdmission = "Since admission";

    // A student in the find box.
    private sealed record StudentItem(int Cc, string Name, int? ClassId, int? SectionId, bool OnRoll);

    private readonly bool _canReceive, _isAdmin;
    private readonly KryptonTextBox _find = new() { CueHint = { CueHintText = "Find student: CC or name" } };
    private readonly LedgerHeader _header = new();
    private readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly KryptonCheckBox _allStudents = new() { Text = "Show left students", AutoSize = true };
    /// <summary>Students matching what is typed in the find box (any part of the name, or the start of the CC).</summary>
    private readonly ListBox _matches = new() { Visible = false, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, TabStop = false, FormattingEnabled = true };
    private readonly AccessGrid _totals = new()
    {
        Dock = DockStyle.None, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeColumns = false,
        AllowUserToOrderColumns = false, ColumnHeadersVisible = false, RowHeadersVisible = false, ScrollBars = ScrollBars.None, ShowFilters = false,
        TabStop = false,
    };
    private readonly Panel _totalsHost = new() { Dock = DockStyle.Bottom };
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
    private readonly ComboBox _paper = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonButton _attachSlip = new() { Text = "Attach bank slip…" };
    private readonly KryptonLabel _slipName = new() { AutoSize = true };
    private readonly Panel _slipRow = new() { BackColor = Color.Transparent };
    private string? _slipPath;
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
    // Receipts of this student that have a bank slip: (collection type, receipt no., date).
    private HashSet<(int, int, DateTime)> _slips = [];

    public FeeLedgerScreen(ScreenContext ctx) : base(ctx)
    {
        _canReceive = ctx.Session.CanEdit(AppArea.Fees);
        _isAdmin = ctx.Session.IsAdmin;

        // Header: find box, then the student and the totals (drawn).
        _find.KeyDown += FindKeyDown;
        _find.TextChanged += (_, _) => ShowMatches();
        _find.Leave += (_, _) => BeginInvoke(() => { if (!_matches.Focused) _matches.Hide(); });
        _allStudents.CheckedChanged += (_, _) => { _find.Focus(); ShowMatches(); };
        _matches.MouseClick += (_, _) => PickMatch();
        _matches.LostFocus += (_, _) => _matches.Hide();
        _matches.Format += (_, e) => e.Value = e.ListItem is StudentItem s ? MatchText(s) : "";
        Controls.Add(_matches);
        var findPanel = new Panel { BackColor = Color.Transparent };
        findPanel.Controls.AddRange([_find, _allStudents]);
        _header.Controls.Add(findPanel);
        var headerCard = new Card();
        headerCard.AddTop(_header);

        // Left: what to show, grid, note.
        _view.Items.AddRange([ViewUnpaid, ViewYear, ViewSinceAdmission]);
        _view.SelectedIndex = 0;
        _view.SelectedIndexChanged += (_, _) => ShowChallans();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        var hint = new KryptonLabel
        {
            AutoSize = true, Margin = new Padding(16, 6, 0, 0),
            Text = _isAdmin ? "Unpaid challans are shaded · type in an unpaid challan to change it · right-click for more"
                            : "Unpaid challans are shaded · only an Admin can change a challan · right-click for more",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        bar.Controls.AddRange([_view, hint]);
        var note = new KryptonLabel
        {
            Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 8, Padding = new Padding(0, 6, 0, 0),
            Text = "Exams column holds exam fee and Annual Fund. Registration is stored in the old Misc column.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        _grid.ReadOnly = !_isAdmin;
        _grid.CellFormatting += FormatCell;
        _grid.CellValueChanged += (_, _) => { if (!_loading) { ShowHeader(); RaiseStatusChanged(); } };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        };
        _grid.CellContextMenuStripNeeded += (_, e) => { if (e.RowIndex >= 0) e.ContextMenuStrip = RowMenu(); };
        var left = new KryptonPanel { Dock = DockStyle.Fill };
        // Totals under the list (grey, bold, never selected).
        var totalsBack = Theme.IsDark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xF2, 0xF2, 0xF2);
        _totals.DefaultCellStyle.BackColor = _totals.DefaultCellStyle.SelectionBackColor = totalsBack;
        _totals.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _totals.SelectionChanged += (_, _) => _totals.ClearSelection();
        _totalsHost.Controls.Add(_totals);
        _totalsHost.Resize += (_, _) => SyncTotals();
        _grid.ColumnWidthChanged += (_, _) => SyncTotals();
        _grid.Scroll += (_, e) => { if (e.ScrollOrientation == ScrollOrientation.HorizontalScroll) SyncTotals(); };
        _grid.CellContentClick += (_, e) => SlipLinkClicked(e.RowIndex, e.ColumnIndex);
        left.Controls.Add(_grid);
        left.Controls.Add(_totalsHost);
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
        var right = new KryptonPanel { Dock = DockStyle.Right, Width = 410, AutoScroll = true, Padding = new Padding(12, 0, 0, 0) };
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
                new("Add fee", Icons.Add, AddFee),
                new("Auto insert", Icons.Refresh, () => Ctx.OpenScreen("periodicfee"))),
            new("Print",
                new("Receipt", Icons.Receipt, ReprintReceipt),
                new("Challan", Icons.Print, PrintChallan),
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
        _slipRow.Height = Theme.RowHeight + 4;
        _attachSlip.Dock = DockStyle.Left;
        _slipName.Dock = DockStyle.Fill;
        _slipName.Padding = new Padding(8, 4, 0, 0);
        _slipRow.Controls.AddRange([_slipName, _attachSlip]);
        fields.Add("Bank / online deposit slip", _slipRow, 2);
        fields.Add("Remarks", _remarks, 2);
        fields.Add("", _printReceipt);
        fields.Add("Receipt paper", _paper);
        fields.Add("", _whatsApp, 2);
        fields.Add("", _email, 2);
        var buttons = new Panel { Height = Theme.RowHeight + 10, BackColor = Color.Transparent };
        _clear.Dock = DockStyle.Left;
        _saveReceipt.Dock = DockStyle.Right;
        buttons.Controls.AddRange([_clear, _saveReceipt]);
        fields.Add("", buttons, 2);
        fields.Enabled = _canReceive;
        _fine.Enabled = _isAdmin; // a fine changes the challan: Admin only
        _receiveCard.AddTop(fields);

        _paper.Items.AddRange(["A4 (laser printer)", "80 mm roll (receipt printer)"]);
        _paper.SelectedIndex = Ctx.Settings.ReceiptPaper == Printing.ReceiptPaper.Roll80 ? 1 : 0;
        _paper.SelectedIndexChanged += (_, _) =>
        {
            Ctx.Settings.ReceiptPaper = _paper.SelectedIndex == 1 ? Printing.ReceiptPaper.Roll80 : Printing.ReceiptPaper.A4;
            TrySaveSettings();
        };
        _attachSlip.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Pictures or PDF|*.jpg;*.jpeg;*.png;*.bmp;*.pdf|All files|*.*", Title = "Bank / online deposit slip" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _slipPath = dialog.FileName;
            ShowSlip();
        };
        _against.ItemCheck += (_, _) => BeginInvoke(ShowAmount); // the checked state changes after this event
        _fine.ValueChanged += (_, _) => ShowAmount();
        _mode.SelectedIndexChanged += (_, _) =>
        {
            if (_mode.SelectedItem is ModeItem m) { Ctx.Settings.FeeCollectionTypeId = m.Type.Id; TrySaveSettings(); }
            SuggestReceiptNo();
            ShowSlip();
        };
        _clear.Click += (_, _) => ShowAgainst();
        _saveReceipt.Click += (_, _) => SaveReceipt();
        Theme.MainButton(_saveReceipt);
    }

    // Sized from the text so they fit at every font size (Krypton's AutoSize buttons came out too wide).
    private void SizeControls()
    {
        int W(string text) => TextRenderer.MeasureText(text, Theme.BaseFont).Width + 28;
        var h = Theme.RowHeight + 2;
        foreach (var b in new[] { _clear, _saveReceipt, _openFamily, _receiveFamily, _attachSlip })
            b.Size = new Size(W(b == _receiveFamily ? "Receive for family" : b.Text), h);
        _receiveFamily.Width = Math.Max(W("Receive for family"), W("Only this student"));
        _slipRow.Height = h + 2;
        _against.Height = Theme.RowHeight * 4;
        _find.Width = TextRenderer.MeasureText("0000 · MUHAMMAD ABDULLAH KHAN", Theme.BaseFont).Width + 20;
        _find.Location = Point.Empty;
        _allStudents.Location = new Point(0, _find.Height + 2);
        _find.Parent!.Size = new Size(_find.Width, _allStudents.Bottom);
        _matches.Font = Theme.BaseFont;
        _view.Width = W(ViewSinceAdmission) + 30;
        _header.Height = Theme.Scaled(1.6f).Height + Theme.Scaled(0.85f).Height + 34;
        _totalsHost.Height = Theme.RowHeight + 2;
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

    /// <summary>
    /// Students whose name contains every word typed, in any order ("habib" or "abdullah habib" finds Muhammad Abdullah Habib),
    /// or whose CC starts with the number typed. Students who have left only when "Show left students" is ticked.
    /// </summary>
    private List<StudentItem> Matching(string text, bool all)
    {
        var words = text.Split([' ', '·'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var number = words.Length == 1 && words[0].All(char.IsDigit) ? words[0] : null;
        return _students
            .Where(s => all || s.OnRoll)
            .Where(s => number is not null
                ? s.Cc.ToString().StartsWith(number, StringComparison.Ordinal)
                : words.All(w => s.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderBy(s => number is not null ? s.Cc.ToString().Length : s.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(s => number is not null ? s.Cc : 0).ThenBy(s => s.Name)
            .ToList();
    }

    private string MatchText(StudentItem s)
    {
        var cls = ClassText(s.ClassId, s.SectionId);
        return $"{s.Cc} · {s.Name}{(cls.Length > 0 ? " · " + cls : "")}{(s.OnRoll ? "" : " · left")}";
    }

    private void ShowMatches()
    {
        var found = _find.ContainsFocus ? Matching(_find.Text.Trim(), _allStudents.Checked).Take(100).ToList() : [];
        if (found.Count == 0) { _matches.Hide(); return; }
        _matches.BeginUpdate();
        _matches.DataSource = found;
        _matches.SelectedIndex = 0;
        _matches.EndUpdate();
        var below = PointToClient(_find.Parent!.PointToScreen(new Point(_find.Left, _find.Bottom)));
        var width = Math.Max(_find.Width, found.Max(s => TextRenderer.MeasureText(MatchText(s), Theme.BaseFont).Width) + 30);
        var rows = Math.Min(found.Count, 12);
        _matches.SetBounds(below.X, below.Y, Math.Min(width, ClientSize.Width - below.X), rows * _matches.ItemHeight + 4);
        _matches.Show();
        _matches.BringToFront();
    }

    private void FindKeyDown(object? sender, KeyEventArgs e)
    {
        if (_matches.Visible && e.KeyCode is Keys.Down or Keys.Up or Keys.PageDown or Keys.PageUp)
        {
            var step = e.KeyCode switch { Keys.Down => 1, Keys.Up => -1, Keys.PageDown => 10, _ => -10 };
            _matches.SelectedIndex = Math.Clamp(_matches.SelectedIndex + step, 0, _matches.Items.Count - 1);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape && _matches.Visible) { _matches.Hide(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindStudent(); }
    }

    private void PickMatch()
    {
        if (_matches.SelectedItem is not StudentItem s) return;
        _matches.Hide();
        _ = LoadStudentAsync(s.Cc);
    }

    private void FindStudent()
    {
        var text = _find.Text.Trim();
        if (text.Length == 0) return;
        // A CC typed or scanned from a challan opens that student, on roll or not.
        if (int.TryParse(text, out var cc) && _students.Any(s => s.Cc == cc)) { _matches.Hide(); _ = LoadStudentAsync(cc); return; }
        if (_matches.Visible) { PickMatch(); return; }
        NotYet(_allStudents.Checked || Matching(text, all: true).Count == 0 ? $"No student matches \"{text}\"."
            : $"No student on roll matches \"{text}\". Tick \"Show left students\" to look for students who have left.");
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
        int? MonthlyFee, (int, int) Today, HashSet<(int, int, DateTime)> Slips);

    /// <summary>The receipts of a student that have a bank slip (empty if sql\fee_receipt_files_install.sql has not been run).</summary>
    private static HashSet<(int, int, DateTime)> SlipsOf(ArqamDbContext db, int cc)
    {
        try
        {
            return (from f in db.FeeReceiptFiles
                    join c in db.FeeChallans on new { T = (int?)f.CollectionTypeId, N = (int?)f.ReceiptNo, D = (DateTime?)f.ReceivedOn }
                        equals new { T = c.CollectionTypeId, N = c.ReceiptNo, D = c.ReceivedOn }
                    where c.StudentId == cc
                    select new { f.CollectionTypeId, f.ReceiptNo, f.ReceivedOn }).Distinct().AsEnumerable()
                .Select(f => (f.CollectionTypeId, f.ReceiptNo, f.ReceivedOn)).ToHashSet();
        }
        catch (Microsoft.Data.SqlClient.SqlException) { return []; }
    }

    private async Task LoadStudentAsync(int cc, bool askToSave = true)
    {
        if (_loading) return;
        if (askToSave && _student?.Id != cc && !ConfirmLeave()) return;
        var keepView = _student?.Id; // reloading the same student keeps what is shown
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
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
                    var challans = db.FeeChallans
                        .Where(c => c.StudentId == cc).OrderBy(c => c.IssueDate).ThenBy(c => c.Id).ToList();
                    var family = s?.FamilyId is int f
                        ? db.Students.Where(x => x.FamilyId == f && (x.OnRoll || x.Id == cc)).OrderBy(x => x.Name).ToList()
                        : s is null ? [] : [s];
                    var ids = family.Select(x => (int?)x.Id).ToList();
                    var unpaid = db.FeeChallans.Where(c => !c.Paid && ids.Contains(c.StudentId)).OrderBy(c => c.IssueDate).ThenBy(c => c.Id).ToList();
                    return new StudentData(s, challans, family, unpaid, db.MonthlyFeeOf(cc), TodayTotals(db), SlipsOf(db, cc));
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
            _slips = data.Slips;
            if (_student is null) NotYet($"There is no student with CC {cc}.");
            _familyMode = false;
            if (_student?.Id != keepView) _view.SelectedIndex = 0; // a new student opens on the unpaid challans
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

    private static DateTime YearAgo => DateTime.Today.AddMonths(-12);

    /// <summary>Challans issued in the last 12 months.</summary>
    private static bool InLastYear(FeeChallan c) => c.IssueDate >= YearAgo;

    /// <summary>
    /// Whether a challan is shown: unpaid ones, the last 12 months, or everything since the student was admitted
    /// (from a month before the admission date, so the admission challan issued just before it is included).
    /// </summary>
    private bool InView(FeeChallan c) => _view.SelectedItem switch
    {
        ViewYear => InLastYear(c),
        ViewSinceAdmission => _student?.AdmissionDate is not { } admitted || c.IssueDate is null || c.IssueDate >= admitted.Date.AddMonths(-1),
        _ => !c.Paid,
    };

    private void ShowStudent()
    {
        ShowChallans();
        ShowAgainst();
        ShowFamily();
    }

    private void ShowChallans()
    {
        var rows = _challans.Where(InView).ToList();
        _totalRow = new FeeChallan
        {
            Period = "Total",
            TuitionFee = Sum(c => c.TuitionFee), ExamsFee = Sum(c => c.ExamsFee), RegistrationFee = Sum(c => c.RegistrationFee),
            AdmissionFee = Sum(c => c.AdmissionFee), ProspectusFee = Sum(c => c.ProspectusFee), Fine = Sum(c => c.Fine), Other = Sum(c => c.Other),
            ReceivedAmount = Sum(c => c.Paid ? c.ReceivedAmount : 0),
        };
        int Sum(Func<FeeChallan, int?> f) => rows.Where(c => !c.IsDeleted).Sum(c => f(c) ?? 0);

        _loading = true;
        _grid.ResetColumns();
        // Columns as narrow as their headings and values allow, so the whole row (with the Bank slip link at its end) fits on screen.
        int Fit(string header, string sample) =>
            Math.Max(TextRenderer.MeasureText(header, Theme.BaseFont).Width, TextRenderer.MeasureText(sample, Theme.BaseFont).Width) + 10;
        _grid.AddText(nameof(FeeChallan.Period), "Period", Fit("Period", "NOV-26"), readOnly: true);
        _grid.AddText(nameof(FeeChallan.IssueDate), "Issued", Fit("Issued", "00/00/0000"), readOnly: true, format: "dd/MM/yyyy");
        _grid.AddText(nameof(FeeChallan.DueDate), "Due", Fit("Due", "00/00/0000"), readOnly: true, format: "dd/MM/yyyy");
        foreach (var (property, header) in new[]
                 {
                     (nameof(FeeChallan.TuitionFee), "Tuition"), (nameof(FeeChallan.ExamsFee), "Exams/AF"), (nameof(FeeChallan.RegistrationFee), "Registration"),
                     (nameof(FeeChallan.AdmissionFee), "Admission"), (nameof(FeeChallan.ProspectusFee), "Prospectus"), (nameof(FeeChallan.Fine), "Fine"),
                     (nameof(FeeChallan.Other), "Other"),
                 })
            Amount(_grid.AddText(property, header, Fit(header, "00,000"), format: "N0"));
        Amount(_grid.AddText(nameof(FeeChallan.Total), "Total", Fit("Total", "000,000"), readOnly: true, format: "N0"));
        Amount(_grid.AddText(nameof(FeeChallan.ReceivedAmount), "Received", Fit("Received", "000,000"), readOnly: true, format: "N0"));
        _grid.AddText(nameof(FeeChallan.ReceivedOn), "Receiving Date", Fit("Receiving Date", "00/00/0000"), readOnly: true, format: "dd/MM/yyyy");
        _grid.AddText(nameof(FeeChallan.ReceiptNo), "Receipt", Fit("Receipt", "0000"), readOnly: true);
        var mode = _grid.AddCombo(nameof(FeeChallan.CollectionTypeId), "Mode", _types.Select(t => ((int?)t.Id, t.Code ?? t.Name ?? "")), Fit("Mode", "ODG"));
        mode.ReadOnly = true;
        mode.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        _grid.AddText(nameof(FeeChallan.EditedBy), "By", Fit("By", "admin"), readOnly: true, fill: true);
        // At the end of each row: "View" when the receipt has a bank / online deposit slip, "Attach" for an online deposit without one.
        // The text is set in FormatCell; clicking it does it.
        _grid.Columns.Add(new DataGridViewLinkColumn
        {
            Name = SlipColumn, HeaderText = "Bank slip", Width = Fit("Bank slip", "Attach"), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
            TrackVisitedState = false, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent,
        });
        // Only an Admin changes a challan, and only an unpaid one; a paid one has its payment cancelled first.
        _grid.CanEditCell = (row, _) => _isAdmin && row >= 0 && _grid.Rows[row].DataBoundItem is FeeChallan c && !c.Paid && !c.IsDeleted;
        _grid.Bind(new FilterBindingList<FeeChallan>(rows));
        ShowTotals();
        _loading = false;
        if (_grid.Rows.Count > 0)
        {
            var last = _grid.Rows.Count - 1; // the newest challan
            _grid.CurrentCell = _grid.Rows[last].Cells[0];
            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, last - 8);
        }
        ShowHeader();
        RaiseStatusChanged();

        static void Amount(DataGridViewColumn c) => c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    /// <summary>
    /// The totals, in a one-row strip under the list with the same columns, so sorting and filtering never move them.
    /// Its columns follow the list's widths and horizontal scrolling.
    /// </summary>
    private void ShowTotals()
    {
        _totals.Columns.Clear();
        foreach (DataGridViewColumn col in _grid.Columns)
        {
            var copy = (DataGridViewColumn)col.Clone();
            copy.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            copy.Width = col.Width;
            copy.Visible = col.Visible;
            copy.DisplayIndex = col.DisplayIndex;
            copy.DefaultCellStyle.Font = Theme.Scaled(1f, FontStyle.Bold);
            copy.DefaultCellStyle.ForeColor = Theme.Text;
            _totals.Columns.Add(copy);
        }
        _totals.DataSource = _grid.Rows.Count == 0 ? null : new List<FeeChallan> { _totalRow! };
        _totals.ClearSelection();
        _totalsHost.Visible = _grid.Rows.Count > 0;
        SyncTotals();
    }

    /// <summary>The totals strip is as wide as all the columns and slides left as the list scrolls (its host panel clips it).</summary>
    private void SyncTotals()
    {
        if (_totals.Columns.Count != _grid.Columns.Count) return;
        for (var i = 0; i < _grid.Columns.Count; i++) _totals.Columns[i].Width = _grid.Columns[i].Width;
        var width = _grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Sum(c => c.Width) + 2;
        _totals.SetBounds(-_grid.HorizontalScrollingOffset, 0, width, _totalsHost.Height);
    }

    public override void SortAscending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(System.ComponentModel.ListSortDirection.Descending);

    private const string SlipColumn = "Slip";

    private bool HasSlip(FeeChallan c) =>
        c is { Paid: true, CollectionTypeId: int t, ReceiptNo: int n, ReceivedOn: DateTime d } && _slips.Contains((t, n, d.Date));

    /// <summary>A paid online-deposit challan whose receipt has no slip yet: anyone who receives fees can attach one.</summary>
    private bool CanAttachSlip(FeeChallan c) =>
        _canReceive && c is { Paid: true, IsDeleted: false, ReceiptNo: int, ReceivedOn: DateTime } && !HasSlip(c) && FeeReceiptFileStore.IsOnline(TypeOf(c));

    /// <summary>The Bank slip link: view the slip, or attach one.</summary>
    private void SlipLinkClicked(int row, int column)
    {
        if (row < 0 || column < 0 || _grid.Columns[column].Name != SlipColumn || _grid.Rows[row].DataBoundItem is not FeeChallan c) return;
        if (HasSlip(c)) OpenSlip(c);
        else if (CanAttachSlip(c)) AttachSlipLater(c);
    }

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not FeeChallan c || e.CellStyle is null) return;
        if (_grid.Columns[e.ColumnIndex].Name == SlipColumn)
        {
            e.Value = HasSlip(c) ? "View" : CanAttachSlip(c) ? "Attach" : "";
            e.FormattingApplied = true;
        }
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
            var year = _challans.Where(c => !c.IsDeleted && InLastYear(c)).ToList();
            _header.ClassText = ClassText(s.ClassId, s.SectionId);
            _header.Charged = year.Sum(c => c.Total);
            _header.Received = year.Where(c => c.Paid).Sum(c => c.ReceivedAmount ?? 0);
            _header.Balance = _challans.Where(c => !c.Paid && !c.IsDeleted).Sum(c => c.Total);
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
        _slipPath = null;
        SuggestReceiptNo();
        ShowAmount();
        ShowSlip();
        _receiveCard.Enabled = _student is not null;
    }

    /// <summary>The slip row is used only for online deposits.</summary>
    private void ShowSlip()
    {
        var online = FeeReceiptFileStore.IsOnline((_mode.SelectedItem as ModeItem)?.Type);
        _attachSlip.Enabled = online;
        _slipName.Text = !online ? "Only for online deposits" : _slipPath is null ? "No slip attached" : Path.GetFileName(_slipPath);
        _slipName.StateCommon.ShortText.Color1 = online && _slipPath is not null ? Theme.Text : Theme.Muted;
    }

    private string NameOf(int? cc) => _family.FirstOrDefault(s => s.Id == cc)?.Name ?? $"CC {cc}";

    private sealed record AgainstItem(FeeChallan Challan, string? StudentName)
    {
        public override string ToString() =>
            $"{(StudentName is null ? "" : StudentName + " · ")}{Challan.Period} · Rs {Challan.Total:N0}";
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

    private static string CodeOf(CollectionType t) => (t.Code ?? "").Trim().ToUpperInvariant();

    /// <summary>The receipt number series of a collection type: boys (CRB, ODB), back receipts (BR) or girls (CR, ODG and the rest).</summary>
    private static string SeriesKey(CollectionType t) => CodeOf(t) switch { "CRB" or "ODB" => "boys", "BR" => "back", _ => "girls" };

    /// <summary>
    /// The paid challans whose receipt numbers are in the same series as this collection type. Online Deposit Boys receipts count with
    /// the boys from <see cref="AppConfig.OnlineBoysOwnSeriesFrom"/>; before that they used the girls numbers.
    /// </summary>
    private IQueryable<FeeChallan> InSeries(IQueryable<FeeChallan> challans, CollectionType type)
    {
        var key = SeriesKey(type);
        var odb = _types.FirstOrDefault(t => CodeOf(t) == "ODB")?.Id;
        var from = Ctx.Config.OnlineBoysOwnSeriesFrom;
        var ids = _types.Where(t => SeriesKey(t) == key && t.Id != odb).Select(t => (int?)t.Id).ToList();
        return key switch
        {
            "boys" => challans.Where(c => ids.Contains(c.CollectionTypeId) || (odb != null && c.CollectionTypeId == odb && c.ReceivedOn >= from)),
            "girls" => challans.Where(c => ids.Contains(c.CollectionTypeId) || (odb != null && c.CollectionTypeId == odb && c.ReceivedOn < from)),
            _ => challans.Where(c => ids.Contains(c.CollectionTypeId)),
        };
    }

    /// <summary>The start of the academic year (1 July): receipt books start again about then.</summary>
    private static DateTime YearStart => new(DateTime.Today.Month >= 7 ? DateTime.Today.Year : DateTime.Today.Year - 1, 7, 1);

    /// <summary>
    /// Suggests the number after the highest one used on the latest day with receipts in this series
    /// (not the highest ever: receipt books start again, e.g. 2899 in June and 952 in October).
    /// </summary>
    private void SuggestReceiptNo()
    {
        if (_db is null || _loading || _mode.SelectedItem is not ModeItem m) { _receiptNo.Text = ""; return; }
        try
        {
            var paid = InSeries(_db.FeeChallans.Where(c => c.Paid && c.ReceivedOn <= DateTime.Today), m.Type);
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
        var since = YearStart;
        var online = FeeReceiptFileStore.IsOnline(mode);
        if (online && _slipPath is null && !Ask("No bank / online deposit slip is attached. Save the receipt without it?\n\nIt can be attached later: right-click the challan."))
            return;
        try
        {
            var used = InSeries(_db.FeeChallans.Where(c => c.Paid && c.ReceiptNo == receiptNo && c.ReceivedOn >= since), mode)
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
            // What the receipt's students owed before paying (with the fine), for the receipt.
            var students = ticked.Select(c => c.StudentId).Distinct().ToList();
            var balanceBefore = _familyUnpaid.Where(c => !c.IsDeleted && students.Contains(c.StudentId)).Sum(c => c.Total);
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

            if (online && _slipPath is { } slip) AttachSlip(mode, receiptNo, date, slip);
            if (_printReceipt.Checked)
            {
                var stillDue = _familyUnpaid.Where(c => !c.IsDeleted && !c.Paid && students.Contains(c.StudentId)).ToList();
                var slipForPrint = online && _slipPath is { } path ? FeeReceiptFileStore.ForPrinting(path, ReadLocal(path)) : (null, null);
                ShowReceipt(receiptNo, date, mode, ticked, stillDue, balanceBefore, slipForPrint);
            }
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

    public override void NewRecord() => AddFee();

    /// <summary>Adds a missing or new challan for the student shown (Admin only).</summary>
    private void AddFee()
    {
        if (_student is not { } s) { NotYet("Find a student first."); return; }
        if (!_isAdmin) { NotYet("Only an Admin can add a fee."); return; }
        if (!ConfirmLeave()) return;
        FeeDefinition? definition;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            definition = db.FeeDefinitions.AsNoTracking().Where(d => d.StudentId == s.Id).OrderByDescending(d => d.Id).FirstOrDefault();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not read the fee definition.", ex); return; }
        using var form = new NewFeeForm(Ctx.Config, Ctx.Session, s, definition,
            _challans.Where(c => !c.IsDeleted && c.Period is not null).Select(c => c.Period!), ClassText(s.ClassId, s.SectionId));
        if (form.ShowDialog(this) == DialogResult.OK) _ = LoadStudentAsync(s.Id, askToSave: false);
    }

    /// <summary>The fee challan form of the student shown: all unpaid challans added together.</summary>
    private void PrintChallan()
    {
        if (_student is not { } s) { NotYet("Find a student first."); return; }
        List<Printing.ChallanPrint> challans;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            challans = FeeChallanData.Load(db, [s.Id]);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the challans.", ex); return; }
        if (challans.Count == 0) { NotYet($"{s.Name} has no unpaid challans."); return; }
        var school = SchoolDetails();
        PrintPreviewForm.Show(this, () => Printing.Challans.Create(challans, school, Ctx.Config.ChallanBank, Ctx.Config.ChallanLateFee),
            $"Fee challan · {s.Id} {s.Name}", $"Fee challan {s.Id} {s.Name}");
    }

    public override void DeleteRecord() => DeleteChallan();

    // ----- Row menu: cancel payment, delete, restore, receipt -----

    private FeeChallan? CurrentChallan() =>
        _grid.CurrentRow?.DataBoundItem is FeeChallan c && !ReferenceEquals(c, _totalRow) ? c : null;

    private ContextMenuStrip? RowMenu()
    {
        if (CurrentChallan() is not { } c) return null;
        var menu = new ContextMenuStrip();
        if (c.Paid) menu.Items.Add($"Print receipt {c.ReceiptNo}", null, (_, _) => ReprintReceipt());
        if (c.Paid && HasSlip(c)) menu.Items.Add("Open bank slip", null, (_, _) => OpenSlip(c));
        if (c.Paid && !c.IsDeleted && FeeReceiptFileStore.IsOnline(TypeOf(c)))
            menu.Items.Add(HasSlip(c) ? "Replace bank slip…" : "Attach bank slip…", null, (_, _) => AttachSlipLater(c)).Enabled =
                _isAdmin || (_canReceive && !HasSlip(c));
        if (c.Paid && !c.IsDeleted) menu.Items.Add("Cancel payment…", null, (_, _) => CancelPayment()).Enabled = _isAdmin;
        if (!c.Paid && !c.IsDeleted) menu.Items.Add("Delete challan…", null, (_, _) => DeleteChallan()).Enabled = _isAdmin;
        if (!_isAdmin) menu.Items.Add(new ToolStripLabel("Only an Admin can cancel or delete") { ForeColor = SystemColors.GrayText });
        return menu;
    }

    private CollectionType? TypeOf(FeeChallan c) => _types.FirstOrDefault(t => t.Id == c.CollectionTypeId);

    // ----- Bank / online deposit slips -----

    /// <summary>Stores the slip of a receipt just saved; if that fails the receipt stays saved and the slip can be attached later.</summary>
    private bool AttachSlip(CollectionType mode, int receiptNo, DateTime date, string path)
    {
        try
        {
            UseWaitCursor = true;
            new FeeReceiptFileStore(Ctx.Config, Ctx.Session).Add(mode, receiptNo, date, path);
            return true;
        }
        catch (DocumentProblem ex) { NotYet(ex.Message + "\n\nThe receipt is saved; attach the slip later by right-clicking the challan."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("The receipt is saved, but the bank slip could not be stored. Attach it later by right-clicking the challan.", ex);
        }
        finally { UseWaitCursor = false; }
        return false;
    }

    /// <summary>Attaches (or, for an Admin, replaces) the slip of a paid online-deposit challan's receipt.</summary>
    private void AttachSlipLater(FeeChallan c)
    {
        if (TypeOf(c) is not { } mode || c is not { ReceiptNo: int no, ReceivedOn: DateTime date }) return;
        if (HasSlip(c) && !_isAdmin) { NotYet("Only an Admin can replace a bank slip."); return; }
        using var dialog = new OpenFileDialog { Filter = "Pictures or PDF|*.jpg;*.jpeg;*.png;*.bmp;*.pdf|All files|*.*", Title = $"Bank / online deposit slip for receipt {no}" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!AttachSlip(mode, no, date, dialog.FileName)) return;
        _slips.Add((mode.Id, no, date.Date));
        _grid.Invalidate();
    }

    private void OpenSlip(FeeChallan c)
    {
        if (c is not { CollectionTypeId: int t, ReceiptNo: int no, ReceivedOn: DateTime date }) return;
        try
        {
            var store = new FeeReceiptFileStore(Ctx.Config, Ctx.Session);
            if (store.Find(t, no, date) is { } file) SlipViewerForm.Show(this, store, file, $"Bank slip · receipt {no} · {date:dd/MM/yyyy}");
            else NotYet("This receipt has no bank slip.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not open the bank slip.", ex); }
    }

    private bool Ask(string question) =>
        KryptonMessageBox.Show(this, question, Title, KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) == DialogResult.Yes;

    private void CancelPayment()
    {
        if (!_isAdmin || CurrentChallan() is not { Paid: true } c) return;
        if (!Ask($"Cancel the payment of {c.Period} (receipt {c.ReceiptNo}, Rs {c.ReceivedAmount:N0})? The challan becomes unpaid again.")) return;
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
        if (c.IsDeleted || !Ask($"Delete the {c.Period} challan (Rs {c.Total:N0})? It stays in the database, marked as deleted.")) return;
        c.IsDeleted = true;
        c.DeletedOn = DateTime.Now;
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
        var mode = TypeOf(c);
        try
        {
            var paid = _db.FeeChallans.AsNoTracking().Where(x => x.Paid && x.ReceiptNo == no && x.ReceivedOn == date);
            var challans = (mode is null ? paid.Where(x => x.CollectionTypeId == c.CollectionTypeId) : InSeries(paid, mode))
                .OrderBy(x => x.StudentId).ThenBy(x => x.IssueDate).ToList();
            // What the same students owe now (a reprint cannot know what they owed when it was first printed).
            var students = challans.Select(x => x.StudentId).Distinct().ToList();
            var stillDue = _db.FeeChallans.AsNoTracking().Where(x => !x.Paid && students.Contains(x.StudentId))
                .OrderBy(x => x.StudentId).ThenBy(x => x.IssueDate).ToList();
            ShowReceipt(no, date, mode, challans, stillDue, balanceBefore: null, StoredSlipForPrint(mode, no, date));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not read the receipt.", ex);
        }
    }

    private static byte[]? ReadLocal(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The saved slip of an online-deposit receipt, ready to print on it (nothing for other modes).</summary>
    private (byte[]?, string?) StoredSlipForPrint(CollectionType? mode, int receiptNo, DateTime date)
    {
        if (!FeeReceiptFileStore.IsOnline(mode)) return (null, null);
        try
        {
            var store = new FeeReceiptFileStore(Ctx.Config, Ctx.Session);
            return store.Find(mode!.Id, receiptNo, date) is { } file ? FeeReceiptFileStore.ForPrinting(file.FilePath, store.ReadBytes(file)) : (null, null);
        }
        catch (Microsoft.Data.SqlClient.SqlException) { return (null, null); }
    }

    /// <summary>
    /// Shows the receipt on this computer's receipt paper (A4 or 80 mm roll), with what the same students still owe
    /// and the bank / online deposit slip (a picture, or a note when it is a PDF).
    /// </summary>
    private void ShowReceipt(int receiptNo, DateTime date, CollectionType? mode, List<FeeChallan> challans, List<FeeChallan> stillDue, int? balanceBefore,
        (byte[]? Picture, string? Note) slip)
    {
        var ids = challans.Concat(stillDue).Select(c => c.StudentId ?? 0).Distinct().ToList();
        var students = _students.Where(s => ids.Contains(s.Cc)).ToDictionary(s => s.Cc);
        var lines = challans.Select(c =>
        {
            var s = students.GetValueOrDefault(c.StudentId ?? 0);
            return new Printing.ReceiptLine(c.StudentId ?? 0, s?.Name ?? "", s is null ? "" : ClassText(s.ClassId, s.SectionId),
                c.Period ?? "", c.Total - (c.Fine ?? 0), c.Fine ?? 0, c.ReceivedAmount ?? c.Total);
        }).ToList();
        var first = students.GetValueOrDefault(challans.FirstOrDefault()?.StudentId ?? 0);
        var school = first?.ClassId is int cls && _classes.TryGetValue(cls, out var ci) && ci.School.Length > 0 ? ci.School : Printing.PrintKit.DefaultSchoolName;
        var by = challans.Select(c => c.EditedBy).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b)) ?? Ctx.Session.UserName;
        var due = stillDue.Select(c => new Printing.DueLine(c.StudentId ?? 0, students.GetValueOrDefault(c.StudentId ?? 0)?.Name ?? "", c.Period ?? "", c.Total)).ToList();
        var receipt = new Printing.ReceiptPrint(receiptNo, date, mode?.Name ?? "", by, school,
            challans.Select(c => c.Remarks).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)), lines, due, DateTime.Today, balanceBefore,
            slip.Picture, slip.Note);
        var details = SchoolDetails();
        var paper = Ctx.Settings.ReceiptPaper;
        PrintPreviewForm.Show(this, () => Printing.Receipts.Create(receipt, details, paper), $"Receipt {receiptNo}", $"Receipt {receiptNo}");
    }

    private void PrintLedger()
    {
        if (_student is null) { NotYet("Find a student first."); return; }
        var title = $"Fee ledger · {_student.Id} {_student.Name} · {_view.SelectedItem}";
        PrintPreviewForm.Show(this, () => Printing.ListExport.From(_grid, title).ToPdf(), "Fee ledger", $"Fee ledger {_student.Id} {_student.Name}");
    }

    // ----- Header -----

    /// <summary>The find box, then the student (initials, name, father, family, class, monthly fee, status) and the year's totals.</summary>
    private sealed class LedgerHeader : Panel
    {
        public Student? Student;
        public string ClassText = "";
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
                ("Charged (12 months)", $"Rs {Charged:N0}", value, Theme.Text),
                ("Received (12 months)", $"Rs {Received:N0}", value, Theme.Text),
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
