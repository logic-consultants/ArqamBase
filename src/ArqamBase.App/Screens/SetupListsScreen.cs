using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>Small lookup lists (classes, sections, departments…) edited in an Access-style grid. Admin only can change them.</summary>
public sealed class SetupListsScreen : ScreenBase
{
    private readonly KryptonListBox _lists = new() { Dock = DockStyle.Left, Width = 200 };
    private readonly KryptonLabel _title = new() { Dock = DockStyle.Top, LabelStyle = LabelStyle.TitlePanel, AutoSize = true };
    private readonly KryptonLabel _hint = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
    private readonly KryptonTextBox _search = new() { Width = 260, CueHint = { CueHintText = "Search all columns" } };
    private readonly KryptonButton _save = new() { Text = "Save", Width = 90 };
    private readonly KryptonButton _undo = new() { Text = "Undo changes", Width = 110 };
    private readonly FlowLayoutPanel _actions = new() { AutoSize = true, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(16, 0, 0, 0) };
    private readonly AccessGrid _grid = new();
    private readonly List<ListEditor> _editors;
    private readonly bool _canEdit;
    private ArqamDbContext? _db;
    private ListEditor? _current;

    public SetupListsScreen(ScreenContext ctx, string? startList = null) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Setup);
        _editors = ListEditor.All(ctx.Session, ctx.Config);

        foreach (var e in _editors)
            _lists.Items.Add(e.Title);
        _lists.SelectedIndexChanged += (_, _) => SwitchTo(_editors[_lists.SelectedIndex]);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _undo, _save, _actions]);
        _search.TextChanged += (_, _) => ApplySearch();
        _save.Click += (_, _) => SaveChanges();
        _undo.Click += (_, _) => Reload();
        _save.Visible = _undo.Visible = _canEdit;

        _grid.ReadOnly = !_canEdit;
        _grid.CurrentCellChanged += (_, _) => RaiseStatusChanged();
        _grid.FilterChanged += (_, _) => RaiseStatusChanged();
        _grid.CellValueChanged += (_, _) => RaiseStatusChanged();
        _grid.UserDeletingRow += (_, e) =>
        {
            if (!ConfirmDelete()) e.Cancel = true;
        };

        var right = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        right.Controls.Add(_grid);
        right.Controls.Add(bar);
        right.Controls.Add(_hint);
        right.Controls.Add(_title);

        Controls.Add(right);
        Controls.Add(_lists);

        var start = startList is null ? 0 : Math.Max(0, _editors.FindIndex(e => e.Title == startList));
        HandleCreated += (_, _) => _lists.SelectedIndex = start;
    }

    public override bool SupportsRecords => true;
    public override bool HasChanges => _db is not null && (_db.ChangeTracker.HasChanges() || NewRows().Any());

    private IEnumerable<object> NewRows() =>
        _db is not null && _grid.DataSource is IFilterableList list
            ? list.AllObjects.Where(o => _db.Entry(o).State == EntityState.Detached)
            : [];
    public override string StatusText => _current is null ? "" : $"{_current.Title}: {_grid.RecordText()}{(HasChanges ? "  ·  unsaved changes" : "")}";

    private void SwitchTo(ListEditor editor)
    {
        if (editor == _current) return;
        if (_current is not null && !ConfirmLeave())
        {
            _lists.SelectedIndex = _editors.IndexOf(_current);
            return;
        }
        _current = editor;
        _title.Text = editor.Title;
        _hint.Text = _canEdit ? editor.Hint : "Only an Admin can change these lists.";
        _search.Text = "";
        _actions.Controls.Clear();
        if (_canEdit)
            foreach (var (text, run) in editor.Actions)
            {
                var b = new KryptonButton { Text = text, AutoSize = true, MinimumSize = new Size(90, 0) };
                b.Click += (_, _) => { _grid.EndEdit(); run(_grid, this); _grid.Refresh(); RaiseStatusChanged(); };
                _actions.Controls.Add(b);
            }
        Reload();
    }

    public override void Reload()
    {
        if (_current is null) return;
        _db?.Dispose();
        _db = Ctx.Config.CreateContext(Ctx.Session);
        try
        {
            _grid.ResetColumns();
            _current.Load(_db, _grid);
            _grid.AllowUserToAddRows = _canEdit && _current.AllowAdd;
            _grid.AllowUserToDeleteRows = _canEdit && _current.AllowDelete;
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError($"Could not load {_current.Title}.", ex);
        }
        RaiseStatusChanged();
    }

    public override bool SaveChanges()
    {
        if (_db is null || !_canEdit) return true;
        _grid.EndEdit();
        if (_current?.Validate(_grid) is { } problem)
        {
            KryptonMessageBox.Show(problem, _current.Title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            return false;
        }
        try
        {
            foreach (var row in NewRows().ToList())
                _db.Add(row);
            var n = _db.SaveChanges();
            RaiseStatusChanged();
            Reload();
            return n >= 0;
        }
        catch (DbUpdateConcurrencyException)
        {
            KryptonMessageBox.Show("Someone else changed these rows while you were editing. Your changes were not saved; the list will reload.",
                _current?.Title ?? "", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            Reload();
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save. Check for duplicate or empty values.", ex);
            return false;
        }
    }

    public override void NewRecord()
    {
        if (!_grid.AllowUserToAddRows) return;
        _grid.Focus();
        var newRow = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.IsNewRow);
        var firstEditable = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => !c.ReadOnly && c.Visible);
        if (newRow is not null && firstEditable is not null)
            _grid.CurrentCell = newRow.Cells[firstEditable.Index];
    }

    public override void DeleteRecord()
    {
        if (!_grid.AllowUserToDeleteRows || _grid.CurrentRow is not { IsNewRow: false } row) return;
        if (ConfirmDelete())
            _grid.Rows.Remove(row);
        RaiseStatusChanged();
    }

    private bool ConfirmDelete() =>
        KryptonMessageBox.Show("Delete this row? It is removed when you press Save.", _current?.Title ?? "",
            KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) == DialogResult.Yes;

    public override void SortAscending() => _grid.SortSelectedColumn(ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(ListSortDirection.Descending);
    public override void ClearFilters() { _search.Text = ""; _grid.ClearAllFilters(); }
    public override void FocusSearch() => _search.Focus();

    /// <summary>Simple search box: shows rows where any visible column contains the text.</summary>
    private void ApplySearch()
    {
        var term = _search.Text.Trim();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var match = term.Length == 0 || row.Cells.Cast<DataGridViewCell>()
                .Any(c => c.FormattedValue?.ToString()?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true);
            if (_grid.CurrentRow == row && !match) _grid.CurrentCell = null;
            row.Visible = match;
        }
        RaiseStatusChanged();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>How one setup list loads, shows and checks its rows.</summary>
public abstract class ListEditor
{
    public abstract string Title { get; }
    public virtual string Hint => "Edit in the grid, then press Save. Click a heading to sort; click its arrow to filter.";
    public virtual bool AllowAdd => true;
    public virtual bool AllowDelete => true;
    public abstract void Load(ArqamDbContext db, AccessGrid grid);

    /// <summary>Returns a message when the rows cannot be saved as they are.</summary>
    public virtual string? Validate(AccessGrid grid) => null;

    /// <summary>Extra buttons shown beside Save while this list is open.</summary>
    public virtual IReadOnlyList<(string Text, Action<AccessGrid, IWin32Window> Run)> Actions => [];

    public static List<ListEditor> All(UserSession session, AppConfig config)
    {
        List<ListEditor> lists = [new ClassesEditor(), new SectionsEditor(), new DepartmentsEditor(), new SchoolsEditor(), new ConversationTypesEditor()];
        if (session.IsAdmin)
            lists.Add(new UsersEditor(session, config));
        lists.AddRange([new AttendanceStatusEditor(), new HolidaysEditor()]);
        return lists;
    }

    protected static FilterBindingList<T> Bind<T>(ArqamDbContext db, AccessGrid grid, IEnumerable<T> rows) where T : class
    {
        // New rows are attached to the context only when saving (see SetupListsScreen.SaveChanges),
        // so half-typed rows with the same default key do not clash.
        var list = new FilterBindingList<T>(rows);
        list.ItemRemoved += item =>
        {
            if (db.Entry(item).State != EntityState.Detached)
                db.Remove(item);
        };
        grid.Bind(list);
        return list;
    }

    protected static IReadOnlyList<T> Rows<T>(AccessGrid grid) where T : class =>
        (grid.DataSource as FilterBindingList<T>)?.AllItems ?? [];
}

internal sealed class ClassesEditor : ListEditor
{
    public override string Title => "Classes";
    public override string Hint => "Sort name controls the order classes appear in (for example \"04 One\"). " + base.Hint;

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        var depts = db.Departments.OrderBy(d => d.Id).Select(d => new { d.Id, d.Name }).ToList();
        grid.AddText(nameof(SchoolClass.Id), "ID", 60, readOnly: true);
        grid.AddText(nameof(SchoolClass.Name), "Class", 140);
        grid.AddCombo(nameof(SchoolClass.DepartmentId), "Department", depts.Select(d => ((int?)d.Id, d.Name ?? "")), 180);
        grid.AddText(nameof(SchoolClass.SortName), "Sort name", 160, fill: true);
        var list = Bind(db, grid, db.Classes.OrderBy(c => c.SortName).ToList());
        list.Sort(nameof(SchoolClass.SortName), ListSortDirection.Ascending);
    }

    public override string? Validate(AccessGrid grid) =>
        Rows<SchoolClass>(grid).Any(c => string.IsNullOrWhiteSpace(c.Name)) ? "Every class needs a name." : null;
}

