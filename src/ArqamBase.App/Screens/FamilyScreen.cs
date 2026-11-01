using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Families on the left; on the right the selected family's totals, details and children (see ScreenDesigns\Family@2x.png).
/// </summary>
public sealed class FamilyScreen : ScreenBase
{
    private static Font NameFont => Theme.Scaled(1f);
    private static Font SubFont => Theme.Scaled(0.85f);

    private readonly bool _canEdit;
    private readonly KryptonTextBox _search = new() { Dock = DockStyle.Top, CueHint = { CueHintText = "Search father, CNIC or phone" } };
    private readonly ComboBox _show = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly AccessGrid _families = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, ColumnHeadersVisible = false, RowHeadersVisible = false, ShowFilters = false,
        CellBorderStyle = DataGridViewCellBorderStyle.None, TwoLineRows = true,
    };
    private readonly StatCard _childrenCard = new("Children in school"), _feeCard = new("Monthly fee (all children)"), _balanceCard = new("Family balance");
    private readonly FamilyFields _fields;
    private readonly Card _childrenBox = new("Children");
    private readonly AccessGrid _children = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false, AllowUserToOrderColumns = false, ScrollBars = ScrollBars.None,
    };
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;

    private ArqamDbContext? _db;
    private FilterBindingList<Family>? _list;
    private Family? _current;
    private Dictionary<int, string> _classNames = [], _sectionNames = [];
    private Dictionary<int, int> _balances = [], _fees = [];
    private object? _pendingNavigation;
    // The context is busy on a background thread while loading, so it must not be touched then.
    private bool _loading;

    public FamilyScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Students);
        _fields = new FamilyFields(_canEdit, showFlag: false);

        _show.Items.AddRange(["Families with children in school", "Flagged families", "All families"]);
        _show.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ApplyFilters();
        _show.SelectedIndexChanged += (_, _) => ApplyFilters();
        _families.SelectionChanged += (_, _) => ShowCurrent();
        _families.FilterChanged += (_, _) => RaiseStatusChanged();
        _families.CellPainting += PaintFamily;
        _children.CellDoubleClick += (_, _) => OpenChild();
        _children.CellFormatting += FormatChild;

        // Left: search and the list of families.
        var left = new KryptonPanel { Dock = DockStyle.Fill };
        left.Controls.Add(_families);
        left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent });
        left.Controls.Add(_show);
        left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent });
        left.Controls.Add(_search);

        // Right: totals, the family's details and the children, scrolling together.
        var stats = new EqualColumns();
        stats.Controls.AddRange([_childrenCard, _feeCard, _balanceCard]);
        var add = new KryptonButton { Text = "Add child", AutoSize = true, Enabled = _canEdit };
        add.Click += (_, _) => AddChild();
        _childrenBox.SetHeaderControl(add);
        _childrenBox.AddTop(_children);
        var content = new StackPanel();
        content.AddTop(stats, _fields, _childrenBox);
        var right = new KryptonPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 8, 0) };
        right.Controls.Add(content);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width / 4, 260, 380));
        Controls.Add(split);

        _commands =
        [
            new("Family",
                new("Fee ledger", Icons.Ledger, OpenLedger),
                new("Send WhatsApp", Icons.Chat, () => NotYet("Sending WhatsApp messages comes with the Messages screens.")),
                new("Flag family", Icons.Flag, ToggleFlag)),
            new("Print",
                new("Family challan", Icons.Print, () => NotYet("Printing the family challan comes with the Fees screens.")),
                new("Defaulter letter", Icons.Document, () => NotYet("Printing the defaulter letter comes with the Fees screens."))),
        ];
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New family";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…" : $"Families: {_families.RecordText()}{(HasChanges ? "  ·  unsaved changes" : "")}";

    // ----- Loading -----

    public override void Reload()
    {
        if (!_loading) _ = ReloadAsync();
    }

    private sealed record LoadedData(Dictionary<int, string> Classes, Dictionary<int, string> Sections, List<Family> Families,
        Dictionary<int, int> Fees, Dictionary<int, int> Balances);

    /// <summary>Runs on a background thread, so the screen opens straight away and stays responsive while the data comes in.</summary>
    private static LoadedData LoadData(ArqamDbContext db) => new(
        db.Classes.ToDictionary(c => c.Id, c => c.Name ?? ""),
        db.Sections.ToDictionary(s => s.Id, s => s.Name ?? ""),
        db.Families.Include(f => f.Students).OrderBy(f => f.FatherName).ToList(),
        db.MonthlyFees(),
        db.UnpaidBalances());

    private async Task ReloadAsync()
    {
        using var t = Timing.Step("Family.Reload");
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

            (_classNames, _sectionNames, _fees, _balances) = (data.Classes, data.Sections, data.Fees, data.Balances);
            _current = null;
            _families.ResetColumns();
            _families.AddText(nameof(Family.FatherName), "Father name", 180, fill: true);
            _list = new FilterBindingList<Family>(data.Families);
            ApplyFilters(); // before binding, so the grid makes rows only for the families shown
            _families.Bind(_list);
            t.Mark("bind list");
            if (keep is int id) Select(id);
            ShowCurrent();
            t.Mark("show family");
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

    // ----- Family list -----

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

    /// <summary>Draws each family as two lines: the father's name, then "family ID · children".</summary>
    private void PaintFamily(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics is null || _families.Rows[e.RowIndex].DataBoundItem is not Family f) return;
        var r = e.CellBounds;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selected : Theme.CardBack))
            e.Graphics.FillRectangle(back, r);
        if (selected)
            using (var bar = new SolidBrush(Theme.Accent))
                e.Graphics.FillRectangle(bar, r.X, r.Y, 3, r.Height);
        using (var line = new Pen(Theme.CardBorder))
            e.Graphics.DrawLine(line, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        var name = f.Id == 0 && string.IsNullOrWhiteSpace(f.FatherName) ? "(new family)" : f.FatherName ?? "";
        TextRenderer.DrawText(e.Graphics, name, NameFont, new Rectangle(r.X + 14, r.Y + 6, r.Width - 20, NameFont.Height), Theme.Text, flags);
        var onRoll = f.ChildrenOnRoll;
        var children = onRoll > 0 ? $"{onRoll} {(onRoll == 1 ? "child" : "children")}" : f.Students.Count > 0 ? "no children in school" : "no children";
        var sub = $"{(f.Id == 0 ? "new" : f.Id.ToString())} · {children}";
        var subRect = new Rectangle(r.X + 14, r.Y + 8 + NameFont.Height, r.Width - 20, SubFont.Height);
        TextRenderer.DrawText(e.Graphics, sub, SubFont, subRect, Theme.Muted, flags);
        if (f.Flag)
        {
            var x = subRect.X + TextRenderer.MeasureText(e.Graphics, sub, SubFont, subRect.Size, flags).Width;
            TextRenderer.DrawText(e.Graphics, "· flagged", SubFont, new Rectangle(x, subRect.Y, subRect.Right - x, subRect.Height), Theme.Warning, flags);
        }
        e.Handled = true;
    }

    // ----- Selected family -----

    private void ShowCurrent()
    {
        var next = _families.CurrentRow?.DataBoundItem as Family;
        if (ReferenceEquals(next, _current)) return;
        CommitPendingEdits();
        _current = next;
        _fields.Show(next);
        ShowChildren(next);
        RaiseStatusChanged();
    }

    private void ShowChildren(Family? family)
    {
        var rows = (family?.Students ?? [])
            .OrderByDescending(s => s.OnRoll).ThenBy(s => s.Name)
            .Select(s => new ChildRow(s.Id, s.Name ?? "",
                s.ClassId is int c && _classNames.TryGetValue(c, out var cn) ? cn : "",
                s.SectionId is int x && _sectionNames.TryGetValue(x, out var sn) ? sn : "",
                // Like the Access ledger, fees and balances count only for children in school.
                s.OnRoll ? _fees.GetValueOrDefault(s.Id) : null,
                s.OnRoll ? _balances.GetValueOrDefault(s.Id) : null,
                s.StatusText, s.OnRoll))
            .ToList();
        var fee = rows.Sum(r => r.MonthlyFee ?? 0);
        var balance = rows.Sum(r => r.Balance ?? 0);

        _childrenCard.SetValue(family is null ? "" : rows.Count(r => r.OnRoll).ToString());
        _feeCard.SetValue(family is null ? "" : $"Rs {fee:N0}");
        _balanceCard.SetValue(family is null ? "" : balance > 0 ? $"Rs {balance:N0} due" : "Nothing due", balance > 0 ? Theme.Warning : Theme.Good);

        if (rows.Count > 0)
            rows.Add(new ChildRow(null, "Total", "", "", fee, balance, "", false, IsTotal: true));
        _children.ResetColumns();
        _children.AddText(nameof(ChildRow.CC), "CC", 70);
        _children.AddText(nameof(ChildRow.Name), "Name", 180, fill: true);
        _children.AddText(nameof(ChildRow.Class), "Class", 100);
        _children.AddText(nameof(ChildRow.Section), "Section", 100);
        _children.AddText(nameof(ChildRow.MonthlyFee), "Monthly fee", 100, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _children.AddText(nameof(ChildRow.Balance), "Balance", 100, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _children.AddText(nameof(ChildRow.Status), "Status", 100);
        foreach (DataGridViewColumn col in _children.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable; // keeps the Total row last
        _children.Bind(new FilterBindingList<ChildRow>(rows));
        _children.Height = _children.ColumnHeadersHeight + Math.Max(1, rows.Count) * _children.RowTemplate.Height + 2;
        _childrenBox.Visible = family is not null;
    }

    private void FormatChild(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.CellStyle is null || _children.Rows[e.RowIndex].DataBoundItem is not ChildRow row) return;
        var column = _children.Columns[e.ColumnIndex].DataPropertyName;
        if (row.IsTotal)
            e.CellStyle.Font = Theme.Scaled(1f, FontStyle.Bold);
        else if (column == nameof(ChildRow.Balance) && row.Balance > 0)
            e.CellStyle.ForeColor = Theme.Warning;
        else if (column == nameof(ChildRow.Status) && row.OnRoll)
            e.CellStyle.ForeColor = Theme.Good;
    }

    public sealed record ChildRow(int? CC, string Name, string Class, string Section, int? MonthlyFee, int? Balance, string Status, bool OnRoll,
        bool IsTotal = false);

    /// <summary>The fee ledger of the selected child, or of the first child in school.</summary>
    private void OpenLedger()
    {
        var cc = _children.CurrentRow?.DataBoundItem is ChildRow { CC: int c } ? c : _current?.Students.Where(s => s.OnRoll).OrderBy(s => s.Name).Select(s => (int?)s.Id).FirstOrDefault();
        if (cc is int id) Ctx.OpenScreenAt("ledger", id);
        else NotYet("Select a child first.");
    }

    private void OpenChild()
    {
        if (_children.CurrentRow?.DataBoundItem is ChildRow { CC: int cc })
            Ctx.OpenScreenAt("students", cc);
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

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        // The children box is sized to its rows.
        var height = _children.ColumnHeadersHeight + Math.Max(1, _children.Rows.Count) * _children.RowTemplate.Height + 2;
        if (_children.Height != height) _children.Height = height;
    }

    // ----- Ribbon commands -----

    private void ToggleFlag()
    {
        if (_current is null || !_canEdit || _loading) return;
        _current.Flag = !_current.Flag;
        _fields.RefreshRecord(_current);
        _families.InvalidateRow(_families.CurrentRow?.Index ?? 0);
        RaiseStatusChanged();
    }

    private void NotYet(string message) =>
        KryptonMessageBox.Show(this, message, "Family", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    // ----- Records -----

    protected override void CommitPendingEdits() => _fields.Commit();

    public override void NewRecord()
    {
        if (!_canEdit || _db is null || _list is null || _loading) return;
        var family = new Family();
        _db.Families.Add(family);
        _search.Text = "";
        _show.SelectedIndex = 2;
        _list.AddExternal(family);
        Select(family);
    }

    public override bool SaveChanges()
    {
        if (_db is null || _loading || !_canEdit) return true;
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
        if (_current is null || _db is null || _list is null || _loading) return;
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
