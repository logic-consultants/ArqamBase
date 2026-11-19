using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// The chart of accounts (ScreenDesigns canvas: Accounts – chart of accounts): account groups on the left, the accounts with
/// their balances, and the selected account's details below. Replaces the Access "Enter Accounts", "Accounts" and
/// "Account Types" forms. The Accountant and Admin change accounts; an account with voucher lines cannot be deleted.
/// </summary>
public sealed class ChartOfAccountsScreen : ScreenBase
{
    /// <summary>One account in the grid, with its balance from every voucher line.</summary>
    public sealed class Row(Account account)
    {
        public Account Account { get; } = account;
        public string AccountNo => Account.AccountNo;
        public string Name => Account.Name;
        public string TypeName { get; set; } = "";
        public string SubTypeName { get; set; } = "";
        public int Lines { get; set; }
        public decimal Net { get; set; }
        /// <summary>The balance without its sign; <see cref="Side"/> says Dr or Cr.</summary>
        public decimal Balance => Math.Abs(Net);
        public string Side => Net > 0 ? "Dr" : Net < 0 ? "Cr" : "";
        public string ActiveText => Account.IsActive ? "Yes" : "No";
        public decimal Opening { get; set; }
    }

    private readonly bool _canEdit;
    private readonly KryptonTextBox _search = new() { Width = 260, CueHint = { CueHintText = "Find account no. or name" } };
    private readonly KryptonCheckBox _activeOnly = new() { Text = "Active accounts only", Checked = true, AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    private readonly KryptonCheckBox _hideZero = new() { Text = "Hide zero balances", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
    private readonly KryptonButton _clear = new() { Text = "Clear filters", AutoSize = true, Margin = new Padding(12, 2, 3, 3) };
    private readonly Label _info = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(16, 8, 3, 3), BackColor = Color.Transparent };
    private readonly KryptonTreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly AccessGrid _grid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
    };
    private readonly BindingSource _bs = new() { DataSource = typeof(Account) };
    private readonly KryptonTextBox _opening = new() { ReadOnly = true };
    private readonly Label _balance = new() { AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
    private readonly Card _details = new("Account");
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private ComboBox _type = null!, _subType = null!;

    private ArqamDbContext? _db;
    private FilterBindingList<Row>? _list;
    private List<AccountType> _types = [];
    private List<AccountSubType> _subTypes = [];
    private Row? _current;
    private DateTime? _lastDate;
    private bool _loading;

    public ChartOfAccountsScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Accounts);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false };
        bar.Controls.AddRange([_search, _activeOnly, _hideZero, _clear, _info]);
        _search.TextChanged += (_, _) => ApplyFilters();
        _activeOnly.CheckedChanged += (_, _) => ApplyFilters();
        _hideZero.CheckedChanged += (_, _) => ApplyFilters();
        _clear.Click += (_, _) => ClearFilters();
        _tree.AfterSelect += (_, _) => { if (!_loading) ApplyFilters(); };

        _grid.AddText(nameof(Row.AccountNo), "Acc. no.", 80);
        _grid.AddText(nameof(Row.Name), "Account", 220, fill: true);
        _grid.AddText(nameof(Row.TypeName), "Type", 100);
        _grid.AddText(nameof(Row.SubTypeName), "Sub-type", 120);
        _grid.AddText(nameof(Row.Lines), "Lines", 70).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Balance), "Balance", 120, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(Row.Side), "Dr/Cr", 55);
        _grid.AddText(nameof(Row.ActiveText), "Active", 60);
        _grid.SelectionChanged += (_, _) => ShowCurrent();
        _grid.CurrentCellChanged += (_, _) => ShowCurrent();
        _grid.FilterChanged += (_, _) => ShowInfo();

        BuildDetails();

        var right = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.Controls.Add(_grid);
        right.Controls.Add(_details);
        _details.Dock = DockStyle.Bottom;
        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_tree);
        split.Panel2.Controls.Add(right);
        WhenSized(split, () => split.SplitterDistance = 250);

        Controls.Add(split);
        Controls.Add(bar);
        Controls.Add(Heading("Chart of accounts"));

        _commands =
        [
            new("List", new ScreenCommand("Print list", Icons.Print, PrintList), new ScreenCommand("Excel", Icons.Excel, ExportExcel)),
        ];
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New account";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _db?.ChangeTracker.HasChanges() == true;
    public override string StatusText => _list is null ? "Loading…"
        : $"{_list.Count:N0} of {_list.TotalCount:N0} accounts{(_lastDate is { } d ? $"  ·  balances to {d:dd/MM/yyyy}" : "")}{(HasChanges ? "  ·  unsaved changes" : "")}";

    // ----- Layout -----

    private void BuildDetails()
    {
        var f = new FieldPanel(4);
        f.Add("Account no.", Inputs.Text(_bs, nameof(Account.AccountNo)));
        f.Add("Account name", Inputs.Text(_bs, nameof(Account.Name)), 2);
        f.Add("Type", _type = Inputs.Combo<int?>(_bs, nameof(Account.AccountTypeId), []));
        f.Add("Sub-type", _subType = Inputs.Combo<int?>(_bs, nameof(Account.SubTypeId), []));
        f.Add("Kind of account", Inputs.Combo<AccountKind?>(_bs, nameof(Account.Kind),
            [(AccountKind.Ordinary, "Ordinary"), (AccountKind.Cash, "Cash"), (AccountKind.Bank, "Bank")]));
        f.Add("Bank account no.", Inputs.Text(_bs, nameof(Account.BankAccountNo)));
        f.Add("Opening balance (opening voucher)", _opening);
        f.Add("Care of", Inputs.Text(_bs, nameof(Account.CareOf)));
        f.Add("Mobile", Inputs.Text(_bs, nameof(Account.Mobile)));
        f.Add("PTCL", Inputs.Text(_bs, nameof(Account.Phone)));
        f.Add("", Inputs.Check(_bs, nameof(Account.IsActive), "Active"));
        f.Add("Address", Inputs.Text(_bs, nameof(Account.Address)), 2);
        f.Add("Notes", Inputs.Text(_bs, nameof(Account.Notes)), 2);
        f.Enabled = _canEdit;
        _details.AddTop(f, _balance);
        _bs.CurrentItemChanged += (_, _) => { if (!_loading) RaiseStatusChanged(); };
    }

    // ----- Loading -----

    public override void Reload()
    {
        if (!ConfirmLeave()) return;
        _loading = true;
        var keep = _current?.Account.Id;
        try
        {
            _db?.Dispose();
            _db = Ctx.Config.CreateContext(Ctx.Session);
            _types = _db.AccountTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToList();
            _subTypes = _db.AccountSubTypes.AsNoTracking().OrderBy(s => s.Name).ToList();
            var accounts = _db.Accounts.OrderBy(a => a.AccountNo).ToList();
            var totals = _db.VoucherLines.AsNoTracking().GroupBy(l => l.AccountId)
                .Select(g => new { g.Key, Lines = g.Count(), Net = g.Sum(l => l.Debit - l.Credit) }).ToDictionary(x => x.Key);
            var openingIds = _db.Vouchers.AsNoTracking().Where(v => v.VoucherType == "Opening").Select(v => v.Id).ToList();
            var opening = _db.VoucherLines.AsNoTracking().Where(l => l.VoucherId != null && openingIds.Contains(l.VoucherId.Value))
                .GroupBy(l => l.AccountId).Select(g => new { g.Key, Net = g.Sum(l => l.Debit - l.Credit) }).ToDictionary(x => x.Key, x => x.Net);
            _lastDate = _db.Vouchers.AsNoTracking().Max(v => (DateTime?)v.VoucherDate);

            Inputs.SetItems(_type, _types.Select(t => ((int?)t.Id, t.Name)));
            Inputs.SetItems(_subType, _subTypes.Select(s => ((int?)s.Id, s.Name)));
            var rows = accounts.Select(a =>
            {
                var r = new Row(a) { Opening = opening.GetValueOrDefault(a.Id) };
                if (totals.TryGetValue(a.Id, out var t)) (r.Lines, r.Net) = (t.Lines, t.Net);
                Describe(r);
                return r;
            }).ToList();
            _list = new FilterBindingList<Row>(rows);
            _grid.Bind(_list);
            BuildTree();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the accounts. Has sql/accounts_install.sql been run on this database?", ex);
        }
        finally { _loading = false; }
        ApplyFilters();
        if (keep is int id) Select(id);
        ShowCurrent();
    }

    private void Describe(Row r)
    {
        r.TypeName = _types.FirstOrDefault(t => t.Id == r.Account.AccountTypeId)?.Name ?? "";
        r.SubTypeName = _subTypes.FirstOrDefault(s => s.Id == r.Account.SubTypeId)?.Name ?? "";
    }

    /// <summary>All accounts, each type, and each sub-type under its type, with the number of accounts.</summary>
    private void BuildTree()
    {
        var rows = _list?.AllItems ?? [];
        var selected = _tree.SelectedNode?.Tag;
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var all = _tree.Nodes.Add($"All accounts ({rows.Count})");
        foreach (var t in _types)
        {
            var node = all.Nodes.Add($"{t.Name} ({rows.Count(r => r.Account.AccountTypeId == t.Id)})");
            node.Tag = (t.Id, (int?)null);
            foreach (var s in _subTypes.Where(s => s.AccountTypeId == t.Id))
                node.Nodes.Add(new TreeNode($"{s.Name} ({rows.Count(r => r.Account.SubTypeId == s.Id)})") { Tag = (t.Id, (int?)s.Id) });
        }
        all.ExpandAll();
        _tree.SelectedNode = FindNode(_tree.Nodes, selected) ?? all;
        _tree.EndUpdate();
    }

    private static TreeNode? FindNode(TreeNodeCollection nodes, object? tag)
    {
        if (tag is null) return null;
        foreach (TreeNode n in nodes)
        {
            if (Equals(n.Tag, tag)) return n;
            if (FindNode(n.Nodes, tag) is { } found) return found;
        }
        return null;
    }

    // ----- Filtering and showing -----

    private void ApplyFilters()
    {
        if (_list is null) return;
        var term = _search.Text.Trim();
        var (typeId, subTypeId) = _tree.SelectedNode?.Tag is ValueTuple<int, int?> tag ? ((int?)tag.Item1, tag.Item2) : (null, null);
        var (activeOnly, hideZero) = (_activeOnly.Checked, _hideZero.Checked);
        _list.SetSearch(r => r.Account.Id == 0
            || ((typeId is null || r.Account.AccountTypeId == typeId)
                && (subTypeId is null || r.Account.SubTypeId == subTypeId)
                && (!activeOnly || r.Account.IsActive)
                && (!hideZero || r.Net != 0)
                && (term.Length == 0 || r.AccountNo.Contains(term, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))));
        ShowInfo();
    }

    private void ShowInfo()
    {
        if (_list is null) return;
        var shown = _list.ToList();
        var net = shown.Sum(r => r.Net);
        _info.Text = $"{shown.Count:N0} accounts shown · net {Math.Abs(net):N0} {(net >= 0 ? "Dr" : "Cr")}";
        RaiseStatusChanged();
    }

    private void ShowCurrent()
    {
        if (_loading) return;
        var next = _grid.CurrentRow?.DataBoundItem as Row;
        if (ReferenceEquals(next, _current) && _current is not null) return;
        CommitPendingEdits();
        _current = next;
        _bs.DataSource = _current?.Account ?? (object)typeof(Account);
        _details.Enabled = _current is not null;
        _opening.Text = _current is null || _current.Opening == 0 ? "" : $"{Math.Abs(_current.Opening):N0} {(_current.Opening > 0 ? "Dr" : "Cr")}";
        _balance.Text = _current is null ? ""
            : $"Current balance Rs {_current.Balance:N0} {_current.Side} · {_current.Lines:N0} voucher lines" +
              (_canEdit ? "" : " · only the Accountant or an Admin changes accounts");
        RaiseStatusChanged();
    }

    private void Select(int accountId)
    {
        if (_list is null) return;
        var index = _list.ToList().FindIndex(r => r.Account.Id == accountId);
        if (index >= 0 && index < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[index].Cells[0];
    }

    // ----- Records -----

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate();
        _bs.EndEdit();
    }

    public override void NewRecord()
    {
        if (!_canEdit || _db is null || _list is null || !SaveChanges()) return;
        var (typeId, subTypeId) = _tree.SelectedNode?.Tag is ValueTuple<int, int?> tag ? (tag.Item1, tag.Item2) : (_types.FirstOrDefault()?.Id ?? 1, null);
        var next = _list.AllItems.Select(r => int.TryParse(r.AccountNo, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var account = new Account
        {
            AccountNo = next.ToString("0000"), Name = "", AccountTypeId = typeId, SubTypeId = subTypeId,
            Kind = subTypeId is int s && _subTypes.FirstOrDefault(x => x.Id == s)?.Name.Contains("Cash") == true ? AccountKind.Bank : AccountKind.Ordinary,
            IsActive = true, CreatedBy = Ctx.Session.UserName, CreatedOn = DateTime.Now,
        };
        _db.Accounts.Add(account);
        var row = new Row(account);
        Describe(row);
        _list.AddExternal(row);
        Select(0);
        ShowCurrent();
    }

    public override bool SaveChanges()
    {
        if (_db is null || _loading || !_canEdit) return true;
        CommitPendingEdits();
        var changed = _db.ChangeTracker.Entries<Account>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToList();
        if (changed.Count == 0) return true;
        foreach (var a in changed)
        {
            a.AccountNo = a.AccountNo.Trim();
            string? problem = null;
            if (a.AccountNo.Length == 0) problem = "needs an account number";
            else if (string.IsNullOrWhiteSpace(a.Name)) problem = "needs a name";
            else if (_list!.AllItems.Any(r => r.Account != a && r.AccountNo.Equals(a.AccountNo, StringComparison.OrdinalIgnoreCase))) problem = $"has the number {a.AccountNo}, which another account already has";
            else if (a.SubTypeId is int s && _subTypes.FirstOrDefault(x => x.Id == s)?.AccountTypeId is int t && t != a.AccountTypeId) problem = "has a sub-type that belongs to another type";
            if (problem is not null)
            {
                Inform($"Account {a.AccountNo} {a.Name} {problem}.".Replace("  ", " "));
                Select(a.Id);
                return false;
            }
            if (_db.Entry(a).State == EntityState.Modified) (a.UpdatedBy, a.UpdatedOn) = (Ctx.Session.UserName, DateTime.Now);
        }
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            Inform("Someone else changed this account meanwhile. Your change was not saved; the accounts will be loaded again.");
            _db.ChangeTracker.Clear();
            Reload();
            return false;
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save the account.", ex); return false; }
        foreach (var r in _list!.AllItems) Describe(r);
        BuildTree();
        _grid.Invalidate();
        RaiseStatusChanged();
        return true;
    }

    /// <summary>Deletes an account that no voucher uses; one that is used can be made inactive instead.</summary>
    public override void DeleteRecord()
    {
        if (!_canEdit || _db is null || _list is null || _current is not { } row) return;
        if (row.Lines > 0)
        {
            Inform($"{row.AccountNo} {row.Name} is used on {row.Lines:N0} voucher lines, so it cannot be deleted. Untick Active to hide it.");
            return;
        }
        if (KryptonMessageBox.Show(this, $"Delete account {row.AccountNo} {row.Name}?", "Chart of accounts",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _db.Accounts.Remove(row.Account);
        try
        {
            if (row.Account.Id != 0) _db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not delete the account.", ex); Reload(); return; }
        _list.RemoveExternal(row);
        BuildTree();
        ShowCurrent();
    }

    public override void SortAscending() => _grid.SortSelectedColumn(ListSortDirection.Ascending);
    public override void SortDescending() => _grid.SortSelectedColumn(ListSortDirection.Descending);
    public override void FocusSearch() => _search.Focus();

    public override void ClearFilters()
    {
        _search.Text = "";
        _activeOnly.Checked = false;
        _hideZero.Checked = false;
        if (_tree.Nodes.Count > 0) _tree.SelectedNode = _tree.Nodes[0];
        _grid.ClearAllFilters();
        ApplyFilters();
    }

    // ----- Output -----

    private void PrintList() =>
        PrintPreviewForm.Show(this, () => Printing.ListExport.From(_grid, "Chart of accounts").ToPdf(), "Chart of accounts", "Chart of accounts");

    private void ExportExcel()
    {
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"Chart of accounts {DateTime.Today:yyyy-MM-dd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { Printing.ListExport.From(_grid, "Chart of accounts").SaveExcel(dialog.FileName); }
        catch (IOException ex) { Program.ShowError("Could not save the Excel file. Is it open in Excel?", ex); }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Chart of accounts", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}