internal sealed class SectionsEditor : ListEditor
{
    public override string Title => "Sections";

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        grid.AddText(nameof(Section.Id), "ID", 60, readOnly: true);
        grid.AddText(nameof(Section.Name), "Section", 200, fill: true);
        Bind(db, grid, db.Sections.OrderBy(s => s.Name).ToList());
    }
}

internal sealed class DepartmentsEditor : ListEditor
{
    public override string Title => "Departments";
    public override string Hint => "School decides where a department belongs. \"Old campus text\" is from Access and is not used by the new app. " + base.Hint;

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        var schools = db.Schools.OrderBy(s => s.Id).Select(s => new { s.Id, s.Name }).ToList();
        grid.AddText(nameof(Department.Id), "No.", 60, readOnly: true);
        grid.AddText(nameof(Department.Name), "Department", 160);
        grid.AddCombo(nameof(Department.SchoolId), "School", schools.Select(s => ((int?)s.Id, s.Name ?? "")), 280);
        grid.AddText(nameof(Department.AccountNumber), "Bank account", 190);
        grid.AddText(nameof(Department.CampusDivision), "Old campus text", 220, fill: true);
        Bind(db, grid, db.Departments.OrderBy(d => d.Id).ToList());
    }
}

internal sealed class SchoolsEditor : ListEditor
{
    public override string Title => "Schools";
    public override bool AllowDelete => false;

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        grid.AddText(nameof(School.Id), "ID", 60, readOnly: true);
        grid.AddText(nameof(School.Code), "Code", 110);
        grid.AddText(nameof(School.Name), "School name", 300, fill: true);
        Bind(db, grid, db.Schools.OrderBy(s => s.Id).ToList());
    }
}

