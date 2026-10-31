using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>Families on the left; the selected family's details and children on the right.</summary>
public sealed class FamilyScreen : ScreenBase
{
    private readonly bool _canEdit;
    private readonly KryptonTextBox _search = new() { Width = 280, CueHint = { CueHintText = "Search father, mother, family ID, phone or CNIC" } };
    private readonly KryptonComboBox _show = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AccessGrid _families = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly KryptonLabel _header = new() { Dock = DockStyle.Top, LabelStyle = LabelStyle.TitlePanel, AutoSize = true };
    private readonly FamilyFields _fields;
    private readonly AccessGrid _children = new() { ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, Dock = DockStyle.Fill };

    private ArqamDbContext? _db;
    private FilterBindingList<Family>? _list;
    private Family? _current;
    private Dictionary<int, string> _classNames = [], _sectionNames = [];
    private object? _pendingNavigation;

    public FamilyScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Students);
        _fields = new FamilyFields(_canEdit) { Dock = DockStyle.Fill };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _show]);
        _show.Items.AddRange(["Families with children on roll", "Flagged families", "All families"]);
        _show.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilters();
        _show.SelectedIndexChanged += (_, _) => ApplyFilters();

        _families.SelectionChanged += (_, _) => ShowCurrent();
        _families.FilterChanged += (_, _) => RaiseStatusChanged();
        _children.CellDoubleClick += (_, e) => OpenChild();

        // Right side: details on top, children below.
        var childBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        childBar.Controls.Add(new KryptonLabel { Text = "Children", LabelStyle = LabelStyle.BoldControl, AutoSize = true, Padding = new Padding(0, 6, 12, 0) });
        var open = new KryptonButton { Text = "Open student", AutoSize = true };
        open.Click += (_, _) => OpenChild();
        var add = new KryptonButton { Text = "Add child", AutoSize = true, Enabled = _canEdit };
        add.Click += (_, _) => AddChild();
        childBar.Controls.AddRange([open, add]);

        var childPanel = new KryptonPanel { Dock = DockStyle.Fill };
        childPanel.Controls.Add(_children);
        childPanel.Controls.Add(childBar);

        var rightSplit = new KryptonSplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(_fields);
        rightSplit.Panel2.Controls.Add(childPanel);

        var right = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.Controls.Add(rightSplit);
        right.Controls.Add(_header);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_families);
        split.Panel2.Controls.Add(right);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width * 2 / 5, 300, 560));
        WhenSized(rightSplit, () => rightSplit.SplitterDistance = Math.Max(200, rightSplit.Height * 3 / 5));

        Controls.Add(split);
        Controls.Add(bar);
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override bool HasChanges => _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…" : $"Families: {_families.RecordText()}{(HasChanges ? "  ·  unsaved changes" : "")}";

    public override void Reload()
    {
        var keep = _current?.Id;
        _db?.Dispose();
        _db = Ctx.Config.CreateContext(Ctx.Session);
        UseWaitCursor = true;
        try
        {
            _classNames = _db.Classes.ToDictionary(c => c.Id, c => c.Name ?? "");
            _sectionNames = _db.Sections.ToDictionary(s => s.Id, s => s.Name ?? "");
            var families = _db.Families.Include(f => f.Students).OrderBy(f => f.FatherName).ToList();

            _families.ResetColumns();
            _families.AddText(nameof(Family.Id), "Family", 70);
            _families.AddText(nameof(Family.FatherName), "Father name", 180, fill: true);
            _families.AddText(nameof(Family.Mobile), "Mobile", 120);
            _families.AddText(nameof(Family.ChildrenOnRoll), "Children", 70);
            _families.AddCheck(nameof(Family.Flag), "Flag", 45).ReadOnly = true;
            _list = new FilterBindingList<Family>(families);
            _families.Bind(_list);
            ApplyFilters();
            if (keep is int id) Select(id);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load families.", ex);
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

    private void ApplyFilters()
    {
        if (_list is null) return;
        var term = _search.Text.Trim();
        var digits = new string(term.Where(char.IsDigit).ToArray());
        var show = _show.SelectedIndex;
        _list.SetSearch(f =>
            (show == 2 || f.Id == 0 || (show == 0 ? f.ChildrenOnRoll > 0 : f.Flag))
            && (term.Length == 0 || Matches(f, term, digits)));
        RaiseStatusChanged();
    }

    private static bool Matches(Family f, string term, string digits) =>
        f.Id.ToString() == term
        || (f.FatherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (f.MotherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || f.Students.Any(s => s.Name?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (digits.Length >= 4 && new[] { f.Mobile, f.MotherMobile, f.PhoneHome, f.PhoneOffice, f.Cnic }
            .Any(p => p is not null && new string(p.Where(char.IsDigit).ToArray()).Contains(digits)));

    private void ShowCurrent()
    {
        var next = _families.CurrentRow?.DataBoundItem as Family;
        if (ReferenceEquals(next, _current)) return;
        CommitPendingEdits();
        _current = next;
        _fields.Show(next);
        _header.Text = next is null ? "" : $"{next.FatherName}   ·   Family {(next.Id == 0 ? "(new)" : next.Id)}";
        ShowChildren(next);
        RaiseStatusChanged();
    }

    private void ShowChildren(Family? family)
    {
        _children.ResetColumns();
        _children.AddText(nameof(ChildRow.CC), "CC", 70);
        _children.AddText(nameof(ChildRow.Name), "Name", 180, fill: true);
        _children.AddText(nameof(ChildRow.Class), "Class", 110);
        _children.AddText(nameof(ChildRow.Section), "Section", 100);
        _children.AddText(nameof(ChildRow.Status), "Status", 90);
        var rows = (family?.Students ?? [])
            .OrderByDescending(s => s.OnRoll).ThenBy(s => s.Name)
            .Select(s => new ChildRow(s.Id, s.Name ?? "", s.ClassId is int c && _classNames.TryGetValue(c, out var cn) ? cn : "",
                s.SectionId is int x && _sectionNames.TryGetValue(x, out var sn) ? sn : "", s.StatusText));
        _children.Bind(new FilterBindingList<ChildRow>(rows));
    }

    public sealed record ChildRow(int CC, string Name, string Class, string Section, string Status);

    private void OpenChild()
    {
        if (_children.CurrentRow?.DataBoundItem is ChildRow { CC: > 0 } row)
            Ctx.OpenScreenAt("students", row.CC);
    }

    private void AddChild()
    {
        if (_current is null) return;
        if (_current.Id == 0)
        {
            KryptonMessageBox.Show(this, "Save the new family first, then add a child.", "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        Ctx.OpenScreenAt("students", new NewStudentInFamily(_current.Id));
    }

    protected override void CommitPendingEdits() => _fields.Commit();

    public override void NewRecord()
    {
        if (!_canEdit || _db is null || _list is null) return;
        var family = new Family();
        _db.Families.Add(family);
        _search.Text = "";
        _show.SelectedIndex = 2;
        _list.AddExternal(family);
        Select(family);
    }

    public override bool SaveChanges()
    {
        if (_db is null || !_canEdit) return true;
        CommitPendingEdits();
        var missing = _db.ChangeTracker.Entries<Family>()
            .FirstOrDefault(e => e.State is EntityState.Added or EntityState.Modified && string.IsNullOrWhiteSpace(e.Entity.FatherName));
        if (missing is not null)
        {
            KryptonMessageBox.Show(this, "Every family needs a father name.", "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            return false;
        }
        try
        {
            _db.SaveChanges();
            _families.Invalidate();
            var f = _current;
            _current = null;
            if (f is not null) Select(f);
            RaiseStatusChanged();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            KryptonMessageBox.Show(this, "Someone else changed this family while you were editing. Your changes were not saved; the list will reload.",
                "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            Reload();
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save.", ex);
            return false;
        }
    }

    public override void DeleteRecord()
    {
        if (_current is null || _db is null || _list is null) return;
        if (!Ctx.Session.IsAdmin)
        {
            KryptonMessageBox.Show(this, "Only an Admin can delete a family.", "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        if (_current.Students.Count > 0)
        {
            KryptonMessageBox.Show(this, "This family has students. Move them to another family first.", "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            return;
        }
        if (KryptonMessageBox.Show(this, $"Delete family {_current.Id} ({_current.FatherName})? It is removed when you press Save.",
                "Family", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var f = _current;
        _db.Families.Remove(f);
        _list.RemoveExternal(f);
        RaiseStatusChanged();
    }

    public override void SortAscending() => _families.SortSelectedColumn(ListSortDirection.Ascending);
    public override void SortDescending() => _families.SortSelectedColumn(ListSortDirection.Descending);
    public override void ClearFilters()
    {
        _search.Text = "";
        _show.SelectedIndex = 2;
        _families.ClearAllFilters();
    }
    public override void FocusSearch() => _search.Focus();

    public override void Navigate(object target)
    {
        if (_list is null)
        {
            _pendingNavigation = target;
            return;
        }
        if (target is int id && _list.AllItems.FirstOrDefault(f => f.Id == id) is { } family)
        {
            if (!_list.Contains(family)) ClearFilters();
            Select(family);
        }
    }

    private void Select(int id)
    {
        if (_list?.FirstOrDefault(f => f.Id == id) is { } f) Select(f);
    }

    private void Select(Family f)
    {
        if (_list is null) return;
        var index = _list.IndexOf(f);
        if (index < 0 || index >= _families.Rows.Count) return;
        _families.CurrentCell = _families.Rows[index].Cells[0];
        _families.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 2);
        ShowCurrent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}
