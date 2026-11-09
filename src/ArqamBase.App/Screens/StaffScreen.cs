using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Staff on the left; on the right the selected person's details, job, pay and interview (replaces frmStaff).
/// Only an Admin changes them or sees the pay figures, the picture and the documents.
/// </summary>
public sealed class StaffScreen : ScreenBase
{
    public sealed class Row
    {
        public required StaffMember Member { get; init; }
        public int Id => Member.Id;
        public string Name => Member.Name ?? "";
        public string Post { get; init; } = "";
    }

    private readonly bool _canEdit, _isAdmin;
    private readonly KryptonTextBox _search = new() { Dock = DockStyle.Top, CueHint = { CueHintText = "Search name, post or phone" } };
    private readonly ComboBox _show = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AccessGrid _staff = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false,
    };
    private readonly BindingSource _bs = new() { DataSource = typeof(StaffMember) };
    private readonly ComboBox _type, _campus;
    private readonly StatCard _salaryCard = new("Current salary"), _advanceCard = new("Advance owed"), _absentCard = new("Absent or leave this month");
    private readonly KryptonPanel _details = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 8, 0) };
    private readonly PictureBox _photo = new() { Size = new Size(150, 180), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly AccessGrid _docGrid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false, Height = 345,
    };
    private readonly Label _docInfo = new() { Text = "", AutoSize = true, BackColor = Color.Transparent };
    private readonly FlowLayoutPanel _docBar = new() { BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
    private readonly List<Control> _docEditButtons = [];
    private readonly StaffDocumentStore _store;
    private List<StaffDocument> _documents = [];
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private ArqamDbContext? _db;
    private FilterBindingList<Row>? _list;
    private StaffMember? _current;
    private Dictionary<int, decimal> _owed = [];
    private Dictionary<int, int> _absent = [];
    private bool _loading;

    public StaffScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Staff);
        _isAdmin = ctx.Session.IsAdmin;
        _store = new StaffDocumentStore(ctx.Config, ctx.Session);
        _show.Items.AddRange(["Working staff", "Staff who left", "All staff"]);
        _show.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilters();
        _show.SelectedIndexChanged += (_, _) => ApplyFilters();
        _staff.SelectionChanged += (_, _) => { if (!_loading) ShowCurrent(); };
        _staff.AddText(nameof(Row.Name), "Name", 170, fill: true);
        _staff.AddText(nameof(Row.Post), "Post", 110);

        var left = new KryptonPanel { Dock = DockStyle.Fill };
        left.Controls.Add(_staff);
        left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent });
        left.Controls.Add(_show);
        left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent });
        left.Controls.Add(_search);

        var personal = new FieldPanel(4);
        personal.Add("Employee ID", Inputs.Text(_bs, nameof(StaffMember.Id), readOnly: true));
        personal.Add("Title", Inputs.Combo<string>(_bs, nameof(StaffMember.Salute), [(null, ""), ("Mr.", "Mr."), ("Miss", "Miss"), ("Mrs.", "Mrs.")]));
        personal.Add("Name", Inputs.Text(_bs, nameof(StaffMember.Name)), 2);
        personal.Add("Father / spouse name", Inputs.Text(_bs, nameof(StaffMember.FatherName)), 2);
        personal.Add("Name (Urdu)", Inputs.Text(_bs, nameof(StaffMember.NameUrdu), rightToLeft: true), 2);
        personal.Add("Date of birth", Inputs.DateText(_bs, nameof(StaffMember.BirthDate)));
        personal.Add("Marital status", Inputs.Text(_bs, nameof(StaffMember.MaritalStatus)));
        personal.Add("Cell no.", Inputs.Phone(_bs, nameof(StaffMember.CellNo)));
        personal.Add("Email", Inputs.Text(_bs, nameof(StaffMember.Email)));
        var home = personal.Add("Home phone", Inputs.Text(_bs, nameof(StaffMember.HomePhone)));
        home.MaxLength = 10;
        personal.Add("CNIC no.", Inputs.Text(_bs, nameof(StaffMember.Cnic)));
        personal.Add("Emergency no.", Inputs.Text(_bs, nameof(StaffMember.EmergencyNo)));
        personal.Add("Cast", Inputs.Text(_bs, nameof(StaffMember.Caste)));
        personal.Add("Religion", Inputs.Text(_bs, nameof(StaffMember.Religion)));
        personal.Add("Address", Inputs.Text(_bs, nameof(StaffMember.Address)), 4);

        _type = Inputs.Combo<int?>(_bs, nameof(StaffMember.TypeId), [(null, "")]);
        _campus = Inputs.Combo<int?>(_bs, nameof(StaffMember.BranchId), [(null, "")]);
        var job = new FieldPanel(4);
        job.Add("Post", _type);
        job.Add("Designation", Inputs.Text(_bs, nameof(StaffMember.Designation)));
        job.Add("Campus", _campus, 2);
        job.Add("Date of joining", Inputs.DateText(_bs, nameof(StaffMember.DateHired)));
        job.Add("Last degree", Inputs.Text(_bs, nameof(StaffMember.Education)));
        job.Add("Subjects", Inputs.Text(_bs, nameof(StaffMember.Subjects)));
        job.Add("Previous experience (years)", Inputs.Amount(_bs, nameof(StaffMember.PrevExperience)));
        job.Add("Left on", Inputs.DateText(_bs, nameof(StaffMember.LeftOn)));
        job.Add("Why left", Inputs.Text(_bs, nameof(StaffMember.LeftReason)), 2);
        job.Add("", Inputs.Check(_bs, nameof(StaffMember.Flag), "Working currently"));

        var pay = new FieldPanel(4);
        pay.Add("Salary sheet", Inputs.Combo<byte?>(_bs, nameof(StaffMember.SalarySheet),
            new (byte?, string)[] { (null, "") }.Concat(Payroll.Sheets.Select(s => ((byte?)s.Id, s.Name)))));
        pay.Add("Current salary", Inputs.Amount(_bs, nameof(StaffMember.CurrentSalary)));
        pay.Add("Salary when hired", Inputs.Amount(_bs, nameof(StaffMember.Salary)));
        pay.Add("ST perk", Inputs.Amount(_bs, nameof(StaffMember.StPerk)));
        pay.Add("Fuel allowance", Inputs.Amount(_bs, nameof(StaffMember.FuelAllowance)));
        pay.Add("Last increment", Inputs.Amount(_bs, nameof(StaffMember.LastIncrement)));
        pay.Add("SCF (G.P) each month", Inputs.Amount(_bs, nameof(StaffMember.Scf)));
        pay.Add("Security each month", Inputs.Amount(_bs, nameof(StaffMember.SecDeduction)));
        pay.Add("Paid by", Inputs.Combo<string>(_bs, nameof(StaffMember.PayMethod), new (string?, string)[] { (null, "") }.Concat(Payroll.PayMethods.Select(m => ((string?)m, m)))));
        pay.Add("Bank account no.", Inputs.Text(_bs, nameof(StaffMember.BankAccount)));

        var interview = new FieldPanel(4);
        interview.Add("Total test marks", Inputs.Amount(_bs, nameof(StaffMember.TotalTestMarks)));
        interview.Add("Obtained marks", Inputs.Amount(_bs, nameof(StaffMember.ObtainedTestMarks)));
        interview.Add("Reference", Inputs.Text(_bs, nameof(StaffMember.Reference)), 2);
        interview.Add("Remarks of interviewer", Inputs.Text(_bs, nameof(StaffMember.InterviewRemarks)), 4);

        foreach (var panel in new[] { personal, job, pay, interview }) panel.Enabled = _canEdit;
        var personalCard = new Card("Personal");
        personalCard.AddTop(personal);
        if (_isAdmin) AddPhoto(personalCard, personal);
        var jobCard = new Card("Job");
        jobCard.AddTop(job);
        var payCard = new Card("Pay") { Visible = _isAdmin };
        payCard.AddTop(pay);
        var interviewCard = new Card("Interview");
        interviewCard.AddTop(interview);
        var stats = new EqualColumns { Visible = _isAdmin };
        stats.Controls.AddRange([_salaryCard, _advanceCard, _absentCard]);
        var content = new StackPanel();
        content.AddTop(stats, personalCard, jobCard, payCard, interviewCard);
        if (_isAdmin) content.AddTop(DocumentsCard());
        _details.Controls.Add(content);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_details);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width / 4, 280, 400));
        Controls.Add(split);

        _commands = _isAdmin
            ?
            [
                new("Staff",
                    new ScreenCommand("Salary sheet", Icons.Money, () => Ctx.OpenScreen("salary")),
                    new ScreenCommand("Advances", Icons.Receipt, () => Ctx.OpenScreen("advances")),
                    new ScreenCommand("Staff attendance", Icons.Check, () => Ctx.OpenScreen("staffattendance"))),
                new("Job offer letter",
                    new ScreenCommand("Print letter", Icons.Print, PrintJobOffer),
                    new ScreenCommand("Letter wording", Icons.Document, () => Ctx.OpenScreen("joboffers"))),
            ]
            : [];
        HandleCreated += (_, _) => Reload();
    }

    /// <summary>Puts the profile picture at the top right of the card, with the fields keeping clear of it.</summary>
    private void AddPhoto(Card card, FieldPanel fields)
    {
        const int width = 156;
        var photoPanel = new FlowLayoutPanel { Size = new Size(width, 220), FlowDirection = FlowDirection.TopDown, BackColor = Color.Transparent };
        photoPanel.Controls.Add(_photo);
        var change = new KryptonButton { Text = "Change photo", Width = _photo.Width, Enabled = _canEdit };
        change.Click += (_, _) => AddDocument(StaffDocumentTypes.ProfilePicture);
        photoPanel.Controls.Add(change);
        fields.Padding = new Padding(0, 0, width + 8, 0);
        fields.MinimumSize = new Size(0, 190);
        card.SetHeaderControl(photoPanel);
    }

    private Card DocumentsCard()
    {
        void Button(string text, Action click, bool edit)
        {
            var b = new KryptonButton { Text = text };
            b.Click += (_, _) => click();
            _docBar.Controls.Add(b);
            if (edit) _docEditButtons.Add(b);
        }
        Button("Add…", () => AddDocument(null), edit: true);
        Button("Open", OpenDocument, edit: false);
        Button("Save a copy…", SaveDocumentCopy, edit: false);
        Button("Delete", DeleteDocument, edit: true);
        _docBar.Controls.Add(_docInfo);
        SizeDocBar();
        Theme.Changed += OnThemeChanged;
        _docGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenDocument(); };
        var card = new Card("Pictures & documents");
        card.AddTop(_docBar, _docGrid);
        return card;
    }

    /// <summary>Sizes the document buttons from their text, so they fit at every font size.</summary>
    private void SizeDocBar()
    {
        var height = Theme.RowHeight + 2;
        foreach (Control c in _docBar.Controls)
            if (c is KryptonButton b)
                b.Size = new Size(TextRenderer.MeasureText(b.Text, Theme.BaseFont).Width + 24, height);
        _docInfo.Font = Theme.BaseFont;
        _docInfo.ForeColor = Theme.Muted;
        _docInfo.Margin = new Padding(8, (height - _docInfo.PreferredSize.Height) / 2 + 3, 3, 3);
        _docBar.Height = height + 10;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => SizeDocBar();

    public override bool SupportsRecords => true;
    public override string NewText => "New staff";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…" : $"Staff: {_list.Count:N0} shown{(HasChanges ? "  ·  unsaved changes" : "")}";

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate(); // pushes the value of the box being typed in
        _bs.EndEdit();
    }

    public override void Reload()
    {
        if (!_loading) _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var keep = _current?.Id;
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        UseWaitCursor = true;
        _loading = true;
        try
        {
            var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var data = await Task.Run(() =>
            {
                var types = db.StaffTypes.AsNoTracking().OrderBy(t => t.Id).ToList();
                var schools = db.Schools.AsNoTracking().OrderBy(s => s.Id).ToList();
                var staff = db.Staff.OrderBy(s => s.Name).ToList();
                var absent = db.StaffAttendance.AsNoTracking().Where(a => a.Date >= month && (a.Status == "A" || a.Status == "L"))
                    .GroupBy(a => a.StaffId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
                var deducted = db.SalaryLines.AsNoTracking().GroupBy(l => l.StaffId)
                    .Select(g => new { g.Key, Sum = g.Sum(l => l.AdvanceDeducted ?? 0) }).ToDictionary(x => x.Key, x => x.Sum);
                var advanced = db.StaffAdvances.AsNoTracking().GroupBy(a => a.StaffId)
                    .Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) }).ToDictionary(x => x.Key, x => x.Sum);
                return (types, schools, staff, absent, deducted, advanced);
            });
            if (!ReferenceEquals(db, _db) || IsDisposed) return;
            Inputs.SetItems(_type, new (int?, string)[] { (null, "") }.Concat(data.types.Select(t => ((int?)t.Id, t.Name?.Trim() ?? ""))));
            Inputs.SetItems(_campus, new (int?, string)[] { (null, "") }.Concat(data.schools.Select(s => ((int?)s.Id, s.Name?.Trim() ?? ""))));
            _absent = data.absent;
            _owed = data.advanced.ToDictionary(a => a.Key, a => a.Value - data.deducted.GetValueOrDefault(a.Key));
            var posts = data.types.ToDictionary(t => t.Id, t => t.Name?.Trim() ?? "");
            _list = new FilterBindingList<Row>(data.staff.Select(s => new Row
            {
                Member = s, Post = s.TypeId is int t && posts.TryGetValue(t, out var p) ? p : s.Designation?.Trim() ?? "",
            }).ToList());
            ApplyFilters();
            _staff.Bind(_list);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load staff. Has sql/staff_payroll_install.sql been run on this database?", ex);
        }
        finally
        {
            _loading = false;
            UseWaitCursor = false;
        }
        if (keep is int id) Select(id);
        ShowCurrent();
        RaiseStatusChanged();
    }

    private void ApplyFilters()
    {
        if (_list is null) return;
        var term = _search.Text.Trim();
        var show = _show.SelectedIndex;
        _list.SetSearch(r => (show == 2 || (show == 0) == (r.Member.LeftOn is null))
                             && (term.Length == 0 || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                 || r.Post.Contains(term, StringComparison.CurrentCultureIgnoreCase) || (r.Member.CellNo ?? "").Contains(term)));
        RaiseStatusChanged();
    }

    private void Select(int id)
    {
        foreach (DataGridViewRow row in _staff.Rows)
            if (row.DataBoundItem is Row r && r.Id == id)
            {
                _staff.CurrentCell = row.Cells[0];
                return;
            }
    }

    private void ShowCurrent()
    {
        if (_staff.CurrentRow?.DataBoundItem is not Row { } row)
        {
            if (_current is { Id: 0 }) return; // a new record is on show
            Show(null);
            return;
        }
        Show(row.Member);
    }

    private void Show(StaffMember? member)
    {
        if (_current is not null) CommitPendingEdits();
        _current = member;
        _bs.DataSource = member ?? (object)typeof(StaffMember);
        _details.Visible = member is not null;
        if (member is null) return;
        _salaryCard.SetValue(member.CurrentSalary is int s ? $"Rs {s:N0}" : "Not set");
        var owed = _owed.GetValueOrDefault(member.Id);
        _advanceCard.SetValue(owed > 0 ? $"Rs {owed:N0}" : "Nothing", owed > 0 ? Theme.Warning : null);
        _absentCard.SetValue($"{_absent.GetValueOrDefault(member.Id)}");
        if (_isAdmin) ShowDocuments(member);
        RaiseStatusChanged();
    }

    // ----- Job offer letter -----

    /// <summary>Prints the letter for this person with the wording of their post, else the general letter, else the school's sample.</summary>
    private void PrintJobOffer()
    {
        CommitPendingEdits();
        if (_current is not { } member || _db is null) return;
        try
        {
            var saved = _db.JobOfferTemplates.AsNoTracking().Where(t => t.StaffTypeId == member.TypeId || t.StaffTypeId == null).ToList();
            var text = saved.FirstOrDefault(t => t.StaffTypeId == member.TypeId && member.TypeId != null)
                       ?? saved.FirstOrDefault(t => t.StaffTypeId == null) ?? Printing.JobOffers.Sample();
            var post = _db.StaffTypes.AsNoTracking().FirstOrDefault(t => t.Id == member.TypeId)?.Name?.Trim();
            var details = Printing.JobOfferDetails.From(member, post, DateTime.Today);
            var school = SchoolDetails();
            Shell.PrintPreviewForm.Show(this, () => Printing.JobOffers.Create(text, details, school), $"Job offer letter · {member.Name}", $"Job offer letter {member.Name}");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not load the letter. Has sql/job_offer_install.sql been run on this database?", ex);
        }
    }

    // ----- Pictures & documents -----

    private void ShowDocuments(StaffMember member)
    {
        _documents = [];
        var saved = member.Id != 0;
        if (saved)
        {
            try
            {
                _documents = _store.List(member.Id);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                Program.ShowError("Could not load the pictures and documents. Has sql/staff_documents_install.sql been run on this database?", ex);
            }
        }
        _docGrid.ResetColumns();
        _docGrid.AddText(nameof(StaffDocument.TypeText), "Type", 150);
        _docGrid.AddText(nameof(StaffDocument.FileName), "File name", 260, fill: true);
        _docGrid.AddText(nameof(StaffDocument.FileSizeKB), "Size (KB)", 75);
        _docGrid.AddText(nameof(StaffDocument.Notes), "Notes", 180);
        _docGrid.AddText(nameof(StaffDocument.CreatedOn), "Added on", 95, format: "dd/MM/yyyy");
        _docGrid.AddText(nameof(StaffDocument.CreatedBy), "Added by", 90);
        _docGrid.Bind(new FilterBindingList<StaffDocument>(_documents));

        foreach (var b in _docEditButtons) b.Enabled = _canEdit && saved;
        var count = _documents.Count(d => d.DocType != StaffDocumentTypes.ProfilePicture);
        _docInfo.Text = !saved ? "Save the new staff member first, then add pictures and documents."
            : $"{count} of {StaffDocumentTypes.MaxDocuments} documents   ·   Saved in {_store.Folder}";
        ShowPhoto(_documents.FirstOrDefault(d => d.DocType == StaffDocumentTypes.ProfilePicture)?.FilePath);
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

    private void AddDocument(string? type)
    {
        if (_current is not { Id: not 0 } member || !_canEdit)
        {
            if (_current is { Id: 0 }) Inform("Save the new staff member first, then add pictures and documents.");
            return;
        }
        if (_db?.Entry(member).Property(s => s.Name).IsModified == true)
        {
            Inform("Save the name change first; the files are named after the staff member.");
            return;
        }

        using var form = new Shell.AddDocumentForm(member.Name ?? $"Employee {member.Id}", StaffDocumentTypes.All, type);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        var existing = _documents.FirstOrDefault(d => d.DocType == form.DocType);
        if (existing is not null && KryptonMessageBox.Show(this, $"{member.Name} already has a {existing.TypeText.ToLower()}. Replace it?",
                "Pictures & documents", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            UseWaitCursor = true;
            _store.Add(member.Id, member.Name, form.DocType, form.FilePath, form.Notes);
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
        if (_isAdmin) ShowDocuments(member);
    }

    private StaffDocument? SelectedDocument => _docGrid.CurrentRow?.DataBoundItem as StaffDocument;

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

    private void RenameDocuments(List<StaffMember> renamed)
    {
        foreach (var member in renamed)
        {
            try
            {
                _store.RenameForStaff(member.Id, member.Name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException or Microsoft.Data.SqlClient.SqlException)
            {
                Program.ShowError($"{member.Name} was saved, but their pictures and documents could not be renamed to match the new name.", ex);
            }
        }
    }

    public override void NewRecord()
    {
        if (!_canEdit || _db is null) { Inform("Only an Admin can add staff."); return; }
        var member = new StaffMember { Flag = true, DateHired = DateTime.Today };
        _db.Staff.Add(member);
        Show(member);
    }

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        _bs.EndEdit();
        if (_db is null || !_canEdit) return true;
        if (_current is not null && string.IsNullOrWhiteSpace(_current.Name)) { Inform("Type the name first."); return false; }
        var renamed = _db.ChangeTracker.Entries<StaffMember>()
            .Where(e => e.State == EntityState.Modified && e.Property(s => s.Name).IsModified).Select(e => e.Entity).ToList();
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException ex) { Program.ShowError("Someone else changed this record. Click Refresh and try again.", ex); return false; }
        catch (DbUpdateException ex) { Program.ShowError("Could not save.", ex); return false; }
        RenameDocuments(renamed);
        Reload();
        return true;
    }

    /// <summary>Removes a record added by mistake; anyone with salaries or advances stays, with a date in Left on.</summary>
    public override void DeleteRecord()
    {
        if (_current is null || _db is null) return;
        if (!_canEdit) { Inform("Only an Admin can delete staff."); return; }
        if (_db.SalaryLines.Any(l => l.StaffId == _current.Id) || _db.StaffAdvances.Any(a => a.StaffId == _current.Id)
            || _db.StaffAttendance.Any(a => a.StaffId == _current.Id) || _db.StaffDocuments.Any(d => d.StaffId == _current.Id))
        {
            Inform($"{_current.Name} has salaries, advances, attendance or documents, so the record stays. Type the date in Left on instead.");
            return;
        }
        if (KryptonMessageBox.Show($"Delete {_current.Name}?", "Delete staff", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.Staff.Remove(_current);
        _current = null;
        SaveChanges();
    }

    public override void FocusSearch() => _search.Focus();

    /// <summary>A staff ID.</summary>
    public override void Navigate(object target)
    {
        if (target is not int id) return;
        _show.SelectedIndex = 2;
        _search.Text = "";
        Select(id);
        ShowCurrent();
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Staff", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

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
