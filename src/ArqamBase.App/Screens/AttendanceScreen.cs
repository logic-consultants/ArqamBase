using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Marks a class and section's attendance for a date (replaces frmClassAttendance, tblAttendanceMaster):
/// everyone starts present; press A, L or T on a student (or the selected students) to mark absent, leave or late, then Save.
/// One register per class, section and date. A locked register can be changed only by an Admin.
/// </summary>
public sealed class AttendanceScreen : ScreenBase
{
    public sealed class Row : INotifyPropertyChanged
    {
        private string _status = "P";
        private string? _remarks;
        public int Sr { get; init; }
        public int Cc { get; init; }
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public string? Mobile { get; init; }
        internal string? WhatsApp { get; init; }
        public bool OnRoll { get; init; } = true;
        /// <summary>Absences this month before this date.</summary>
        public int AbsentThisMonth { get; init; }

        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
        }

        public string? Remarks
        {
            get => _remarks;
            set { if (_remarks == value) return; _remarks = value; PropertyChanged?.Invoke(this, new(nameof(Remarks))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private static readonly (string Code, string Name)[] Statuses = [("P", "Present"), ("A", "Absent"), ("L", "Leave"), ("T", "Late")];

    private readonly KryptonDateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd dd/MM/yyyy" };
    private readonly ComboBox _class = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _section = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonLabel _banner = new()
    {
        Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), StateCommon = { ShortText = { Color1 = Theme.Muted } }
    };
    private readonly StatCard _onRoll = new("Students"), _present = new("Present"), _absent = new("Absent"), _leave = new("Leave"), _late = new("Late");
    private readonly AccessGrid _grid = new();
    private readonly bool _canEdit, _isAdmin;
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private List<(int Id, string Name, string Sort)> _classes = [];
    private List<(int Id, int? ClassId, string Name)> _sections = [];
    private Dictionary<DateTime, string?> _holidays = [];
    private FilterBindingList<Row>? _list;
    private AttendanceRegister? _register;
    private (DateTime Date, int ClassId, int SectionId)? _shown;
    private bool _filling, _loading, _dirty;

    public AttendanceScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Attendance);
        _isAdmin = ctx.Session.IsAdmin;
        _date.Value = DateTime.Today;
        _date.MaxDate = DateTime.Today;

        var fields = new FieldPanel(4);
        fields.Add("Date", _date);
        fields.Add("Class", _class);
        fields.Add("Section", _section);
        var choose = new Card();
        choose.AddTop(fields);

        var stats = new EqualColumns { Padding = new Padding(0, Card.Gap, 0, 0) };
        stats.Controls.AddRange([_onRoll, _present, _absent, _leave, _late]);

        BuildGrid();
        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0),
            Text = _canEdit ? "Everyone starts present. Press A (absent), L (leave), T (late) or P on a student, or on several selected students; double-click the mark to change it. Then Save."
                            : "You can view attendance. An Admin, the Admissions office, coordinators and teachers in charge can mark it.",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };

        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, Card.Gap, 0, 0) };
        body.Controls.Add(_grid);
        body.Controls.Add(stats);
        body.Controls.Add(_banner);
        Controls.Add(body);
        Controls.Add(hint);
        Controls.Add(choose);

        _date.ValueChanged += (_, _) => { if (!_filling) ShowRegister(); };
        _class.SelectedIndexChanged += (_, _) => { if (!_filling) { FillSections(); ShowRegister(); } };
        _section.SelectedIndexChanged += (_, _) => { if (!_filling) ShowRegister(); };

        _commands =
        [
            new("Mark",
                new ScreenCommand("Present", Icons.Check, () => Mark("P")),
                new ScreenCommand("Absent", Icons.Cross, () => Mark("A")),
                new ScreenCommand("Leave", Icons.Calendar, () => Mark("L")),
                new ScreenCommand("Late", Icons.Clock, () => Mark("T"))),
            new("Date",
                new ScreenCommand("Previous day", Icons.Previous, () => MoveDay(-1)),
                new ScreenCommand("Next day", Icons.Next, () => MoveDay(1)),
                new ScreenCommand("Today", Icons.Calendar, () => _date.Value = DateTime.Today)),
            .. _isAdmin ? new[] { new ScreenCommandGroup("Register", new ScreenCommand("Lock / unlock", Icons.Lock, ToggleLock)) } : [],
        ];
        LoadLists();
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool SupportsRecords => true;
    public override string NewText => "All present";
    public override bool HasChanges => _dirty;

    public override string StatusText => _list is null ? "" :
        $"{_list.Count:N0} students · {Count("P") + Count("T"):N0} present · {Count("A"):N0} absent · {Count("L"):N0} leave · {Count("T"):N0} late";

    private int Count(string status) => _list?.AllItems.Count(r => r.Status == status) ?? 0;

    private bool Editable => _canEdit && (_register is not { IsLocked: true } || _isAdmin) && Ctx.Session.CanChangeDay(_date.Value);

    public override void NewRecord()
    {
        if (!Editable || _list is null) return;
        foreach (var r in _list.AllItems) r.Status = "P";
        Changed();
    }

    public override void Reload()
    {
        if (!ConfirmLeave()) return;
        _dirty = false;
        LoadLists();
    }

    /// <summary>Deletes the register (Admin only), so the day can be taken again or was taken by mistake on a holiday.</summary>
    public override void DeleteRecord()
    {
        if (_register is null) { Inform("This day's attendance has not been saved, so there is nothing to delete."); return; }
        if (!_isAdmin) { Inform("Only an Admin can delete a day's attendance."); return; }
        if (KryptonMessageBox.Show($"Delete the attendance of {ClassText} on {_register.Date:dd/MM/yyyy}?", "Delete attendance",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            db.AttendanceRegisters.Where(r => r.Id == _register.Id).ExecuteDelete();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not delete the attendance.", ex); return; }
        _dirty = false;
        _shown = null;
        ShowRegister();
    }

    /// <summary>A class and section, e.g. "5 · Blue" (as the Students screen links here).</summary>
    public override void Navigate(object target)
    {
        if (target is not (int classId, int sectionId)) return;
        var c = _classes.FindIndex(x => x.Id == classId);
        if (c < 0) return;
        _class.SelectedIndex = c;
        var s = _sections.Where(x => x.ClassId == classId).Select(x => x.Id).ToList().IndexOf(sectionId);
        if (s >= 0) _section.SelectedIndex = s;
    }

    public override bool SaveChanges()
    {
        _grid.EndEdit();
        if (!_dirty) return true;
        if (!Editable || _list is null || _shown is not { } key) return false;
        if (HolidayText(key.Date) is { } holiday &&
            KryptonMessageBox.Show($"{key.Date:dddd dd/MM/yyyy} is {holiday}. Save attendance for it anyway?", "Save attendance",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return false;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var register = db.AttendanceRegisters.Include(r => r.Marks)
                .FirstOrDefault(r => r.Date == key.Date && r.ClassId == key.ClassId && r.SectionId == key.SectionId);
            if (register is null)
            {
                register = new AttendanceRegister { Date = key.Date, ClassId = key.ClassId, SectionId = key.SectionId };
                db.AttendanceRegisters.Add(register);
            }
            else if (register.IsLocked && !_isAdmin)
            {
                Inform("This day's attendance was locked meanwhile. Only an Admin can change it.");
                return false;
            }
            register.TakenBy = Ctx.Session.UserName;
            register.TakenOn = DateTime.Now;
            foreach (var row in _list.AllItems)
            {
                var mark = register.Marks.FirstOrDefault(m => m.StudentId == row.Cc);
                if (mark is null) register.Marks.Add(mark = new StudentAttendance { StudentId = row.Cc });
                mark.Status = row.Status;
                mark.Remarks = string.IsNullOrWhiteSpace(row.Remarks) ? null : row.Remarks.Trim();
            }
            db.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save the attendance. Someone may have saved this class at the same time; click Refresh and try again.", ex);
            return false;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not save the attendance.", ex); return false; }
        _dirty = false;
        _shown = null;
        ShowRegister();
        return true;
    }

    // ----- Loading -----

    private void LoadLists()
    {
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            // Coordinators see their departments' classes, teachers in charge their own class and section.
            var classes = db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).ToList();
            var department = classes.ToDictionary(c => c.Id, c => c.DepartmentId);
            _sections = db.Sections.AsNoTracking().OrderBy(s => s.Name).AsEnumerable()
                .Where(s => s.ClassId is int ci && Ctx.Session.Covers(department.GetValueOrDefault(ci), ci, s.Id))
                .Select(s => (s.Id, s.ClassId, s.Name ?? "")).ToList();
            _classes = classes.Where(c => !Ctx.Session.IsScoped || _sections.Any(s => s.ClassId == c.Id))
                .Select(c => (c.Id, c.Name ?? "", c.SortName ?? "")).ToList();
            _holidays = db.Calendar.AsNoTracking().Where(d => d.IsHoliday).ToList().ToDictionary(d => d.Date.Date, d => d.Reason);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the classes.", ex); return; }
        _filling = true;
        var keep = _class.SelectedItem as Inputs.Item<int>;
        _class.DataSource = _classes.Select(c => new Inputs.Item<int>(c.Id, c.Name)).ToList();
        _class.DisplayMember = nameof(Inputs.Item<int>.Text);
        if (keep is not null) _class.SelectedIndex = Math.Max(0, _classes.FindIndex(c => c.Id == keep.Value));
        _filling = false;
        FillSections();
        _shown = null;
        ShowRegister();
    }

    private void FillSections()
    {
        _filling = true;
        var keep = _section.Text;
        var cls = (_class.SelectedItem as Inputs.Item<int>)?.Value;
        var items = _sections.Where(s => s.ClassId == cls).Select(s => new Inputs.Item<int>(s.Id, s.Name)).ToList();
        _section.DataSource = items;
        _section.DisplayMember = nameof(Inputs.Item<int>.Text);
        var i = items.FindIndex(s => s.Text == keep);
        if (items.Count > 0) _section.SelectedIndex = Math.Max(0, i);
        _filling = false;
    }

    private string ClassText => $"class {_class.Text}{(_section.Text.Length > 0 ? $" ({_section.Text})" : "")}";

    /// <summary>Shows the register of the chosen date, class and section; a day not taken yet starts with everyone present.</summary>
    private void ShowRegister()
    {
        if (_loading) return;
        var cls = (_class.SelectedItem as Inputs.Item<int>)?.Value;
        var section = (_section.SelectedItem as Inputs.Item<int>)?.Value;
        if (cls is null || section is null)
        {
            _list = null;
            _grid.DataSource = null;
            _banner.Text = cls is not null ? "This class has no sections. Add one in Setup lists."
                : Ctx.Session.IsScoped ? "No class or department is assigned to you yet. Ask an Admin to assign one in Setup lists › Users & roles." : "";
            UpdateCounts();
            return;
        }
        var key = (_date.Value.Date, cls.Value, section.Value);
        if (_shown == key) return;
        if (_dirty && _shown is not null && !ConfirmLeave())
        {
            // Go back to what is shown.
            _filling = true;
            _date.Value = _shown.Value.Date;
            _class.SelectedIndex = _classes.FindIndex(c => c.Id == _shown.Value.ClassId);
            _filling = false;
            FillSections();
            _filling = true;
            _section.SelectedIndex = _sections.Where(s => s.ClassId == _shown.Value.ClassId).Select(s => s.Id).ToList().IndexOf(_shown.Value.SectionId);
            _filling = false;
            return;
        }
        _loading = true;
        try
        {
            var (date, classId, sectionId) = key;
            var monthStart = new DateTime(date.Year, date.Month, 1);
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            _register = db.AttendanceRegisters.AsNoTracking().Include(r => r.Marks)
                .FirstOrDefault(r => r.Date == date && r.ClassId == classId && r.SectionId == sectionId);
            var marked = _register?.Marks.Select(m => m.StudentId).ToList() ?? [];
            var students = db.Students.AsNoTracking()
                .Where(s => (s.OnRoll && s.ClassId == classId && s.SectionId == sectionId) || marked.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, Father = s.Family!.FatherName, Mobile = s.Family.Mobile, s.WhatsApp, s.OnRoll })
                .OrderBy(s => s.Name).ToList();
            var ids = students.Select(s => s.Id).ToList();
            var absences = (from m in db.StudentAttendance.AsNoTracking()
                            join r in db.AttendanceRegisters.AsNoTracking() on m.RegisterId equals r.Id
                            where ids.Contains(m.StudentId) && m.Status == "A" && r.Date >= monthStart && r.Date < date
                            group m by m.StudentId into g
                            select new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
            var marks = _register?.Marks.ToDictionary(m => m.StudentId) ?? [];
            var sr = 0;
            var rows = students.Select(s => new Row
            {
                Sr = ++sr, Cc = s.Id, Name = s.Name ?? "", FatherName = s.Father ?? "", Mobile = s.Mobile, WhatsApp = s.WhatsApp, OnRoll = s.OnRoll,
                AbsentThisMonth = absences.GetValueOrDefault(s.Id),
                Status = marks.TryGetValue(s.Id, out var m) ? m.Status.Trim().ToUpperInvariant() : "P",
                Remarks = marks.TryGetValue(s.Id, out var m2) ? m2.Remarks : null,
            }).ToList();
            foreach (var r in rows) r.PropertyChanged += (_, _) => Changed();
            _list = new FilterBindingList<Row>(rows);
            _grid.Bind(_list);
            _shown = key;
            _dirty = false;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the attendance.", ex); }
        finally { _loading = false; }
        ShowBanner();
        UpdateCounts();
    }

    private string? HolidayText(DateTime date) =>
        _holidays.TryGetValue(date.Date, out var reason) ? $"a holiday{(string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason.Trim()})")}"
        : date.DayOfWeek == DayOfWeek.Sunday ? "a Sunday" : null;

    private void ShowBanner()
    {
        var parts = new List<string>();
        var warn = false;
        if (HolidayText(_date.Value) is { } holiday) { parts.Add($"{_date.Value:dddd dd/MM/yyyy} is {holiday}."); warn = true; }
        if (_register is null)
            parts.Add(_list?.Count == 0 ? "No students on roll in this class and section." : "Not taken yet: everyone is marked present until you save.");
        else
            parts.Add($"Taken by {_register.TakenBy} on {_register.TakenOn:dd/MM/yyyy HH:mm}.");
        if (_register is { IsLocked: true }) { parts.Add(_isAdmin ? "Locked: only an Admin can change it." : "Locked: it cannot be changed."); warn = true; }
        if (_canEdit && !Ctx.Session.CanChangeDay(_date.Value)) parts.Add("You can mark only today's attendance; earlier days are shown to view.");
        if (_dirty) parts.Add("Not saved.");
        _banner.Text = string.Join("  ", parts);
        _banner.StateCommon.ShortText.Color1 = warn ? Theme.Warning : _dirty ? Theme.Accent : Theme.Muted;
        _grid.ReadOnly = !Editable;
    }

    private void UpdateCounts()
    {
        _onRoll.SetValue($"{_list?.Count ?? 0:N0}");
        _present.SetValue($"{Count("P") + Count("T"):N0}", Theme.Good);
        _absent.SetValue($"{Count("A"):N0}", Count("A") > 0 ? Theme.Error : null);
        _leave.SetValue($"{Count("L"):N0}");
        _late.SetValue($"{Count("T"):N0}", Count("T") > 0 ? Theme.Warning : null);
        RaiseStatusChanged();
    }

    private void Changed()
    {
        if (_loading) return;
        _dirty = true;
        ShowBanner();
        UpdateCounts();
        _grid.InvalidateColumn(_grid.Columns[nameof(Row.Status)]!.Index);
    }

    // ----- Marking -----

    private List<Row> SelectedRows()
    {
        var rows = _grid.SelectedCells.Cast<DataGridViewCell>().Select(c => _grid.Rows[c.RowIndex].DataBoundItem).OfType<Row>().Distinct().ToList();
        if (rows.Count == 0 && _grid.CurrentRow?.DataBoundItem is Row current) rows.Add(current);
        return rows;
    }

    private void Mark(string status)
    {
        if (!Editable) { if (_register is { IsLocked: true }) Inform("This day's attendance is locked. Only an Admin can change it."); return; }
        foreach (var r in SelectedRows()) r.Status = status;
    }

    private void MoveDay(int days)
    {
        var d = _date.Value.Date.AddDays(days);
        if (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(days); // skip Sundays
        if (d <= _date.MaxDate) _date.Value = d;
    }

    private void ToggleLock()
    {
        if (_register is null) { Inform("Save the attendance first."); return; }
        if (!ConfirmLeave()) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var locked = !_register.IsLocked;
            db.AttendanceRegisters.Where(r => r.Id == _register.Id).ExecuteUpdate(s => s.SetProperty(r => r.IsLocked, locked));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not lock the attendance.", ex); return; }
        _shown = null;
        ShowRegister();
    }

    private void BuildGrid()
    {
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.AddText(nameof(Row.Sr), "Sr#", 45, readOnly: true);
        _grid.AddText(nameof(Row.Cc), "CC", 60, readOnly: true);
        _grid.AddText(nameof(Row.Name), "Name", 200, readOnly: true, fill: true);
        _grid.AddText(nameof(Row.FatherName), "Father name", 180, readOnly: true, fill: true);
        var status = _grid.AddCombo(nameof(Row.Status), "Attendance", Statuses.Select(s => (s.Code, s.Name)), 120);
        status.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
        _grid.AddText(nameof(Row.AbsentThisMonth), "Absent this month", 120, readOnly: true).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Remarks), "Remarks", 200, fill: true);
        _grid.AddText(nameof(Row.Mobile), "Mobile", 115, readOnly: true);
        _grid.FormatNumbers(nameof(Row.Mobile), NumberFormat.Phone);
        _grid.CanEditCell = (_, column) => Editable && _grid.Columns[column].DataPropertyName is nameof(Row.Remarks) or nameof(Row.Status);

        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            if (_grid.Columns[e.ColumnIndex].DataPropertyName == nameof(Row.Status))
            {
                e.CellStyle!.ForeColor = row.Status switch { "A" => Theme.Error, "T" => Theme.Warning, "L" => Theme.Accent, _ => Theme.Good };
                e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            }
            else if (!row.OnRoll) e.CellStyle!.ForeColor = Theme.Muted;
        };
        // P / A / L / T mark the selected students (unless typing remarks); double-click the mark to go to the next one.
        _grid.KeyDown += (_, e) =>
        {
            if (_grid.IsCurrentCellInEditMode || _grid.CurrentCell?.OwningColumn?.DataPropertyName == nameof(Row.Remarks)) return;
            var status = e.KeyCode switch { Keys.P => "P", Keys.A => "A", Keys.L => "L", Keys.T => "T", _ => null };
            if (status is null || e.Modifiers != Keys.None) return;
            Mark(status);
            e.Handled = e.SuppressKeyPress = true;
            if (_grid.SelectedCells.Count <= 1 && _grid.CurrentCell is { } cell && cell.RowIndex + 1 < _grid.Rows.Count)
                _grid.CurrentCell = _grid.Rows[cell.RowIndex + 1].Cells[cell.ColumnIndex];
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(Row.Status) || _grid.Rows[e.RowIndex].DataBoundItem is not Row row || !Editable) return;
            var i = Array.FindIndex(Statuses, s => s.Code == row.Status);
            row.Status = Statuses[(i + 1) % Statuses.Length].Code;
        };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !_grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)].Selected)
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        };
        _grid.CellContextMenuStripNeeded += (_, e) => { if (e.RowIndex >= 0) e.ContextMenuStrip = RowMenu(); };
    }

    private ContextMenuStrip? RowMenu()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Row row) return null;
        var menu = new ContextMenuStrip();
        foreach (var (code, name) in Statuses)
            menu.Items.Add($"Mark {name.ToLowerInvariant()}  ({code})", null, (_, _) => Mark(code)).Enabled = Editable;
        menu.Items.Add(new ToolStripSeparator());
        var digits = NumberFormat.Digits(row.WhatsApp is { Length: > 0 } wa ? wa : row.Mobile);
        var phone = digits.Length >= 10 ? "92" + digits[^10..] : null;
        menu.Items.Add(phone is null ? "Send absence WhatsApp (no mobile number)" : $"Send absence WhatsApp to {NumberFormat.Phone(row.WhatsApp ?? row.Mobile)}",
            Icons.Small(Icons.Chat), (_, _) => SendAbsence(row, phone!)).Enabled = phone is not null && row.Status is "A" or "T";
        if (Ctx.Session.CanView(AppArea.Students))
            menu.Items.Add($"Open student ({row.Cc} {row.Name})", Icons.Small(Icons.Person), (_, _) => Ctx.OpenScreenAt("students", row.Cc));
        return menu;
    }

    /// <summary>Opens WhatsApp with the absence (or late) note written; the user presses Send there.</summary>
    private void SendAbsence(Row row, string phone)
    {
        var what = row.Status == "T" ? "came late to school" : "is absent from school";
        var text = $"Dear Mr. *{row.FatherName}*\nYour child *{row.Name}* ({ClassText}) {what} today, {_date.Value:dddd dd/MM/yyyy}.\n" +
                   "If you have not informed the school, kindly send an application.\n\nRegards,\nSchool Administration";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://wa.me/{phone}?text={Uri.EscapeDataString(text)}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) { Program.ShowError("Could not open WhatsApp on this computer.", ex); }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Mark attendance", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
