using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Navigator;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>Opens the Students screen with a new student already added to the given family.</summary>
public sealed record NewStudentInFamily(int FamilyId);

/// <summary>
/// Student list (search, class, section, status filters) with the selected student's details below:
/// General, Family, Conversations, and Fees / Attendance (later phases).
/// </summary>
public sealed class StudentsScreen : ScreenBase
{
    private readonly bool _canEdit;
    private readonly KryptonTextBox _search = new() { Width = 280, CueHint = { CueHintText = "Search name, CC, father, phone or Form-B" } };
    private readonly ComboBox _schoolFilter = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _classFilter = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _sectionFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _statusFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _filterChip = new() { AutoSize = true, Visible = false, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(8, 5, 3, 3) };
    private readonly AccessGrid _grid = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly KryptonNavigator _tabs = new() { Dock = DockStyle.Fill, NavigatorMode = NavigatorMode.BarTabGroup };
    private readonly BindingSource _bs = new() { DataSource = typeof(Student) };
    private readonly PictureBox _photo = new() { Size = new Size(150, 180), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly KryptonPanel _detail = new() { Dock = DockStyle.Fill };
    private readonly KryptonPanel _generalHost = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
    private readonly FamilyFields _family;
    private readonly AccessGrid _convoGrid = new();
    private readonly KryptonButton _changeFamily = new() { Text = "Change family…", AutoSize = true };
    private readonly AccessGrid _docGrid = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly KryptonLabel _docInfo = new() { Text = "", AutoSize = true };
    private readonly FlowLayoutPanel _docBar = new() { Dock = DockStyle.Top, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
    private readonly List<Control> _docEditButtons = [];
    private readonly AccessGrid _feeGrid = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly Label _feeInfo = new() { Text = "", AutoSize = true, Margin = new Padding(12, 8, 3, 3), BackColor = Color.Transparent, ForeColor = Theme.Muted };
    private readonly KryptonCheckBox _feeAll = new() { Text = "Show paid challans too", AutoSize = true, Checked = true, Padding = new Padding(8, 4, 0, 0) };
    private KryptonPage? _feePage;
    private List<FeeChallan> _fees = [];
    private int? _feesFor;
    private readonly StudentDocumentStore _store;
    private List<StudentDocument> _documents = [];

    private ArqamDbContext? _db;
    private FilterBindingList<Student>? _list;
    private FilterBindingList<Conversation>? _convos;
    private List<(int? Value, string Text)> _classes = [], _sections = [], _bloodGroups = [], _convoTypes = [];
    private List<Family> _allFamilies = [];
    /// <summary>Class → school (from the class's department).</summary>
    private Dictionary<int, (int Id, string Name)> _classSchool = [];
    private List<(int? Value, string Text)> _schools = [];
    private Student? _current;
    private object? _pendingNavigation;
    private string _lastChange = "";
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;

    public StudentsScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Students);
        _family = new FamilyFields(_canEdit);
        _store = new StudentDocumentStore(ctx.Config, ctx.Session);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _schoolFilter, _classFilter, _sectionFilter, _statusFilter, _filterChip,
            new Label { Text = "Click a column heading to sort; use its arrow to filter", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 3, 3), BackColor = Color.Transparent }]);
        _statusFilter.Items.AddRange(["Status: Active", "Status: Not active", "All students"]);
        _statusFilter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilters();
        _schoolFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _classFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _sectionFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilters();

        _grid.SelectionChanged += (_, _) => ShowCurrent();
        _grid.FilterChanged += (_, _) => { ShowFilterChip(); RaiseStatusChanged(); };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.CellStyle is not null && _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(Student.StatusText))
                e.CellStyle.ForeColor = Equals(e.Value, "Active") ? Theme.Good : Theme.Muted;
        };

        _changeFamily.Click += (_, _) => ChangeFamily();
        BuildGeneralTab();
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.Hide;
        _tabs.Button.ContextButtonDisplay = ButtonDisplay.Hide;
        _tabs.Pages.Add(Page("General", _generalHost));
        _tabs.Pages.Add(Page("Family", FamilyTab()));
        _tabs.Pages.Add(_feePage = Page("Fees", FeesTab()));
        _tabs.SelectedPageChanged += (_, _) => { if (_tabs.SelectedPage == _feePage) { _feesFor = null; _ = ShowFeesAsync(_current); } }; // fresh each time, after fee is received
        _tabs.Pages.Add(Page("Attendance", Later("Attendance history comes with the attendance module (phase 6).")));
        _tabs.Pages.Add(Page("Conversations", ConversationTab()));
        _tabs.Pages.Add(Page("Documents", DocumentsTab()));
        _detail.Controls.Add(_tabs);

        var card = new FillCard();
        card.Controls.Add(_detail);
        var split = new KryptonSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Padding = new Padding(0, 8, 0, 0);
        split.Panel2.Controls.Add(card);
        WhenSized(split, () => split.SplitterDistance = Math.Max(160, split.Height * 9 / 20));

        Controls.Add(split);
        Controls.Add(bar);

        _commands =
        [
            new("Student",
                new("Fee ledger", Icons.Ledger, () => { if (_current is { Id: not 0 } s) Ctx.OpenScreenAt("ledger", s.Id); }),
                new("Family", Icons.People, () => { if (_current?.FamilyId is int id) Ctx.OpenScreenAt("family", id); }),
                new("Send WhatsApp", Icons.Chat, () => NotYet("Sending WhatsApp messages comes with the Messages screens (phase 5)."))),
            new("Print",
                new("ID card", Icons.IdCard, PrintIdCards),
                new("Leaving cert.", Icons.Document, PrintLeavingCertificate),
                new("Bonafide cert.", Icons.Document, PrintBonafideCertificate)),
            new("Export",
                new("Excel", Icons.Excel, ExportExcel),
                new("PDF", Icons.Document, ExportPdf)),
        ];
        HandleCreated += (_, _) => Reload();
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;

    private void NotYet(string message) =>
        KryptonMessageBox.Show(this, message, "Students", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    // ----- Layout -----

    private static KryptonPage Page(string text, Control content)
    {
        var page = new KryptonPage { Text = text, TextTitle = text, Padding = new Padding(6) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static Control Later(string text) => new KryptonLabel { Text = text, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

    /// <summary>Built once; <see cref="FillLists"/> refills the drop-downs after each load.</summary>
    private void BuildGeneralTab()
    {
        var f = new FieldPanel(4);
        var cc = f.Add("CC", Inputs.Text(_bs, nameof(Student.Id), readOnly: true));
        cc.DataBindings[0].Format += (_, e) => { if (e.Value is 0) e.Value = "(new)"; };
        f.Add("DS#", Inputs.Text(_bs, nameof(Student.DsNo)));
        var famId = Inputs.Text(_bs, nameof(Student.FamilyId), readOnly: true);
        famId.DataBindings[0].Format += (_, e) =>
        {
            if (_bs.Current is Student s) e.Value = s.FamilyId is int id ? $"{id} · {s.FatherName}" : s.Family is not null ? $"(new) · {s.FatherName}" : "";
        };
        var famRow = new TableLayoutPanel { ColumnCount = 2, Height = 28, BackColor = Color.Transparent };
        famRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        famRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        famId.Dock = DockStyle.Fill;
        famRow.Controls.Add(famId);
        famRow.Controls.Add(_changeFamily);
        f.Add("Family", famRow, 2);
        f.Add("Name", Inputs.Text(_bs, nameof(Student.Name)), 2);
        f.Add("Name (Urdu)", Inputs.Text(_bs, nameof(Student.NameUrdu), rightToLeft: true), 2);
        f.Add("Sex", Inputs.Combo<string>(_bs, nameof(Student.Sex), [(null, ""), ("MALE", "Male"), ("FEMALE", "Female")]));
        f.Add("Birth date", Inputs.Date(_bs, nameof(Student.BirthDate)));
        f.Add("Birth place", Inputs.Text(_bs, nameof(Student.BirthPlace)));
        f.Add("Form-B no.", Inputs.FormB(_bs, nameof(Student.FormB)));
        f.Add("Admission date", Inputs.Date(_bs, nameof(Student.AdmissionDate)));
        f.Add("Admission class", Inputs.Text(_bs, nameof(Student.AdmissionClass)));
        _classCombo = f.Add("Current class", Inputs.Combo(_bs, nameof(Student.ClassId), _classes));
        _sectionCombo = f.Add("Section", Inputs.Combo(_bs, nameof(Student.SectionId), _sections));
        _bloodCombo = f.Add("Blood group", Inputs.Combo(_bs, nameof(Student.BloodGroupId), _bloodGroups));
        var status = f.Add("Status", Inputs.Combo<string>(_bs, nameof(Student.StatusChoice), [("Active", "Active"), ("Left", "Left"), ("Transfer", "Transfer")]));
        status.SelectionChangeCommitted += (_, _) => { _grid.Invalidate(); RaiseStatusChanged(); };
        f.Add("WhatsApp", Inputs.Phone(_bs, nameof(Student.WhatsApp)));
        f.Add("Email", Inputs.Text(_bs, nameof(Student.Email)));
        f.Add("Leaving date", Inputs.Date(_bs, nameof(Student.LeavingDate)));
        f.Add("Reason for leaving", Inputs.Text(_bs, nameof(Student.LeavingReason)), 3);
        f.Add("Remarks", Inputs.Text(_bs, nameof(Student.Remarks)), 4);
        f.Enabled = _canEdit;

        var photoPanel = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 170, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, Padding = new Padding(8, 0, 0, 0) };
        photoPanel.Controls.Add(_photo);
        var change = new KryptonButton { Text = "Change photo", Width = 150, Enabled = _canEdit };
        change.Click += (_, _) => ChoosePhoto();
        photoPanel.Controls.Add(change);

        _generalHost.SuspendLayout();
        _generalHost.Controls.Add(f);
        _generalHost.Controls.Add(photoPanel);
        _generalHost.ResumeLayout(true);
        _changeFamily.Enabled = _canEdit;
    }

    private ComboBox? _classCombo, _sectionCombo, _bloodCombo;

    private void FillLists()
    {
        if (_classCombo is null || _sectionCombo is null || _bloodCombo is null) return;
        Inputs.SetItems(_classCombo, _classes);
        Inputs.SetItems(_sectionCombo, _sections);
        Inputs.SetItems(_bloodCombo, _bloodGroups);
    }

    private Control FamilyTab()
    {
        var host = new KryptonPanel { AutoScroll = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        var open = new KryptonButton { Text = "Open in Family screen", AutoSize = true };
        open.Click += (_, _) => { if (_current?.FamilyId is int id) Ctx.OpenScreenAt("family", id); };
        bar.Controls.Add(open);
        bar.Controls.Add(new KryptonLabel { Text = "Changes here apply to the whole family (all brothers and sisters).", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        host.Controls.Add(_family);
        host.Controls.Add(bar);
        return host;
    }

    /// <summary>The student's challans, newest first, read only. Receiving fee and changing challans stay in the Fee ledger.</summary>
    private Control FeesTab()
    {
        var host = new KryptonPanel();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent, WrapContents = false };
        var open = new KryptonButton { Text = "Open in Fee ledger", AutoSize = true };
        open.Click += (_, _) => OpenLedger();
        bar.Controls.AddRange([open, _feeAll, _feeInfo]);
        _feeAll.CheckedChanged += (_, _) => BindFees();
        _feeGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenLedger(); };
        _feeGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.CellStyle is not null && _feeGrid.Rows[e.RowIndex].DataBoundItem is FeeChallan { Paid: false })
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.Error;
        };
        host.Controls.Add(_feeGrid);
        host.Controls.Add(bar);
        return host;
    }

    private void OpenLedger()
    {
        if (_current is { Id: not 0 } s) Ctx.OpenScreenAt("ledger", s.Id);
    }

    /// <summary>Loads the challans when the Fees tab is open, in the background, once per student.</summary>
    private async Task ShowFeesAsync(Student? student)
    {
        if (_tabs.SelectedPage != _feePage) return;
        if (student is not { Id: not 0 }) { _feesFor = null; _fees = []; BindFees(); return; }
        if (_feesFor == student.Id) return;
        _feesFor = student.Id;
        _feeInfo.Text = "Loading…";
        List<FeeChallan> fees;
        try
        {
            fees = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                return db.FeeChallans.AsNoTracking().Where(c => c.StudentId == student.Id && !c.IsDeleted)
                    .OrderByDescending(c => c.IssueDate).ThenByDescending(c => c.Id).ToList();
            });
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            _feesFor = null;
            _feeInfo.Text = "Could not load the fee record: " + ex.Message;
            return;
        }
        if (IsDisposed || !ReferenceEquals(student, _current)) return;
        _fees = fees;
        BindFees();
    }

    private void BindFees()
    {
        _feeGrid.ResetColumns();
        var rows = _feeAll.Checked ? _fees : _fees.Where(c => !c.Paid).ToList();
        _feeGrid.AddText(nameof(FeeChallan.Period), "Period", 80);
        _feeGrid.AddText(nameof(FeeChallan.IssueDate), "Issued", 95, format: "dd/MM/yyyy");
        _feeGrid.AddText(nameof(FeeChallan.DueDate), "Due", 95, format: "dd/MM/yyyy");
        foreach (var (property, header) in new[]
                 {
                     (nameof(FeeChallan.TuitionFee), "Tuition"), (nameof(FeeChallan.ExamsFee), "Exams/AF"), (nameof(FeeChallan.AdmissionFee), "Admission"),
                     (nameof(FeeChallan.Fine), "Fine"), (nameof(FeeChallan.Other), "Other"), (nameof(FeeChallan.Total), "Total"), (nameof(FeeChallan.ReceivedAmount), "Received"),
                 })
            _feeGrid.AddText(property, header, 85, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _feeGrid.AddText(nameof(FeeChallan.ReceivedOn), "Received on", 100, format: "dd/MM/yyyy");
        _feeGrid.AddText(nameof(FeeChallan.ReceiptNo), "Receipt", 70);
        _feeGrid.AddText(nameof(FeeChallan.Remarks), "Remarks", 150, fill: true);
        _feeGrid.Bind(new FilterBindingList<FeeChallan>(rows));
        var unpaid = _fees.Where(c => !c.Paid).ToList();
        var paid = _fees.Where(c => c.Paid).ToList();
        _feeInfo.Text = _current is not { Id: not 0 } ? ""
            : _fees.Count == 0 ? "No challans."
            : $"{unpaid.Count} unpaid, Rs {unpaid.Sum(c => c.Total):N0}  ·  received Rs {paid.Sum(c => c.ReceivedAmount ?? 0):N0} on {paid.Count} challans"
              + (paid.Where(c => c.ReceivedOn != null).MaxBy(c => c.ReceivedOn) is { } last ? $"  ·  last paid {last.ReceivedOn:dd/MM/yyyy}" : "")
              + "  ·  unpaid in red; double-click to open the Fee ledger";
    }

    private Control ConversationTab()
    {
        _convoGrid.ReadOnly = !_canEdit;
        _convoGrid.AllowUserToAddRows = _canEdit;
        _convoGrid.AllowUserToDeleteRows = _canEdit;
        _convoGrid.UserDeletingRow += (_, e) =>
        {
            if (KryptonMessageBox.Show("Delete this conversation? It is removed when you press Save.", "Conversations",
                    KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
        };
        return _convoGrid;
    }

    private Control DocumentsTab()
    {
        var host = new KryptonPanel();
        var bar = _docBar;
        KryptonButton Button(string text, Action click, bool edit)
        {
            var b = new KryptonButton { Text = text };
            b.Click += (_, _) => click();
            bar.Controls.Add(b);
            if (edit) _docEditButtons.Add(b);
            return b;
        }
        Button("Add…", () => AddDocument(null), edit: true);
        Button("Open", OpenDocument, edit: false);
        Button("Save a copy…", SaveDocumentCopy, edit: false);
        Button("Delete", DeleteDocument, edit: true);
        bar.Controls.Add(_docInfo);
        SizeDocBar();
        Theme.Changed += OnThemeChanged;
        _docGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenDocument(); };
        host.Controls.Add(_docGrid);
        host.Controls.Add(bar);
        return host;
    }

    /// <summary>Sizes the document buttons from their text, so they fit at every font size (Krypton's AutoSize buttons overlap).</summary>
    private void SizeDocBar()
    {
        var height = Theme.RowHeight + 2;
        foreach (Control c in _docBar.Controls)
            if (c is KryptonButton b)
                b.Size = new Size(TextRenderer.MeasureText(b.Text, Theme.BaseFont).Width + 24, height);
        _docInfo.Margin = new Padding(8, (height - _docInfo.PreferredSize.Height) / 2 + 3, 3, 3);
        _docBar.Height = height + 10;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SizeDocBar();

    // ----- Loading -----

    public override bool SupportsRecords => true;
    // The context is busy on a background thread while loading, so it must not be touched then.
    private bool _loading;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…"
        : $"Students: {_grid.RecordText()}{(HasChanges ? "  ·  unsaved changes" : "")}{(_lastChange.Length > 0 ? "  ·  " + _lastChange : "")}";

    public override void Reload()
    {
        if (!_loading) _ = ReloadAsync();
    }

    private sealed record LoadedData(
        List<(int? Value, string Text)> Classes, List<(int? Value, string Text)> Sections, List<(int? Value, string Text)> BloodGroups,
        List<(int? Value, string Text)> ConversationTypes, List<Family> Families, List<Student> Students,
        Dictionary<int, (int Id, string Name)> ClassSchool, List<(int? Value, string Text)> Schools);

    /// <summary>Runs on a background thread, so the screen opens straight away and stays responsive while the data comes in.</summary>
    private static LoadedData LoadData(ArqamDbContext db)
    {
        List<(int? Value, string Text)> Blank() => [(null, "")];
        var classes = Blank();
        classes.AddRange(db.Classes.OrderBy(c => c.SortName).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => ((int?)c.Id, c.Name ?? "")));
        var sections = Blank();
        sections.AddRange(db.Sections.OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")));
        var bloodGroups = Blank();
        bloodGroups.AddRange(db.BloodGroups.OrderBy(b => b.Id).AsEnumerable().Select(b => ((int?)b.Id, (b.Name ?? "").Split('\n')[0].Trim())));
        var convoTypes = db.ConversationTypes.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).AsEnumerable().Select(t => ((int?)t.Id, t.Name ?? "")).ToList();
        var families = db.Families.ToList();
        var students = db.Students.Include(s => s.Family).OrderBy(s => s.Name).ToList();
        var classSchool = (from c in db.Classes
                           join d in db.Departments on c.DepartmentId equals d.Id
                           join sc in db.Schools on d.SchoolId equals sc.Id
                           select new { ClassId = c.Id, SchoolId = sc.Id, sc.Name }).AsEnumerable()
            .ToDictionary(x => x.ClassId, x => (x.SchoolId, x.Name ?? ""));
        var schools = db.Schools.OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList();
        return new LoadedData(classes, sections, bloodGroups, convoTypes, families, students, classSchool, schools);
    }

    private async Task ReloadAsync()
    {
        using var t = Timing.Step("Students.Reload");
        var keep = _current?.Id;
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        UseWaitCursor = true;
        try
        {
            _loading = true;
            LoadedData data;
            try { data = await Task.Run(() => LoadData(db)); }
            finally { _loading = false; }
            t.Mark("load data (background)");
            if (!ReferenceEquals(db, _db) || IsDisposed) return; // a newer reload has started, or the tab was closed

            (_classes, _sections, _bloodGroups, _convoTypes, _allFamilies) = (data.Classes, data.Sections, data.BloodGroups, data.ConversationTypes, data.Families);
            (_classSchool, _schools) = (data.ClassSchool, data.Schools);
            var students = data.Students;

            FillFilter(_schoolFilter, "All schools", _schools);
            FillFilter(_classFilter, "All classes", _classes.Skip(1).Select(c => (c.Value, $"Class: {c.Text}")));
            FillFilter(_sectionFilter, "All sections", _sections.Skip(1).Select(s => (s.Value, $"Section: {s.Text}")));

            _grid.ResetColumns();
            _grid.AddText(nameof(Student.Id), "CC", 70);
            _grid.AddText(nameof(Student.Name), "Name", 180, fill: true);
            _grid.AddText(nameof(Student.FatherName), "Father name", 180, fill: true);
            _grid.AddCombo(nameof(Student.ClassId), "Class", _classes.Skip(1), 110).ReadOnly = true;
            _grid.AddCombo(nameof(Student.SectionId), "Section", _sections.Skip(1), 100).ReadOnly = true;
            _grid.AddText(nameof(Student.FamilyId), "Family", 70);
            _grid.AddText(nameof(Student.StatusText), "Status", 90);
            _grid.AddText(nameof(Student.AdmissionDate), "Admitted", 95, format: "dd/MM/yyyy");
            _grid.AddText(nameof(Student.WhatsApp), "WhatsApp", 130);
            _grid.AddText(nameof(Student.Mobile), "Mobile", 130);
            _grid.AddText(nameof(Student.MotherMobile), "MCell", 130);
            _grid.AddText(nameof(Student.EmergencyNo), "Emergency no.", 130);
            foreach (var number in new[] { nameof(Student.WhatsApp), nameof(Student.Mobile), nameof(Student.MotherMobile), nameof(Student.EmergencyNo) })
                _grid.FormatNumbers(number, NumberFormat.Phone);
            foreach (DataGridViewColumn c in _grid.Columns)
                if (c is DataGridViewComboBoxColumn combo) combo.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;

            t.Mark("filters + columns");
            _list = new FilterBindingList<Student>(students);
            _grid.Bind(_list);
            t.Mark("bind grid");
            FillLists();
            t.Mark("fill drop-downs");
            ApplyFilters();
            t.Mark("apply filters");
            if (keep is int id) SelectStudent(id);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load students.", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (_pendingNavigation is { } target)
        {
            _pendingNavigation = null;
            Navigate(target);
        }
        RaiseStatusChanged();
    }

    private static void FillFilter(ComboBox combo, string all, IEnumerable<(int? Value, string Text)> items)
    {
        var selected = combo.SelectedItem as Inputs.Item<int?>;
        var list = new List<Inputs.Item<int?>> { new(null, all) };
        list.AddRange(items.Select(i => new Inputs.Item<int?>(i.Value, i.Text)));
        combo.DataSource = list;
        combo.DisplayMember = nameof(Inputs.Item<int?>.Text);
        combo.SelectedItem = list.FirstOrDefault(i => selected is not null && i.Value == selected.Value) ?? list[0];
    }

    private void ApplyFilters()
    {
        if (_list is null) return;
        var term = _search.Text.Trim();
        var digits = new string(term.Where(char.IsDigit).ToArray());
        var schoolId = (_schoolFilter.SelectedItem as Inputs.Item<int?>)?.Value;
        var classId = (_classFilter.SelectedItem as Inputs.Item<int?>)?.Value;
        var sectionId = (_sectionFilter.SelectedItem as Inputs.Item<int?>)?.Value;
        var status = _statusFilter.SelectedIndex;

        _list.SetSearch(s =>
            (status == 2 || s.OnRoll == (status == 0) || s.Id == 0)
            && (schoolId is null || (s.ClassId is int c && _classSchool.TryGetValue(c, out var school) && school.Id == schoolId))
            && (classId is null || s.ClassId == classId)
            && (sectionId is null || s.SectionId == sectionId)
            && (term.Length == 0 || MatchesSearch(s, term, digits)));
        _grid.Invalidate();
        ShowFilterChip();
        RaiseStatusChanged();
    }

    /// <summary>"Filtered: Class = Five, Section = Rose" when the list is narrowed beyond the usual active students.</summary>
    private void ShowFilterChip()
    {
        static string? Name(ComboBox combo) => combo.SelectedIndex > 0 && combo.SelectedItem is Inputs.Item<int?> item ? item.Text.Split(": ").Last() : null;
        var parts = new List<string>();
        if (Name(_schoolFilter) is { } school) parts.Add($"School = {school}");
        if (Name(_classFilter) is { } cls) parts.Add($"Class = {cls}");
        if (Name(_sectionFilter) is { } section) parts.Add($"Section = {section}");
        if (_search.Text.Trim() is { Length: > 0 } term) parts.Add($"\"{term}\"");
        if (_list?.AnyFilter == true) parts.Add("column filters");
        _filterChip.Text = "Filtered: " + string.Join(", ", parts);
        _filterChip.BackColor = Theme.Selected;
        _filterChip.ForeColor = Theme.Text;
        _filterChip.Visible = parts.Count > 0;
    }

    private static bool MatchesSearch(Student s, string term, string digits) =>
        s.Id.ToString() == term
        || (s.Name?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (s.FatherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (digits.Length >= 4 && new[] { s.FormB, s.WhatsApp, s.Family?.Mobile, s.Family?.MotherMobile, s.Family?.PhoneHome }
            .Any(p => p is not null && new string(p.Where(char.IsDigit).ToArray()).Contains(digits)));

    private void ShowCurrent()
    {
        var next = _grid.CurrentRow?.DataBoundItem as Student;
        if (ReferenceEquals(next, _current)) return;
        using var t = Timing.Step($"Students.ShowCurrent CC {next?.Id}");
        CommitEdits();
        _current = next;
        _bs.DataSource = next ?? (object)typeof(Student);
        _generalHost.Enabled = next is not null;
        t.Mark("bind General");
        _family.Show(next?.Family);
        t.Mark("family");
        _ = ShowLastChangeAsync(next);
        ShowConversations(next);
        t.Mark("conversations");
        ShowDocuments(next);
        t.Mark("documents + photo");
        _ = ShowFeesAsync(next);
        RaiseStatusChanged();
    }

    /// <summary>"Last changed by … on …" for the status bar, read from the audit log in the background.</summary>
    private async Task ShowLastChangeAsync(Student? student)
    {
        _lastChange = "";
        if (student is not { Id: not 0 }) return;
        LastChange? change;
        try
        {
            change = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                return db.LastChangeOf("Students", student.Id);
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            return; // no audit log on this database, or no permission to read it
        }
        if (!ReferenceEquals(student, _current) || change is null) return;
        _lastChange = $"Last changed by {change.ChangedBy} on {change.ChangedAt:dd/MM/yyyy} · audited";
        RaiseStatusChanged();
    }

    private void ShowConversations(Student? student)
    {
        _convoGrid.ResetColumns();
        if (student is null || _db is null) return;
        if (student.Id != 0 && !_db.Entry(student).Collection(s => s.Conversations).IsLoaded)
            _db.Entry(student).Collection(s => s.Conversations).Load();

        _convoGrid.AddText(nameof(Conversation.Date), "Date", 95, format: "dd/MM/yyyy");
        _convoGrid.AddCombo(nameof(Conversation.TypeId), "Type", _convoTypes, 170);
        _convoGrid.AddText(nameof(Conversation.Transcript), "What was said", 250, fill: true);
        _convoGrid.AddCheck(nameof(Conversation.IsIssue), "Issue", 55);
        _convoGrid.AddCheck(nameof(Conversation.IsResolved), "Resolved", 70);
        _convoGrid.AddText(nameof(Conversation.Resolution), "Resolution", 180);
        _convoGrid.AddText(nameof(Conversation.EnteredBy), "Entered by", 100, readOnly: true);

        _convos = new FilterBindingList<Conversation>(student.Conversations.OrderByDescending(c => c.Date));
        _convos.ItemAdded += c => { c.EnteredBy = Ctx.Session.UserName; student.Conversations.Add(c); };
        _convos.ItemRemoved += c =>
        {
            if (_db.Entry(c).State is EntityState.Added or EntityState.Detached) student.Conversations.Remove(c);
            else _db.Remove(c);
        };
        _convoGrid.Bind(_convos);
    }

    // ----- Pictures & documents -----

    private void ShowDocuments(Student? student)
    {
        _documents = [];
        if (student is { Id: not 0 })
        {
            try
            {
                _documents = _store.List(student.Id);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                Program.ShowError("Could not load the student's pictures and documents.", ex);
            }
        }
        _docGrid.ResetColumns();
        _docGrid.AddText(nameof(StudentDocument.TypeText), "Type", 150);
        _docGrid.AddText(nameof(StudentDocument.FileName), "File name", 260, fill: true);
        _docGrid.AddText(nameof(StudentDocument.FileSizeKB), "Size (KB)", 75);
        _docGrid.AddText(nameof(StudentDocument.Notes), "Notes", 180);
        _docGrid.AddText(nameof(StudentDocument.CreatedOn), "Added on", 95, format: "dd/MM/yyyy");
        _docGrid.AddText(nameof(StudentDocument.CreatedBy), "Added by", 90);
        _docGrid.Bind(new FilterBindingList<StudentDocument>(_documents));

        var saved = student is { Id: not 0 };
        foreach (var b in _docEditButtons) b.Enabled = _canEdit && saved;
        _docInfo.Text = student is null ? ""
            : !saved ? "Save the new student first, then add pictures and documents."
            : $"{_documents.Count} of {DocumentTypes.MaxPerStudent}   ·   Saved in {_store.Folder}";

        // The profile picture; students from Access may still have a picture path instead.
        var profile = _documents.FirstOrDefault(d => d.DocType == DocumentTypes.ProfilePicture);
        ShowPhoto(profile?.FilePath ?? student?.PicturePath);
    }

    private void ShowPhoto(string? path)
    {
        var old = _photo.Image;
        _photo.Image = null;
        old?.Dispose();
        if (string.IsNullOrWhiteSpace(path) || _store.ReadBytes(path) is not { } bytes) return;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            StudentDocumentStore.ApplyOrientation(image);
            _photo.Image = new Bitmap(image);
        }
        catch (ArgumentException)
        {
        }
    }

    private void ChoosePhoto() => AddDocument(DocumentTypes.ProfilePicture);

    private void AddDocument(string? type)
    {
        if (_current is not { Id: not 0 } student || !_canEdit)
        {
            if (_current is { Id: 0 })
                KryptonMessageBox.Show(this, "Save the new student first, then add pictures and documents.", "Students",
                    KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        if (_db?.Entry(student).Property(s => s.Name).IsModified == true)
        {
            KryptonMessageBox.Show(this, "Save the student's name change first; the files are named after the student.", "Students",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }

        using var form = new AddDocumentForm(student.Name ?? $"CC {student.Id}", type);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        var existing = _documents.FirstOrDefault(d => d.DocType == form.DocType);
        if (existing is not null && KryptonMessageBox.Show(this, $"{student.Name} already has a {existing.TypeText.ToLower()}. Replace it?",
                "Pictures & documents", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            UseWaitCursor = true;
            _store.Add(student.Id, student.Name, form.DocType, form.FilePath, form.Notes);
        }
        catch (DocumentProblem ex)
        {
            KryptonMessageBox.Show(this, ex.Message, "Pictures & documents", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError($"Could not save the file in {_store.Folder}.", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
        ShowDocuments(student);
    }

    private StudentDocument? SelectedDocument => _docGrid.CurrentRow?.DataBoundItem as StudentDocument;

    private void OpenDocument()
    {
        if (SelectedDocument is not { } doc) return;
        try
        {
            _store.Open(doc);
        }
        catch (DocumentProblem ex)
        {
            KryptonMessageBox.Show(this, ex.Message, "Pictures & documents", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Program.ShowError("Could not open the file.", ex);
        }
    }

    private void SaveDocumentCopy()
    {
        if (SelectedDocument is not { } doc) return;
        using var dlg = new SaveFileDialog { FileName = doc.FileName, Filter = $"{doc.TypeText}|*{Path.GetExtension(doc.FilePath)}" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _store.SaveCopy(doc, dlg.FileName);
        }
        catch (DocumentProblem ex)
        {
            KryptonMessageBox.Show(this, ex.Message, "Pictures & documents", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Program.ShowError("Could not save the copy.", ex);
        }
    }

    private void DeleteDocument()
    {
        if (SelectedDocument is not { } doc || _current is null || !_canEdit) return;
        if (KryptonMessageBox.Show(this, $"Delete {doc.TypeText.ToLower()} ({doc.FileName})?\n\nThe file is moved to the Deleted folder.",
                "Pictures & documents", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            _store.Delete(doc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("Could not delete the file.", ex);
        }
        ShowDocuments(_current);
    }

    // ----- Printing and export -----

    private static string TextOf(List<(int? Value, string Text)> items, int? value) =>
        value is null ? "" : items.FirstOrDefault(i => i.Value == value).Text ?? "";

    /// <summary>The details a printout needs. The photo is the profile picture (read from the attachments folder).</summary>
    private Printing.StudentPrint PrintInfo(Student s, bool withPhoto)
    {
        byte[]? photo = null;
        if (withPhoto && s.Id != 0)
        {
            try
            {
                var docs = ReferenceEquals(s, _current) ? _documents : _store.List(s.Id);
                var path = docs.FirstOrDefault(d => d.DocType == DocumentTypes.ProfilePicture)?.FilePath ?? s.PicturePath;
                if (!string.IsNullOrWhiteSpace(path) && StudentDocumentStore.IsPicture(path)) photo = _store.ReadBytes(path);
            }
            catch (Microsoft.Data.SqlClient.SqlException) { } // printed without the photo
        }
        var school = s.ClassId is int c && _classSchool.TryGetValue(c, out var sc) && sc.Name.Length > 0 ? sc.Name : Printing.PrintKit.DefaultSchoolName;
        return new Printing.StudentPrint(
            s.Id, s.DsNo, s.Name ?? "", s.Sex == "FEMALE", s.FatherName ?? "", s.BirthDate, s.AdmissionDate,
            TextOf(_classes, s.ClassId), TextOf(_sections, s.SectionId), school, s.LeavingDate, s.LeavingReason, s.Remarks,
            TextOf(_bloodGroups, s.BloodGroupId), s.FamilyId, s.Family?.Mobile ?? s.WhatsApp, photo);
    }

    /// <summary>The selected student, or a message if there is none (or it is not saved yet).</summary>
    private Student? StudentToPrint()
    {
        CommitEdits();
        if (_current is { Id: not 0 } s) return s;
        NotYet(_current is null ? "Select a student first." : "Save the new student first.");
        return null;
    }

    private void PrintIdCards()
    {
        if (StudentToPrint() is not { } current) return;
        var shown = _list?.Where(s => s.Id != 0).ToList() ?? [];
        var students = new List<Student> { current };
        if (shown.Count > 1)
        {
            var answer = KryptonMessageBox.Show(this,
                $"Print ID cards for all {shown.Count} students in the list?\n\nYes: all {shown.Count} (ten to a page)\nNo: only {current.Name}",
                "ID cards", KryptonMessageBoxButtons.YesNoCancel, KryptonMessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) students = shown;
        }
        UseWaitCursor = true;
        List<Printing.StudentPrint> cards;
        Printing.SchoolPrint school;
        try { cards = students.Select(s => PrintInfo(s, withPhoto: true)).ToList(); school = SchoolDetails(); }
        finally { UseWaitCursor = false; }
        PrintPreviewForm.Show(this, () => Printing.IdCards.Create(cards, school), "ID cards",
            cards.Count == 1 ? $"ID card {current.Id} {current.Name}" : $"ID cards ({cards.Count})");
    }

    private void PrintLeavingCertificate()
    {
        if (StudentToPrint() is not { } s) return;
        if (s.OnRoll || s.LeavingDate is null)
        {
            NotYet("The leaving certificate is for students who have left: set Status to Left or Transfer, fill in the leaving date, and Save.");
            return;
        }
        var (info, school) = (PrintInfo(s, withPhoto: false), SchoolDetails());
        PrintPreviewForm.Show(this, () => Printing.Certificates.Leaving(info, school), "School leaving certificate", $"Leaving certificate {s.Id} {s.Name}");
    }

    private void PrintBonafideCertificate()
    {
        if (StudentToPrint() is not { } s) return;
        if (!s.OnRoll)
        {
            NotYet("The bonafide certificate is for active students. This student's status is " + s.StatusChoice + ".");
            return;
        }
        var (info, school) = (PrintInfo(s, withPhoto: false), SchoolDetails());
        PrintPreviewForm.Show(this, () => Printing.Certificates.Bonafide(info, school), "Bonafide certificate", $"Bonafide certificate {s.Id} {s.Name}");
    }

    private void ExportExcel()
    {
        if (_list is null) return;
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"Students {DateTime.Today:yyyy-MM-dd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Printing.ListExport.From(_grid, "Students").SaveExcel(dialog.FileName);
        }
        catch (IOException ex)
        {
            Program.ShowError("Could not save the Excel file. Is it open in Excel?", ex);
            return;
        }
        if (KryptonMessageBox.Show(this, $"Saved {Path.GetFileName(dialog.FileName)}. Open it now?", "Excel",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Information) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }

    private void ExportPdf()
    {
        if (_list is null) return;
        var title = "Students" + (_filterChip.Visible ? " · " + _filterChip.Text : "");
        var export = Printing.ListExport.From(_grid, title);
        PrintPreviewForm.Show(this, export.ToPdf, "Student list", $"Students {DateTime.Today:yyyy-MM-dd}");
    }

    // ----- Records -----

    protected override void CommitPendingEdits() => CommitEdits();

    private void CommitEdits()
    {
        FindForm()?.Validate(); // pushes the value of the box being typed in
        _bs.EndEdit();
        _family.Commit();
        _convoGrid.EndEdit();
    }

    public override void NewRecord()
    {
        if (!_canEdit || _db is null || _list is null) return;
        using var pick = new PickFamilyForm(_allFamilies);
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        AddStudent(pick.IsNewFamily ? null : pick.SelectedFamily);
    }

    private void AddStudent(Family? family)
    {
        if (_db is null || _list is null) return;
        var isNewFamily = family is null;
        family ??= new Family();
        var student = new Student
        {
            Family = family,
            AdmissionDate = DateTime.Today,
            OnRoll = true,
            Sex = null,
        };
        _db.Students.Add(student);
        if (isNewFamily) _allFamilies.Add(family);
        _search.Text = "";
        _list.AddExternal(student);
        SelectStudent(student);
        _tabs.SelectedIndex = 0;
        RaiseStatusChanged();
        KryptonMessageBox.Show(this,
            isNewFamily ? "Fill in the student's details on General, then the father's and mother's details on the Family tab. Press Save when done."
                        : $"The student is added to family {family.Id} ({family.FatherName}). Fill in the details and press Save.",
            "New student", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
    }

    private void ChangeFamily()
    {
        if (_current is null || !_canEdit) return;
        using var pick = new PickFamilyForm(_allFamilies, "Change family");
        if (pick.ShowDialog(this) != DialogResult.OK) return;
        var family = pick.IsNewFamily ? new Family() : pick.SelectedFamily!;
        if (pick.IsNewFamily) _allFamilies.Add(family);
        _current.Family = family;
        _current.FamilyId = family.Id == 0 ? null : family.Id;
        _bs.ResetBindings(false);
        _family.Show(family);
        _grid.Invalidate();
        RaiseStatusChanged();
    }

    public override bool SaveChanges()
    {
        if (_db is null || _loading || !_canEdit) return true;
        CommitEdits();
        var changed = _db.ChangeTracker.Entries<Student>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToList();
        var problem = changed.Select(s =>
            string.IsNullOrWhiteSpace(s.Name) ? $"CC {(s.Id == 0 ? "(new)" : s.Id)} needs a name."
            : s.Family is null && s.FamilyId is null ? $"{s.Name} needs a family."
            : s.ClassId is null && s.OnRoll ? $"{s.Name} needs a class."
            : null).FirstOrDefault(p => p is not null) ?? FormBProblem();
        if (problem is not null)
        {
            KryptonMessageBox.Show(this, problem, "Students", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            return false;
        }
        // Students whose name was corrected: their files are renamed to match after saving.
        var renamed = _db.ChangeTracker.Entries<Student>()
            .Where(e => e.State == EntityState.Modified && e.Property(s => s.Name).IsModified
                        && !string.Equals(e.Property(s => s.Name).OriginalValue, e.Entity.Name, StringComparison.Ordinal))
            .Select(e => e.Entity).ToList();
        try
        {
            _db.SaveChanges();
            RenameDocuments(renamed);
            _grid.Invalidate();
            ShowCurrentAgain();
            RaiseStatusChanged();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            KryptonMessageBox.Show(this, "Someone else changed this family while you were editing. Your changes were not saved; the list will reload.",
                "Students", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            Reload();
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save.", ex);
            return false;
        }
    }

    /// <summary>
    /// A Form-B number that was typed or changed must be blank or 13 digits, and no other student may have it.
    /// Numbers already stored are not checked unless they are changed.
    /// </summary>
    private string? FormBProblem()
    {
        var typed = _db!.ChangeTracker.Entries<Student>()
            .Where(e => e.State == EntityState.Added && !string.IsNullOrWhiteSpace(e.Entity.FormB)
                        || e.State == EntityState.Modified && e.Property(s => s.FormB).IsModified)
            .Select(e => e.Entity).Where(s => !string.IsNullOrWhiteSpace(s.FormB)).ToList();
        if (typed.Count == 0) return null;
        foreach (var s in typed)
        {
            var digits = NumberFormat.Digits(s.FormB);
            if (digits.Length != NumberFormat.FormBLength || s.FormB!.Any(c => !char.IsDigit(c) && c != '-'))
                return $"{s.Name}'s Form-B no. must be {NumberFormat.FormBLength} digits (like 12345-1234567-0), or left blank.";
        }
        // Few students have a Form-B number, so they are compared here by their digits (stored with or without dashes).
        var ids = typed.Select(s => s.Id).ToList();
        var stored = _db.Students.AsNoTracking().Where(s => s.FormB != null && s.FormB != "" && !ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.FormB }).ToList();
        foreach (var s in typed)
        {
            var digits = NumberFormat.Digits(s.FormB);
            var (otherId, otherName) = stored.Where(o => NumberFormat.Digits(o.FormB) == digits).Select(o => (o.Id, o.Name)).FirstOrDefault();
            if (otherName is null)
                (otherId, otherName) = typed.Where(o => !ReferenceEquals(o, s) && NumberFormat.Digits(o.FormB) == digits).Select(o => (o.Id, o.Name)).FirstOrDefault();
            if (otherName is not null || otherId != 0)
                return $"Form-B no. {NumberFormat.Cnic(digits)} is already given to {otherName}{(otherId == 0 ? "" : $" (CC {otherId})")}. Each child's Form-B no. is different.";
        }
        return null;
    }

    private void RenameDocuments(List<Student> students)
    {
        foreach (var s in students)
        {
            try
            {
                _store.RenameForStudent(s.Id, s.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or Microsoft.Data.SqlClient.SqlException)
            {
                Program.ShowError($"{s.Name} was saved, but their pictures and documents could not be renamed to match the new name.", ex);
            }
        }
    }

    private void ShowCurrentAgain()
    {
        var s = _current;
        _current = null;
        if (s is not null) SelectStudent(s);
    }

    public override void DeleteRecord()
    {
        if (_current is null || _db is null || _list is null) return;
        if (!Ctx.Session.IsAdmin)
        {
            KryptonMessageBox.Show(this, "Only an Admin can delete a student. To show that a student has left, set Status to Left or Transfer and fill in the leaving date.",
                "Students", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        if (KryptonMessageBox.Show(this, $"Delete {_current.Name} (CC {_current.Id})? Usually you should mark the student as left instead.\n\nThe student is removed when you press Save.",
                "Students", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        var s = _current;
        _db.Students.Remove(s);
        _list.RemoveExternal(s);
        RaiseStatusChanged();
    }

    public override void SortAscending() => _grid.SortSelectedColumn(ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(ListSortDirection.Descending);
    public override void ClearFilters()
    {
        _search.Text = "";
        _schoolFilter.SelectedIndex = _classFilter.SelectedIndex = _sectionFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndex = 2;
        _grid.ClearAllFilters();
    }
    public override void FocusSearch() => _search.Focus();

    public override void Navigate(object target)
    {
        if (_list is null)
        {
            _pendingNavigation = target;
            return;
        }
        switch (target)
        {
            case int cc:
                if (_list.AllItems.FirstOrDefault(s => s.Id == cc) is { } s)
                {
                    if (!_list.Contains(s)) ClearFilters();
                    SelectStudent(s);
                }
                break;
            case NewStudentInFamily n when _canEdit:
                AddStudent(_allFamilies.FirstOrDefault(f => f.Id == n.FamilyId));
                break;
        }
    }

    private void SelectStudent(int cc)
    {
        if (_list?.FirstOrDefault(s => s.Id == cc) is { } s) SelectStudent(s);
    }

    private void SelectStudent(Student s)
    {
        if (_list is null) return;
        var index = _list.IndexOf(s);
        if (index < 0 || index >= _grid.Rows.Count) return;
        _grid.CurrentCell = _grid.Rows[index].Cells[0];
        _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 2);
        ShowCurrent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Theme.Changed -= OnThemeChanged;
            _db?.Dispose();
            _photo.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
