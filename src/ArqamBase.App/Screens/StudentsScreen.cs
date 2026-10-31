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
    private readonly KryptonComboBox _classFilter = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonComboBox _sectionFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonComboBox _statusFilter = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AccessGrid _grid = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly KryptonLabel _header = new() { Dock = DockStyle.Top, LabelStyle = LabelStyle.TitlePanel, AutoSize = true };
    private readonly KryptonNavigator _tabs = new() { Dock = DockStyle.Fill, NavigatorMode = NavigatorMode.BarTabGroup };
    private readonly BindingSource _bs = new() { DataSource = typeof(Student) };
    private readonly PictureBox _photo = new() { Size = new Size(150, 180), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly KryptonPanel _detail = new() { Dock = DockStyle.Fill };
    private readonly KryptonPanel _generalHost = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
    private readonly FamilyFields _family;
    private readonly AccessGrid _convoGrid = new();
    private readonly KryptonButton _changeFamily = new() { Text = "Change family…", AutoSize = true };
    private readonly AccessGrid _docGrid = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly KryptonLabel _docInfo = new() { Text = "", AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    private readonly List<Control> _docEditButtons = [];
    private readonly StudentDocumentStore _store;
    private List<StudentDocument> _documents = [];

    private ArqamDbContext? _db;
    private FilterBindingList<Student>? _list;
    private FilterBindingList<Conversation>? _convos;
    private List<(int? Value, string Text)> _classes = [], _sections = [], _bloodGroups = [];
    private List<Family> _allFamilies = [];
    private Student? _current;
    private object? _pendingNavigation;

    public StudentsScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Students);
        _family = new FamilyFields(_canEdit) { Dock = DockStyle.Fill };
        _store = new StudentDocumentStore(ctx.Config, ctx.Session);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _classFilter, _sectionFilter, _statusFilter]);
        _statusFilter.Items.AddRange(["On roll", "Not on roll", "All students"]);
        _statusFilter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilters();
        _classFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _sectionFilter.SelectedIndexChanged += (_, _) => ApplyFilters();
        _statusFilter.SelectedIndexChanged += (_, _) => ApplyFilters();

        _grid.SelectionChanged += (_, _) => ShowCurrent();
        _grid.FilterChanged += (_, _) => RaiseStatusChanged();

        _changeFamily.Click += (_, _) => ChangeFamily();
        BuildGeneralTab();
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.Hide;
        _tabs.Button.ContextButtonDisplay = ButtonDisplay.Hide;
        _tabs.Pages.Add(Page("General", _generalHost));
        _tabs.Pages.Add(Page("Family", FamilyTab()));
        _tabs.Pages.Add(Page("Conversations", ConversationTab()));
        _tabs.Pages.Add(Page("Pictures & documents", DocumentsTab()));
        _tabs.Pages.Add(Page("Fees", Later("The fee ledger for this student comes in phase 3. Until then, use Access.")));
        _tabs.Pages.Add(Page("Attendance", Later("Attendance history comes with the attendance module (phase 6).")));
        _detail.Controls.Add(_tabs);
        _detail.Controls.Add(_header);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);
        WhenSized(split, () => split.SplitterDistance = Math.Max(160, split.Height * 9 / 20));

        Controls.Add(split);
        Controls.Add(bar);
        HandleCreated += (_, _) => Reload();
    }

    // ----- Layout -----

    private static KryptonPage Page(string text, Control content)
    {
        var page = new KryptonPage { Text = text, TextTitle = text, Padding = new Padding(6) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static Control Later(string text) => new KryptonLabel { Text = text, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

    private void BuildGeneralTab()
    {
        // Rebuilt after loading so the drop-downs get the class, section and blood group lists.
        // Take out the controls that are reused before the old panels are disposed.
        _changeFamily.Parent?.Controls.Remove(_changeFamily);
        _photo.Parent?.Controls.Remove(_photo);
        foreach (var old in _generalHost.Controls.Cast<Control>().ToList())
        {
            _generalHost.Controls.Remove(old);
            old.Dispose();
        }
        var f = new FieldPanel(4);
        var cc = f.Add("CC", Inputs.Text(_bs, nameof(Student.Id), readOnly: true));
        cc.DataBindings[0].Format += (_, e) => { if (e.Value is 0) e.Value = "(new)"; };
        f.Add("DS#", Inputs.Text(_bs, nameof(Student.DsNo)));
        var famId = Inputs.Text(_bs, nameof(Student.FamilyId), readOnly: true);
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
        f.Add("Form-B no.", Inputs.Text(_bs, nameof(Student.FormB)));
        f.Add("Admission date", Inputs.Date(_bs, nameof(Student.AdmissionDate)));
        f.Add("Admission class", Inputs.Text(_bs, nameof(Student.AdmissionClass)));
        f.Add("Current class", Inputs.Combo(_bs, nameof(Student.ClassId), _classes));
        f.Add("Section", Inputs.Combo(_bs, nameof(Student.SectionId), _sections));
        f.Add("Blood group", Inputs.Combo(_bs, nameof(Student.BloodGroupId), _bloodGroups));
        f.Add("WhatsApp", Inputs.Text(_bs, nameof(Student.WhatsApp)));
        f.Add("Email", Inputs.Text(_bs, nameof(Student.Email)), 2);
        f.Add("", Inputs.Check(_bs, nameof(Student.OnRoll), "Currently in school (on roll)"));
        f.Add("Left as", Inputs.Combo<string>(_bs, nameof(Student.LeavingType), [(null, ""), ("Left", "Left"), ("Transfer", "Transfer")]));
        f.Add("Leaving date", Inputs.Date(_bs, nameof(Student.LeavingDate)));
        f.Add("Reason for leaving", Inputs.Text(_bs, nameof(Student.LeavingReason)));
        f.Add("Remarks", Inputs.Text(_bs, nameof(Student.Remarks), multiline: true), 4);
        f.Enabled = _canEdit;

        var photoPanel = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 170, FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent, Padding = new Padding(8, 0, 0, 0) };
        photoPanel.Controls.Add(_photo);
        var change = new KryptonButton { Text = "Change photo…", Width = 150, Enabled = _canEdit };
        change.Click += (_, _) => ChoosePhoto();
        photoPanel.Controls.Add(change);

        _generalHost.Controls.Add(f);
        _generalHost.Controls.Add(photoPanel);
        _changeFamily.Enabled = _canEdit;
    }

    private Control FamilyTab()
    {
        var host = new KryptonPanel();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        var open = new KryptonButton { Text = "Open in Family screen", AutoSize = true };
        open.Click += (_, _) => { if (_current?.FamilyId is int id) Ctx.OpenScreenAt("family", id); };
        bar.Controls.Add(open);
        bar.Controls.Add(new KryptonLabel { Text = "Changes here apply to the whole family (all brothers and sisters).", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        host.Controls.Add(_family);
        host.Controls.Add(bar);
        return host;
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
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent, WrapContents = false };
        KryptonButton Button(string text, Action click, bool edit)
        {
            var b = new KryptonButton { Text = text, AutoSize = true };
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
        _docGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenDocument(); };
        host.Controls.Add(_docGrid);
        host.Controls.Add(bar);
        return host;
    }

    // ----- Loading -----

    public override bool SupportsRecords => true;
    public override bool HasChanges => _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…" : $"Students: {_grid.RecordText()}{(HasChanges ? "  ·  unsaved changes" : "")}";

    public override void Reload()
    {
        var keep = _current?.Id;
        _db?.Dispose();
        _db = Ctx.Config.CreateContext(Ctx.Session);
        UseWaitCursor = true;
        try
        {
            _classes = [(null, "")];
            _classes.AddRange(_db.Classes.OrderBy(c => c.SortName).Select(c => new { c.Id, c.Name }).AsEnumerable().Select(c => ((int?)c.Id, c.Name ?? "")));
            _sections = [(null, "")];
            _sections.AddRange(_db.Sections.OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")));
            _bloodGroups = [(null, "")];
            _bloodGroups.AddRange(_db.BloodGroups.OrderBy(b => b.Id).AsEnumerable().Select(b => ((int?)b.Id, (b.Name ?? "").Split('\n')[0].Trim())));

            _allFamilies = _db.Families.ToList();
            var students = _db.Students.Include(s => s.Family).OrderBy(s => s.Name).ToList();

            FillFilter(_classFilter, "All classes", _classes.Skip(1));
            FillFilter(_sectionFilter, "All sections", _sections.Skip(1));

            _grid.ResetColumns();
            _grid.AddText(nameof(Student.Id), "CC", 70);
            _grid.AddText(nameof(Student.Name), "Name", 180, fill: true);
            _grid.AddText(nameof(Student.FatherName), "Father name", 180, fill: true);
            _grid.AddCombo(nameof(Student.ClassId), "Class", _classes.Skip(1), 110).ReadOnly = true;
            _grid.AddCombo(nameof(Student.SectionId), "Section", _sections.Skip(1), 100).ReadOnly = true;
            _grid.AddText(nameof(Student.FamilyId), "Family", 70);
            _grid.AddText(nameof(Student.StatusText), "Status", 90);
            _grid.AddText(nameof(Student.AdmissionDate), "Admitted", 95, format: "dd/MM/yyyy");
            _grid.AddText(nameof(Student.WhatsApp), "WhatsApp", 120);
            foreach (DataGridViewColumn c in _grid.Columns)
                if (c is DataGridViewComboBoxColumn combo) combo.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;

            _list = new FilterBindingList<Student>(students);
            _grid.Bind(_list);
            BuildGeneralTab();
            ApplyFilters();
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

    private static void FillFilter(KryptonComboBox combo, string all, IEnumerable<(int? Value, string Text)> items)
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
        var classId = (_classFilter.SelectedItem as Inputs.Item<int?>)?.Value;
        var sectionId = (_sectionFilter.SelectedItem as Inputs.Item<int?>)?.Value;
        var status = _statusFilter.SelectedIndex;

        _list.SetSearch(s =>
            (status == 2 || s.OnRoll == (status == 0) || s.Id == 0)
            && (classId is null || s.ClassId == classId)
            && (sectionId is null || s.SectionId == sectionId)
            && (term.Length == 0 || MatchesSearch(s, term, digits)));
        _grid.Invalidate();
        RaiseStatusChanged();
    }

    private static bool MatchesSearch(Student s, string term, string digits) =>
        s.Id.ToString() == term
        || (s.Name?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (s.FatherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (s.FormB?.Contains(term) ?? false)
        || (digits.Length >= 4 && new[] { s.WhatsApp, s.Family?.Mobile, s.Family?.MotherMobile, s.Family?.PhoneHome }
            .Any(p => p is not null && new string(p.Where(char.IsDigit).ToArray()).Contains(digits)));

    private void ShowCurrent()
    {
        var next = _grid.CurrentRow?.DataBoundItem as Student;
        if (ReferenceEquals(next, _current)) return;
        CommitEdits();
        _current = next;
        _bs.DataSource = next ?? (object)typeof(Student);
        _generalHost.Enabled = next is not null;
        _family.Show(next?.Family);
        _header.Text = next is null ? "" : $"{next.Name}  {(next.Sex == "FEMALE" ? "D/o" : "S/o")} {next.FatherName}   ·   CC {(next.Id == 0 ? "(new)" : next.Id)}";
        ShowConversations(next);
        ShowDocuments(next);
        RaiseStatusChanged();
    }

    private void ShowConversations(Student? student)
    {
        _convoGrid.ResetColumns();
        if (student is null || _db is null) return;
        if (student.Id != 0 && !_db.Entry(student).Collection(s => s.Conversations).IsLoaded)
            _db.Entry(student).Collection(s => s.Conversations).Load();

        var types = _db.ConversationTypes.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToList();
        _convoGrid.AddText(nameof(Conversation.Date), "Date", 95, format: "dd/MM/yyyy");
        _convoGrid.AddCombo(nameof(Conversation.TypeId), "Type", types.Select(t => ((int?)t.Id, t.Name ?? "")), 170);
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
        if (_db is null || !_canEdit) return true;
        CommitEdits();
        var changed = _db.ChangeTracker.Entries<Student>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToList();
        var problem = changed.Select(s =>
            string.IsNullOrWhiteSpace(s.Name) ? $"CC {(s.Id == 0 ? "(new)" : s.Id)} needs a name."
            : s.Family is null && s.FamilyId is null ? $"{s.Name} needs a family."
            : s.ClassId is null && s.OnRoll ? $"{s.Name} needs a class."
            : null).FirstOrDefault(p => p is not null);
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
            KryptonMessageBox.Show(this, "Only an Admin can delete a student. To show that a student has left, untick \"Currently in school\" and fill in the leaving date.",
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
        _classFilter.SelectedIndex = _sectionFilter.SelectedIndex = 0;
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
            _db?.Dispose();
            _photo.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