internal sealed class ConversationTypesEditor : ListEditor
{
    public override string Title => "Conversation types";

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        grid.AddText(nameof(ConversationType.Id), "ID", 60, readOnly: true);
        grid.AddText(nameof(ConversationType.Name), "Conversation type", 300, fill: true);
        Bind(db, grid, db.ConversationTypes.OrderBy(c => c.Name).ToList());
    }
}

internal sealed class UsersEditor(UserSession session, AppConfig config) : ListEditor
{
    public override string Title => "Users & roles";
    public override bool AllowDelete => false;

    private ArqamDbContext? _db;

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        _db = db;
        grid.AddText(nameof(AppUser.UserName), "User name", 160);
        grid.AddText(nameof(AppUser.FullName), "Full name", 200);
        grid.AddCombo(nameof(AppUser.RoleName), "Role", Enum.GetValues<AppRole>().Select(r => (r.ToString(), UserSession.RoleLabel(r))), 130);
        grid.AddCheck(nameof(AppUser.IsActive), "Active", 60);
        grid.AddText(nameof(AppUser.PasswordState), "Password", 100, readOnly: true);
        grid.AddCheck(nameof(AppUser.IsLocked), "Locked", 60).ReadOnly = true;
        grid.AddText(nameof(AppUser.LastSignIn), "Last sign-in", 140, readOnly: true, format: "dd/MM/yyyy HH:mm");
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LooksAfter", HeaderText = "Marks attendance for", Width = 260, ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable,
        });
        Bind(db, grid, db.AppUsers.OrderBy(u => u.UserName).ToList());
        LoadScopes(db);
        grid.CellFormatting -= ShowScope;
        grid.CellFormatting += ShowScope;

        // A user name is the key: it can be typed for a new user but not changed afterwards.
        grid.CanEditCell = (row, col) =>
            grid.Columns[col].DataPropertyName != nameof(AppUser.UserName)
            || row >= grid.Rows.Count || grid.Rows[row].IsNewRow
            || grid.Rows[row].DataBoundItem is not AppUser u || db.Entry(u).State is EntityState.Added or EntityState.Detached;
    }

    public override string Hint =>
        "Add a user, then press Set password to give them a temporary password; they choose their own at first sign-in. " +
        "Switch users off instead of deleting them.";

    public override IReadOnlyList<(string Text, Action<AccessGrid, IWin32Window> Run)> Actions =>
    [
        ("Set password…", SetPassword),
        ("Unlock", (grid, _) => { if (Current(grid) is { } u) { u.FailedAttempts = 0; u.LockedUntil = null; } }),
        ("Assign classes…", Assign),
    ];

    private static AppUser? Current(AccessGrid grid) => grid.CurrentRow?.DataBoundItem as AppUser;

    // ----- What coordinators and teachers in charge look after -----

    private List<UserScope> _scopes = [];
    private Dictionary<int, string> _departments = [];
    private List<(int ClassId, int SectionId, string Text)> _classSections = [];

    private void LoadScopes(ArqamDbContext db)
    {
        var schools = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => s.Name?.Trim() ?? "");
        _departments = db.Departments.AsNoTracking().OrderBy(d => d.SchoolId).ThenBy(d => d.Name).AsEnumerable()
            .ToDictionary(d => d.Id, d => $"{(d.SchoolId is int s ? schools.GetValueOrDefault(s) : "")} · {d.Name?.Trim()}".TrimStart(' ', '·'));
        var classes = db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).ToList();
        var sections = ClassSections.Read(db, onRollOnly: true);
        _classSections = classes.SelectMany(c => sections.Where(s => s.ClassId == c.Id)
                .Select(s => (c.Id, s.Id, $"{c.Name?.Trim()} · {s.Name.Trim()}"
                    + (c.DepartmentId is int d && _departments.TryGetValue(d, out var dn) ? $"   ({dn})" : ""))))
            .ToList();
        try { _scopes = db.UserScopes.AsNoTracking().ToList(); }
        catch (Microsoft.Data.SqlClient.SqlException) { _scopes = []; } // sql/attendance_roles_install.sql not run yet
    }

    private string ScopeText(AppUser user)
    {
        if (user.Role is not (AppRole.Coordinator or AppRole.TeacherInCharge)) return user.Role == AppRole.Admin || user.Role == AppRole.Admissions ? "All classes" : "";
        var mine = _scopes.Where(s => string.Equals(s.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)).ToList();
        var parts = user.Role == AppRole.Coordinator
            ? mine.Where(s => s.DepartmentId is not null).Select(s => _departments.GetValueOrDefault(s.DepartmentId!.Value, "?"))
            : mine.Where(s => s.ClassId is not null).Select(s => _classSections.FirstOrDefault(c => c.ClassId == s.ClassId && c.SectionId == s.SectionId).Text?.Split("   (")[0] ?? "?");
        var text = string.Join(", ", parts);
        return text.Length > 0 ? text : "Not assigned: press Assign classes";
    }

    private void ShowScope(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not AccessGrid grid || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "LooksAfter" || grid.Rows[e.RowIndex].DataBoundItem is not AppUser u) return;
        e.Value = ScopeText(u);
        e.FormattingApplied = true;
    }

    /// <summary>Ticks the departments of a coordinator or the classes of a teacher in charge; saved straight away.</summary>
    private void Assign(AccessGrid grid, IWin32Window owner)
    {
        void Say(string text) => KryptonMessageBox.Show(owner, text, "Assign classes", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
        if (Current(grid) is not { } user || string.IsNullOrWhiteSpace(user.UserName)) { Say("Select a user first."); return; }
        if (user.Role is not (AppRole.Coordinator or AppRole.TeacherInCharge))
        {
            Say("Only coordinators and teachers in charge are assigned. Admins and the Admissions office mark every class; other roles do not mark attendance.");
            return;
        }
        if (_db is null || _db.Entry(user).State is EntityState.Added or EntityState.Detached) { Say($"Save {user.UserName} first, then assign."); return; }
        var coordinator = user.Role == AppRole.Coordinator;
        var mine = _scopes.Where(s => string.Equals(s.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)).ToList();
        var items = coordinator
            ? _departments.Select(d => (d.Key, (int?)null, d.Value)).ToList()
            : _classSections.Select(c => (c.ClassId, (int?)c.SectionId, c.Text)).ToList();
        var chosen = coordinator
            ? mine.Where(s => s.DepartmentId is not null).Select(s => (s.DepartmentId!.Value, (int?)null))
            : mine.Where(s => s.ClassId is not null).Select(s => (s.ClassId!.Value, s.SectionId));
        using var form = new Shell.AssignScopeForm(user.UserName, coordinator ? "departments" : "classes and sections", items, chosen);
        if (form.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            using var db = config.CreateContext(session);
            db.UserScopes.Where(s => s.UserName == user.UserName).ExecuteDelete();
            db.UserScopes.AddRange(form.Chosen.Select(c => coordinator
                ? new UserScope { UserName = user.UserName, DepartmentId = c.Id }
                : new UserScope { UserName = user.UserName, ClassId = c.Id, SectionId = c.SectionId }));
            db.SaveChanges();
            _scopes = db.UserScopes.AsNoTracking().ToList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or DbUpdateException)
        {
            Program.ShowError("Could not save the assignment. Has sql/attendance_roles_install.sql been run on this database?", ex);
            return;
        }
        grid.Invalidate();
        Say($"Saved. {user.UserName} sees it at their next sign-in.");
    }

    private void SetPassword(AccessGrid grid, IWin32Window owner)
    {
        if (Current(grid) is not { } user || string.IsNullOrWhiteSpace(user.UserName))
        {
            KryptonMessageBox.Show(owner, "Select a user (with a user name) first.", "Set password", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        using var form = new Shell.SetPasswordForm(user.UserName);
        if (form.ShowDialog(owner) != DialogResult.OK) return;
        user.SetPassword(form.Password, mustChange: true);
        if (_db is not null && _db.Entry(user).State == EntityState.Unchanged)
            _db.Entry(user).State = EntityState.Modified;
        KryptonMessageBox.Show(owner, $"Temporary password set for {user.UserName}. Press Save to keep it.", "Set password",
            KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
    }

    public override string? Validate(AccessGrid grid)
    {
        var list = Rows<AppUser>(grid);
        foreach (var u in list)
            u.UserName = u.UserName?.Trim() ?? "";
        if (list.Any(u => u.UserName.Length == 0))
            return "Every user needs a user name.";
        if (list.GroupBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return "The same user name is listed twice.";
        if (list.FirstOrDefault(u => !u.HasPassword) is { } noPassword)
            return $"Set a password for {noPassword.UserName}: select the row, then press Set password.";
        var me = list.FirstOrDefault(u => string.Equals(u.UserName, session.UserName, StringComparison.OrdinalIgnoreCase));
        if (me is null || !me.IsActive || me.Role != AppRole.Admin)
            return "You cannot remove your own Admin role or switch yourself off. Ask another Admin to do it.";
        return null;
    }
}

internal sealed class AttendanceStatusEditor : ListEditor
{
    public override string Title => "Attendance statuses";
    public override string Hint => "The codes P, A, L and T are fixed. You can rename them and choose whether they count as present.";
    public override bool AllowAdd => false;
    public override bool AllowDelete => false;

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        grid.AddText(nameof(AttendanceStatus.Code), "Code", 60, readOnly: true);
        grid.AddText(nameof(AttendanceStatus.Name), "Name", 180);
        grid.AddCheck(nameof(AttendanceStatus.CountsAsPresent), "Counts as present", 130);
        grid.AddText(nameof(AttendanceStatus.SortOrder), "Order", 70, fill: true);
        Bind(db, grid, db.AttendanceStatuses.OrderBy(s => s.SortOrder).ToList());
    }

    public override string? Validate(AccessGrid grid) =>
        Rows<AttendanceStatus>(grid).Any(s => string.IsNullOrWhiteSpace(s.Name)) ? "Every status needs a name." : null;
}

internal sealed class HolidaysEditor : ListEditor
{
    public override string Title => "Holidays";
    public override string Hint => "Days the school is closed. Attendance is not taken on these dates. Type dates as dd/mm/yyyy.";

    public override void Load(ArqamDbContext db, AccessGrid grid)
    {
        grid.AddText(nameof(CalendarDay.Date), "Date", 120, format: "dd/MM/yyyy");
        grid.AddText(nameof(CalendarDay.DayName), "Day", 110, readOnly: true);
        grid.AddCheck(nameof(CalendarDay.IsHoliday), "Closed", 70);
        grid.AddText(nameof(CalendarDay.Reason), "Reason", 300, fill: true);
        Bind(db, grid, db.Calendar.OrderByDescending(c => c.Date).ToList());

        // The date is the key: it can be typed for a new row; to move a saved holiday, delete it and add the new date.
        grid.CanEditCell = (row, col) =>
            grid.Columns[col].DataPropertyName != nameof(CalendarDay.Date)
            || row >= grid.Rows.Count || grid.Rows[row].IsNewRow
            || grid.Rows[row].DataBoundItem is not CalendarDay d || db.Entry(d).State is EntityState.Added or EntityState.Detached;
    }

    public override string? Validate(AccessGrid grid) =>
        Rows<CalendarDay>(grid).GroupBy(c => c.Date.Date).Any(g => g.Count() > 1) ? "The same date is listed twice." : null;
}
