using System.ComponentModel;
using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Voucher entry (ScreenDesigns canvas: Accounts – daily voucher): the vouchers of a financial year on the left, the chosen voucher's
/// lines on the right. A daily cash voucher (one a day) starts with cash in hand brought forward and ends with cash in hand carried
/// forward, both worked out by the app. Bank, journal and opening vouchers must balance by themselves. Replaces the Access
/// Trans_head and table1 forms. Days up to the year's lock date are changed only by an Admin.
/// </summary>
public sealed class VouchersScreen : ScreenBase
{
    private static readonly (string Type, string Text)[] Types = [("Daily", "Daily cash"), ("Bank", "Bank"), ("Journal", "Journal"), ("Opening", "Opening")];
    private const string Title = "Daily voucher";

    /// <summary>A voucher in the list on the left.</summary>
    public sealed class VoucherRow
    {
        public int Id { get; init; }
        public DateTime VoucherDate { get; init; }
        public string Day => VoucherDate.ToString("ddd");
        public int VoucherNo { get; init; }
        public string VoucherType { get; init; } = "";
        public int? Folio { get; init; }
        public string? Narration { get; init; }
        public decimal Amount { get; init; }
        public int Lines { get; init; }
        public string Status { get; set; } = "";
    }

    /// <summary>The accounts by id and by number; an account number can be typed without its leading zeros.</summary>
    public sealed class AccountLookup(IReadOnlyList<Account> accounts)
    {
        public IReadOnlyList<Account> All { get; } = accounts;
        private readonly Dictionary<int, Account> _byId = accounts.ToDictionary(a => a.Id);

        public Account? ById(int id) => _byId.GetValueOrDefault(id);

        public Account? ByNumber(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return null;
            return All.FirstOrDefault(a => a.AccountNo.Equals(text, StringComparison.OrdinalIgnoreCase))
                   ?? (int.TryParse(text, out var n) ? All.FirstOrDefault(a => int.TryParse(a.AccountNo, out var m) && m == n) : null);
        }
    }

    /// <summary>One voucher line in the grid.</summary>
    public sealed class LineRow(VoucherLine line, AccountLookup accounts) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public VoucherLine Line { get; } = line;
        public int? No { get; set; }

        /// <summary>Typing a number fills the account; an unknown number is refused.</summary>
        public string AccountNo
        {
            get => accounts.ById(Line.AccountId)?.AccountNo ?? "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) Line.AccountId = 0;
                else Line.AccountId = (accounts.ByNumber(value) ?? throw new FormatException($"There is no account {value}.")).Id;
                Changed();
            }
        }

        public int? AccountId
        {
            get => Line.AccountId == 0 ? null : Line.AccountId;
            set { Line.AccountId = value ?? 0; Changed(); }
        }

        public string AccountName => accounts.ById(Line.AccountId)?.Name ?? "";
        public string? Description { get => Line.Description; set => Line.Description = Blank(value); }
        public string? BillNo { get => Line.BillNo; set => Line.BillNo = Blank(value); }
        public string? ChequeNo { get => Line.ChequeNo; set => Line.ChequeNo = Blank(value); }
        public DateTime? ChequeDate { get => Line.ChequeDate; set => Line.ChequeDate = value?.Date; }

        /// <summary>Debit (Access "D"). Entering a debit clears the credit: a line is one or the other.</summary>
        public decimal? Debit
        {
            get => Line.Debit == 0 ? null : Line.Debit;
            set
            {
                Line.Debit = Amount(value);
                if (Line.Debit != 0) Line.Credit = 0;
                Changed();
            }
        }

        /// <summary>Credit (Access "W").</summary>
        public decimal? Credit
        {
            get => Line.Credit == 0 ? null : Line.Credit;
            set
            {
                Line.Credit = Amount(value);
                if (Line.Credit != 0) Line.Debit = 0;
                Changed();
            }
        }

        public bool IsOk { get => Line.IsOk; set => Line.IsOk = value; }
        public bool IsCleared { get => Line.IsCleared; set => Line.IsCleared = value; }
        public string Source => Line.Source;
        public string EnteredBy => Line.EnteredBy ?? "";

        /// <summary>Cash in hand brought or carried forward, worked out by the app.</summary>
        public bool IsAuto => Line.Source == "Auto";
        /// <summary>Lines the user does not type: the cash carried forward and the posted fee receipts.</summary>
        public bool IsFixed => Line.Source is "Auto" or "Fees";
        public bool IsBlank => Line.AccountId == 0 && Line.Debit == 0 && Line.Credit == 0 && Description is null && BillNo is null && ChequeNo is null;

        public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));

        /// <summary>Puts a signed amount on the right side: positive is a debit, negative a credit.</summary>
        public void SetNet(decimal net)
        {
            Line.Debit = Math.Max(net, 0);
            Line.Credit = Math.Max(-net, 0);
            Changed();
        }

        private static decimal Amount(decimal? value) =>
            value is < 0 ? throw new FormatException("Amounts cannot be negative; use the other column.") : Math.Round(value ?? 0, 2);

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private readonly bool _canEdit, _isAdmin;

    // Left: the vouchers of the year.
    private readonly ComboBox _yearBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Margin = new Padding(0, 4, 6, 3) };
    private readonly List<KryptonCheckButton> _typeButtons = [];
    private readonly KryptonTextBox _listSearch = new() { Width = 270, CueHint = { CueHintText = "Date, folio or narration" }, Margin = new Padding(0, 4, 3, 3) };
    private readonly AccessGrid _vgrid = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
    };

    // Right: the voucher.
    private readonly KryptonTextBox _no = new() { ReadOnly = true };
    private readonly KryptonTextBox _type = new() { ReadOnly = true };
    private readonly KryptonTextBox _date = new();
    private readonly KryptonTextBox _folio = new();
    private readonly KryptonTextBox _narration = new();
    private readonly Label _state = new() { AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
    private readonly KryptonTextBox _lineSearch = new() { Width = 300, CueHint = { CueHintText = "Search account, description, bill or cheque no." } };
    private readonly KryptonButton _clear = new() { Text = "Clear filters", AutoSize = true, Margin = new Padding(8, 2, 3, 3) };
    private readonly Label _lineInfo = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 3, 3), BackColor = Color.Transparent };
    private readonly AccessGrid _grid = new() { SelectionMode = DataGridViewSelectionMode.CellSelect };
    private readonly Label _totals = new() { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 9, 12, 3) };
    private readonly Label _balanced = new() { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 9, 12, 3) };
    private readonly KryptonButton _save = new() { Text = "Save", AutoSize = true };
    private readonly KryptonButton _cancel = new() { Text = "Cancel", AutoSize = true };
    private readonly KryptonButton _lock = new() { Text = "Lock this day", AutoSize = true };
    private readonly KryptonButton _print = new() { Text = "Print voucher", AutoSize = true };
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;

    private ArqamDbContext? _db;
    private List<FinancialYear> _years = [];
    private FinancialYear? _year;
    private AccountLookup _accounts = new([]);
    private Account? _cash;
    private FilterBindingList<VoucherRow>? _vlist;
    private FilterBindingList<LineRow>? _list;
    private Voucher? _voucher;
    private readonly List<VoucherLine> _removed = [];
    private string _selectedType = "Daily";
    private decimal? _previousCarry;
    private bool _dirty, _loading;
    /// <summary>The voucher whose row was last opened from the list.</summary>
    private int? _listId;

    public VouchersScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Accounts);
        _isAdmin = ctx.Session.IsAdmin;

        // ----- Left -----
        var leftBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true };
        leftBar.Controls.Add(_yearBox);
        foreach (var (type, text) in Types)
        {
            var b = new KryptonCheckButton { Text = text, Margin = new Padding(0, 3, 2, 3), Tag = type, Checked = type == _selectedType };
            Fit(b);
            b.Click += (_, _) => SelectType(type);
            _typeButtons.Add(b);
            leftBar.Controls.Add(b);
        }
        leftBar.SetFlowBreak(_typeButtons[^1], true);
        leftBar.Controls.Add(_listSearch);
        _listSearch.TextChanged += (_, _) => FilterVouchers();
        _yearBox.SelectedIndexChanged += (_, _) => { if (!_loading) ChangeYear(); };

        _vgrid.AddText(nameof(VoucherRow.VoucherDate), "Date", 80, format: "dd/MM/yyyy");
        _vgrid.AddText(nameof(VoucherRow.Day), "Day", 42);
        _vgrid.AddText(nameof(VoucherRow.VoucherNo), "No.", 48);
        _vgrid.AddText(nameof(VoucherRow.Folio), "Folio", 48);
        _vgrid.AddText(nameof(VoucherRow.Amount), "Amount", 90, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _vgrid.AddText(nameof(VoucherRow.Status), "Status", 70, fill: true);
        _vgrid.CurrentCellChanged += (_, _) => { if (!_loading) OpenSelected(); };
        _vgrid.FilterChanged += (_, _) => RaiseStatusChanged();

        var left = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0) };
        left.Controls.Add(_vgrid);
        left.Controls.Add(leftBar);

        // ----- Right -----
        var f = new FieldPanel(5);
        f.Add("Voucher no.", _no);
        f.Add("Type", _type);
        f.Add("Date", _date);
        f.Add("Folio (page)", _folio);
        f.Skip();
        f.Add("Narration", _narration, 5);
        var header = new Card("Voucher");
        header.AddTop(f, _state);
        foreach (var box in new[] { _date, _folio, _narration })
            box.TextChanged += (_, _) => { if (!_loading) { _dirty = true; RaiseStatusChanged(); } };
        _date.Leave += (_, _) => DateChanged();
        _folio.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar);

        var lineBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        lineBar.Controls.AddRange([_lineSearch, _clear, _lineInfo]);
        _lineSearch.TextChanged += (_, _) => FilterLines();
        _clear.Click += (_, _) => ClearFilters();

        _grid.CellParsing += ParseCell;
        _grid.CellFormatting += FormatCell;
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].DataPropertyName != nameof(LineRow.EnteredBy)
                || _grid.Rows[e.RowIndex].DataBoundItem is not LineRow { Line: var l }) return;
            var parts = new List<string>();
            if (l.EnteredBy is not null) parts.Add($"Entered by {l.EnteredBy} on {l.EnteredOn:dd/MM/yyyy HH:mm}");
            if (l.ChangedBy is not null) parts.Add($"Last changed by {l.ChangedBy} on {l.ChangedOn:dd/MM/yyyy HH:mm}");
            e.ToolTipText = parts.Count > 0 ? string.Join("\n", parts) : l.Source == "Import" ? "Imported from Access" : "";
        };
        _grid.CellValueChanged += (_, _) => { if (!_loading) { _dirty = true; ShowTotals(); } };
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.UserDeletingRow += (_, e) =>
        {
            if (e.Row?.DataBoundItem is LineRow { IsFixed: true }) { e.Cancel = true; Inform("Cash brought or carried forward and posted fee lines cannot be deleted here."); }
        };
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is ComboBox combo)
            {
                combo.DropDownWidth = 320;
                combo.MaxDropDownItems = 20;
            }
        };
        _grid.FilterChanged += (_, _) => ShowLineInfo();

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Padding = new Padding(0, 6, 0, 0) };
        bottom.Controls.AddRange([_totals, _balanced]);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([_save, _cancel, _lock, _print]);
        foreach (var b in new Control[] { _print, _lock, _cancel, _save, _clear }) Fit(b);
        _save.Click += (_, _) => SaveChanges();
        _cancel.Click += (_, _) => CancelChanges();
        _lock.Click += (_, _) => LockDay();
        _print.Click += (_, _) => PrintVoucher();

        var right = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        right.Controls.Add(_grid);
        right.Controls.Add(bottom);
        right.Controls.Add(buttons);
        right.Controls.Add(lineBar);
        right.Controls.Add(header);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        WhenSized(split, () => split.SplitterDistance = 420);

        Controls.Add(split);
        Controls.Add(Heading(Title));

        _commands =
        [
            new("Voucher", new ScreenCommand("Delete voucher", Icons.Delete, DeleteVoucher), new ScreenCommand("Cancel changes", Icons.Undo, CancelChanges)),
            new("Day", new ScreenCommand("Lock or unlock day", Icons.Lock, LockDay)),
            new("Print", new ScreenCommand("Voucher", Icons.Print, PrintVoucher), new ScreenCommand("Excel", Icons.Excel, ExportExcel)),
        ];
        HandleCreated += (_, _) => Reload();
    }

    /// <summary>A fixed width that fits the text: auto-sized Krypton buttons in a flow panel can overlap their neighbours.</summary>
    private static void Fit(Control b)
    {
        b.AutoSize = false;
        b.Size = new Size(TextRenderer.MeasureText(b.Text.Replace("&", ""), Theme.BaseFont).Width + 18, Theme.RowHeight + 2);
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New voucher";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _voucher is not null && (_dirty || _removed.Count > 0 || _db?.ChangeTracker.HasChanges() == true);

    public override string StatusText
    {
        get
        {
            if (_vlist is null) return "Loading…";
            var parts = new List<string> { $"{_vlist.Count:N0} of {_vlist.TotalCount:N0} vouchers" };
            if (_voucher is not null) parts.Add(_voucher.Id == 0 ? "new voucher, not saved yet" : $"voucher {_voucher.VoucherNo}");
            if (_year is not null) parts.Add($"financial year {_year.Name}");
            if (_year?.LockedUpTo is { } locked) parts.Add($"locked up to {locked:dd/MM/yyyy}");
            if (HasChanges) parts.Add("unsaved changes");
            return string.Join("  ·  ", parts);
        }
    }

    // ----- Loading -----

    public override void Reload()
    {
        if (!ConfirmLeave()) return;
        _loading = true;
        var keepYear = _year?.Id;
        var keepVoucher = _voucher?.Id;
        try
        {
            NewContext();
            _years = _db!.FinancialYears.AsNoTracking().OrderBy(y => y.StartDate).ToList();
            var accounts = _db.Accounts.AsNoTracking().OrderBy(a => a.AccountNo).ToList();
            _accounts = new AccountLookup(accounts);
            _cash = accounts.Where(a => a.Kind == AccountKind.Cash && a.IsActive).OrderBy(a => a.AccountNo).FirstOrDefault()
                    ?? accounts.FirstOrDefault(a => a.Kind == AccountKind.Cash);
            BuildColumns();

            _yearBox.DataSource = _years.Select(y => y.Name).ToList();
            var year = _years.FirstOrDefault(y => y.Id == keepYear)
                       ?? _years.FirstOrDefault(y => y.StartDate <= DateTime.Today && DateTime.Today <= y.EndDate)
                       ?? _years.LastOrDefault();
            _yearBox.SelectedIndex = year is null ? -1 : _years.IndexOf(year);
            _year = year is null ? null : _db.FinancialYears.Find(year.Id);
            LoadVoucherList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the vouchers. Has sql/accounts_install.sql been run on this database?", ex);
        }
        finally { _loading = false; }

        (_voucher, _listId, _dirty) = (null, null, false);
        _removed.Clear();
        if (keepVoucher is int id and > 0 && _vlist?.AllItems.Any(v => v.Id == id) == true) Open(id);
        else OpenSelected();
    }

    private void NewContext()
    {
        _db?.Dispose();
        _db = Ctx.Config.CreateContext(Ctx.Session);
        if (_year is not null) _year = _db.FinancialYears.Find(_year.Id);
    }

    /// <summary>The line columns; the account list is filled once the accounts are loaded, cash and banks first.</summary>
    private void BuildColumns()
    {
        _grid.ResetColumns();
        _grid.AddText(nameof(LineRow.No), "#", 36, readOnly: true);
        _grid.AddText(nameof(LineRow.AccountNo), "Acc. no.", 70);
        var items = _accounts.All
            .OrderBy(a => a.IsActive ? 0 : 1).ThenBy(a => a.Kind == AccountKind.Ordinary ? 1 : 0).ThenBy(a => a.AccountNo)
            .Select(a => ((int?)a.Id, a.IsActive ? a.Name : $"{a.Name} (inactive)"));
        _grid.AddCombo(nameof(LineRow.AccountId), "Account", items, 200);
        _grid.AddText(nameof(LineRow.Description), "Description", 220, fill: true);
        _grid.AddText(nameof(LineRow.BillNo), "Bill no.", 70);
        _grid.AddText(nameof(LineRow.ChequeNo), "Chq no.", 80);
        _grid.AddText(nameof(LineRow.ChequeDate), "Chq date", 85, format: "dd/MM/yyyy");
        _grid.AddText(nameof(LineRow.Debit), "Debit (D)", 95, format: "#,##0.##").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddText(nameof(LineRow.Credit), "Credit (W)", 95, format: "#,##0.##").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.AddCheck(nameof(LineRow.IsOk), "OK", 45);
        _grid.AddCheck(nameof(LineRow.IsCleared), "Cleared", 60);
        _grid.AddText(nameof(LineRow.Source), "From", 60, readOnly: true);
        _grid.AddText(nameof(LineRow.EnteredBy), "Entered by", 90, readOnly: true);
        _grid.CanEditCell = CanEditCell;
    }

    /// <summary>The year's vouchers, newest first, with their totals.</summary>
    private void LoadVoucherList()
    {
        if (_db is null) return;
        var yearId = _year?.Id ?? -1;
        var rows = _db.Vouchers.AsNoTracking().Where(v => v.YearId == yearId)
            .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.VoucherNo)
            .Select(v => new VoucherRow
            {
                Id = v.Id, VoucherDate = v.VoucherDate, VoucherNo = v.VoucherNo, VoucherType = v.VoucherType, Folio = v.Folio, Narration = v.Narration,
                Amount = v.Lines.Sum(l => (decimal?)l.Debit) ?? 0, Lines = v.Lines.Count(),
            }).ToList();
        foreach (var r in rows) r.Status = IsLocked(r.VoucherDate) ? "Locked" : "";
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _vlist = new FilterBindingList<VoucherRow>(rows);
            _vgrid.Bind(_vlist);
            FilterVouchers();
        }
        finally { _loading = wasLoading; }
    }

    private void FilterVouchers()
    {
        if (_vlist is null) return;
        var term = _listSearch.Text.Trim();
        var type = _selectedType;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _vlist.SetSearch(v => v.VoucherType == type
                && (term.Length == 0 || v.VoucherDate.ToString("dd/MM/yyyy ddd").Contains(term, StringComparison.OrdinalIgnoreCase)
                    || v.Folio?.ToString() == term || v.VoucherNo.ToString() == term
                    || v.Narration?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true));
            if (_voucher is { Id: > 0 } current) SelectInList(current.Id);
        }
        finally { _loading = wasLoading; }
        RaiseStatusChanged();
    }

    private void SelectType(string type)
    {
        var leave = type != _selectedType && _voucher is not null && _voucher.VoucherType != type;
        if (leave && !ConfirmLeave()) type = _selectedType;
        foreach (var b in _typeButtons) b.Checked = (string)b.Tag! == type;
        if (type == _selectedType) return;
        _selectedType = type;
        if (leave)
        {
            (_voucher, _listId, _dirty) = (null, null, false);
            _removed.Clear();
        }
        FilterVouchers();
        OpenSelected();
    }

    private void ChangeYear()
    {
        if (!ConfirmLeave())
        {
            _loading = true;
            _yearBox.SelectedIndex = _year is null ? -1 : _years.FindIndex(y => y.Id == _year.Id);
            _loading = false;
            return;
        }
        var year = _yearBox.SelectedIndex >= 0 ? _years[_yearBox.SelectedIndex] : null;
        _dirty = false;
        _removed.Clear();
        NewContext();
        _year = year is null ? null : _db!.FinancialYears.Find(year.Id);
        (_voucher, _listId) = (null, null);
        LoadVoucherList();
        OpenSelected();
    }

    private void SelectInList(int voucherId)
    {
        if (_vlist is null) return;
        var index = _vlist.ToList().FindIndex(v => v.Id == voucherId);
        if (index >= 0 && index < _vgrid.Rows.Count && _vgrid.CurrentCell?.RowIndex != index)
            _vgrid.CurrentCell = _vgrid.Rows[index].Cells[0];
    }

    /// <summary>Opens the voucher chosen on the left, after asking to save the one being changed.</summary>
    private void OpenSelected()
    {
        // The list also moves by itself (it picks its first row when it gets the focus), so only a different row opens a voucher.
        var row = _vgrid.CurrentRow?.DataBoundItem as VoucherRow;
        if (row is not null && (row.Id == _voucher?.Id || row.Id == _listId)) return;
        if (row is null && _voucher is not null) return;
        if (!ConfirmLeave())
        {
            if (_listId is int id) { _loading = true; SelectInList(id); _loading = false; }
            return;
        }
        if (row is null) ShowVoucher(null, []);
        else Open(row.Id);
    }

    private void Open(int voucherId)
    {
        try
        {
            NewContext();
            var voucher = _db!.Vouchers.Include(v => v.Lines).FirstOrDefault(v => v.Id == voucherId);
            if (voucher is null) { Inform("That voucher is no longer there; the list will be loaded again."); _voucher = null; Reload(); return; }
            ShowVoucher(voucher, voucher.Lines.OrderBy(l => l.LineNumber).ToList());
            _loading = true;
            SelectInList(voucherId);
            _listId = voucherId;
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not open the voucher.", ex);
        }
        finally { _loading = false; }
    }

    /// <summary>Shows a voucher (or nothing) and its lines.</summary>
    private void ShowVoucher(Voucher? voucher, IReadOnlyList<VoucherLine> lines)
    {
        _loading = true;
        try
        {
            _voucher = voucher;
            _removed.Clear();
            _dirty = false;
            _no.Text = voucher?.VoucherNo.ToString() ?? "";
            _type.Text = voucher is null ? "" : Types.FirstOrDefault(t => t.Type == voucher.VoucherType).Text ?? voucher.VoucherType;
            _date.Text = voucher?.VoucherDate.ToString("dd/MM/yyyy") ?? "";
            _folio.Text = voucher?.Folio?.ToString() ?? "";
            _narration.Text = voucher?.Narration ?? "";

            var rows = lines.Select(l => new LineRow(l, _accounts)).ToList();
            _list = new FilterBindingList<LineRow>(rows);
            _list.AddingNew += (_, e) => e.NewObject = new LineRow(new VoucherLine { Source = "Typed" }, _accounts);
            _list.ItemRemoved += r =>
            {
                if (r.Line.Id != 0) _removed.Add(r.Line);
                if (!_loading) { _dirty |= !r.IsBlank || r.Line.Id != 0; ShowTotals(); }
            };
            _grid.Bind(_list);

            var editable = Editable;
            _grid.ReadOnly = !editable;
            _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = editable;
            _date.ReadOnly = _folio.ReadOnly = _narration.ReadOnly = !editable || voucher?.VoucherType == "Opening" && voucher.Id != 0;
            _save.Enabled = _cancel.Enabled = editable;
            _print.Enabled = voucher is not null;
            _lock.Enabled = voucher is { Id: > 0 } && _canEdit && _year is { IsClosed: false };
            _lock.Text = voucher is not null && IsLocked(voucher.VoucherDate) ? "Unlock this day" : "Lock this day";
            _previousCarry = voucher is not null && HasCarryLines ? PreviousCarry(voucher.VoucherDate, voucher.Id) : null;
        }
        finally { _loading = false; }
        FilterLines();
        ShowState();
        ShowTotals();
    }

    // ----- State -----

    private bool IsLocked(DateTime date) => _year is { } y && (y.IsClosed || y.LockedUpTo is { } l && date.Date <= l.Date);

    /// <summary>Whether this user may change the voucher: the Accountant or an Admin, and only an Admin on a locked day.</summary>
    private bool Editable => _voucher is not null && _canEdit && _year is { IsClosed: false } && (!IsLocked(_voucher.VoucherDate) || _isAdmin);

    private void ShowState()
    {
        if (_voucher is null)
        {
            _state.Text = _year is null ? "No financial year has been set up." : "No voucher chosen. Press New voucher to start one.";
            _state.ForeColor = Theme.Muted;
            return;
        }
        var locked = IsLocked(_voucher.VoucherDate);
        (_state.Text, _state.ForeColor) = (locked, _year?.IsClosed == true, _canEdit) switch
        {
            (_, true, _) => ($"The financial year {_year!.Name} is closed: nothing can be changed.", Theme.Warning),
            (true, _, _) when _isAdmin => ($"Locked up to {_year!.LockedUpTo:dd/MM/yyyy}. You are an Admin, so you can still change it.", Theme.Warning),
            (true, _, _) => ($"Locked up to {_year!.LockedUpTo:dd/MM/yyyy}: only an Admin can change this day.", Theme.Warning),
            (_, _, false) => ("Read only: only the Accountant or an Admin changes vouchers.", Theme.Muted),
            _ => (_voucher.Id == 0 ? "New voucher · not saved yet" : "Open · edits allowed", Theme.Good),
        };
        if (_voucher.VoucherType == "Daily" && !HasCarryLines && _voucher.Lines.Count > 0)
            _state.Text += "  ·  Imported from Access: cash in hand b/f and c/f are ordinary lines here.";
    }

    private bool CanEditCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || _grid.Rows[rowIndex].DataBoundItem is not LineRow row) return true;
        if (!row.IsFixed) return true;
        var prop = _grid.Columns[columnIndex].DataPropertyName;
        return !row.IsAuto && prop is nameof(LineRow.IsOk) or nameof(LineRow.IsCleared) || row.IsAuto && prop == nameof(LineRow.IsOk);
    }

    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count || _grid.Rows[e.RowIndex].DataBoundItem is not LineRow row || e.CellStyle is null) return;
        if (row.IsFixed) e.CellStyle.BackColor = Theme.Mix(Theme.Primary, Theme.CardBack, 0.92);
        if (row.IsAuto) e.CellStyle.ForeColor = Theme.Muted;
    }

    /// <summary>Amounts may be typed with commas ("12,800"); dates as dd/mm/yyyy.</summary>
    private void ParseCell(object? sender, DataGridViewCellParsingEventArgs e)
    {
        var prop = _grid.Columns[e.ColumnIndex].DataPropertyName;
        var text = (e.Value as string)?.Trim() ?? "";
        if (prop is nameof(LineRow.Debit) or nameof(LineRow.Credit))
        {
            if (text.Length == 0) { e.Value = null; e.ParsingApplied = true; }
            else if (ColumnFilter.ParseNumber(text) is { } n) { e.Value = n; e.ParsingApplied = true; }
        }
        else if (prop == nameof(LineRow.ChequeDate))
        {
            if (text.Length == 0) { e.Value = null; e.ParsingApplied = true; }
            else if (ColumnFilter.ParseDate(text) is { } d) { e.Value = d; e.ParsingApplied = true; }
        }
    }

    // ----- Lines: search and totals -----

    private void FilterLines()
    {
        if (_list is null) return;
        var term = _lineSearch.Text.Trim();
        _list.SetSearch(term.Length == 0 ? null : r => r.Line.Id == 0 && r.IsBlank
            || r.AccountNo.Contains(term, StringComparison.OrdinalIgnoreCase)
            || r.AccountName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || r.Description?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true
            || r.BillNo?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
            || r.ChequeNo?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
        ShowLineInfo();
    }

    private void ShowLineInfo()
    {
        if (_list is null) return;
        var total = _list.AllItems.Count(r => !r.IsBlank);
        var shown = _list.Count(r => !r.IsBlank);
        _lineInfo.Text = shown == total ? $"Showing all {total:N0} lines · click ▾ on a column to sort or filter" : $"Showing {shown:N0} of {total:N0} lines";
        RaiseStatusChanged();
    }

    /// <summary>Cash in hand lines worked out by the app: brought forward (first) and carried forward (last).</summary>
    private (LineRow? BroughtForward, LineRow? CarriedForward) CarryLines()
    {
        var autos = _list?.AllItems.Where(r => r.IsAuto).ToList() ?? [];
        return autos.Count >= 2 ? (autos[0], autos[^1]) : (null, null);
    }

    private bool HasCarryLines => _voucher?.VoucherType == "Daily" && CarryLines().CarriedForward is not null;

    /// <summary>Numbers the lines, works out the cash carried forward and shows the totals.</summary>
    private void ShowTotals()
    {
        if (_list is null) return;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            var (bf, cf) = HasCarryLines ? CarryLines() : (null, null);
            var n = 0;
            foreach (var r in Ordered()) r.No = r.IsBlank ? null : ++n;
            if (cf is not null)
            {
                var net = _list.AllItems.Where(r => r != cf).Sum(r => r.Line.Credit - r.Line.Debit);
                if (cf.Line.Debit - cf.Line.Credit != net && Editable) cf.SetNet(net);
            }
            var debits = _list.AllItems.Sum(r => r.Line.Debit);
            var credits = _list.AllItems.Sum(r => r.Line.Credit);
            var parts = new List<string>();
            if (bf is not null) parts.Add($"Cash in hand b/f {Signed(bf.Line.Credit - bf.Line.Debit)}");
            parts.Add($"Debits (D) {debits:#,##0.##}");
            parts.Add($"Credits (W) {credits:#,##0.##}");
            if (cf is not null) parts.Add($"Cash in hand c/f {Signed(cf.Line.Debit - cf.Line.Credit)}");
            _totals.Text = string.Join("   ·   ", parts);

            var diff = debits - credits;
            var notes = new List<string>();
            if (diff == 0) notes.Add("Balanced");
            else notes.Add($"Not balanced · difference {Math.Abs(diff):#,##0.##} {(diff > 0 ? "more debit" : "more credit")}");
            if (cf is not null && cf.Line.Credit > 0) notes.Add("cash in hand would go below zero");
            if (bf is not null && _previousCarry is { } p && p != bf.Line.Credit - bf.Line.Debit && Editable)
                notes.Add($"the day before carried forward {Signed(p)}; Save updates b/f");
            _balanced.Text = string.Join(" · ", notes);
            _balanced.ForeColor = notes.Count == 1 && diff == 0 ? Theme.Good : Theme.Warning;
            _grid.Invalidate();
        }
        finally { _loading = wasLoading; }
        ShowLineInfo();
    }

    private static string Signed(decimal amount) => amount < 0 ? $"-{Math.Abs(amount):#,##0.##}" : $"{amount:#,##0.##}";

    /// <summary>The lines in saving order: brought forward first, carried forward last, blank lines left out.</summary>
    private List<LineRow> Ordered()
    {
        var rows = _list?.AllItems.ToList() ?? [];
        var (bf, cf) = HasCarryLines ? CarryLines() : (null, null);
        var middle = rows.Where(r => r != bf && r != cf).ToList();
        var result = new List<LineRow>();
        if (bf is not null) result.Add(bf);
        result.AddRange(middle);
        if (cf is not null) result.Add(cf);
        return result;
    }

    /// <summary>
    /// Cash in hand carried forward by the last daily voucher before this date: its carried-forward line, or for a day imported from
    /// Access its last cash in hand debit ("Closing Balance"). Zero when there is no earlier day.
    /// </summary>
    private decimal PreviousCarry(DateTime date, int voucherId)
    {
        if (_db is null || _cash is null) return 0;
        var prev = _db.Vouchers.AsNoTracking().Where(v => v.VoucherType == "Daily" && v.VoucherDate < date.Date && v.Id != voucherId)
            .OrderByDescending(v => v.VoucherDate).Select(v => (int?)v.Id).FirstOrDefault();
        if (prev is null) return 0;
        var cashId = _cash.Id;
        var lines = _db.VoucherLines.AsNoTracking().Where(l => l.VoucherId == prev && l.AccountId == cashId).OrderBy(l => l.LineNumber).ToList();
        var auto = lines.LastOrDefault(l => l.Source == "Auto");
        if (auto is not null) return auto.Debit - auto.Credit;
        return lines.LastOrDefault(l => l.Debit > 0)?.Debit ?? 0;
    }

    private void DateChanged()
    {
        if (_loading || _voucher is null || _voucher.VoucherType != "Daily") return;
        if (ColumnFilter.ParseDate(_date.Text) is not { } date) return;
        var old = _voucher.VoucherDate;
        if (date == old) return;
        if (_narration.Text == $"Daily cash sheet {old:dd/MM/yyyy}") _narration.Text = $"Daily cash sheet {date:dd/MM/yyyy}";
        if (HasCarryLines) { _previousCarry = PreviousCarry(date, _voucher.Id); ShowTotals(); }
    }

    // ----- Records -----

    protected override void CommitPendingEdits()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        FindForm()?.Validate();
    }

    public override void NewRecord()
    {
        if (!_canEdit) return;
        if (_year is not { IsClosed: false } year || _db is null) { Inform("There is no open financial year to add a voucher to."); return; }
        if (!ConfirmLeave()) return;
        NewContext();
        var type = _selectedType;
        if (type == "Opening" && _db!.Vouchers.Any(v => v.YearId == year.Id && v.VoucherType == "Opening"))
        {
            Inform($"{year.Name} already has its opening voucher; open it from the list to change it.");
            return;
        }

        var lastDaily = _db!.Vouchers.AsNoTracking().Where(v => v.YearId == year.Id && v.VoucherType == "Daily")
            .OrderByDescending(v => v.VoucherDate).Select(v => new { v.VoucherDate, v.Folio }).FirstOrDefault();
        var date = type switch
        {
            "Daily" when lastDaily is not null => NextWorkingDay(lastDaily.VoucherDate),
            "Opening" => year.StartDate,
            _ => DateTime.Today,
        };
        if (date < year.StartDate) date = year.StartDate;
        if (date > year.EndDate) date = year.EndDate;
        var voucher = new Voucher
        {
            YearId = year.Id, VoucherType = type, VoucherDate = date,
            VoucherNo = (_db.Vouchers.Where(v => v.YearId == year.Id).Max(v => (int?)v.VoucherNo) ?? 0) + 1,
            Folio = type == "Daily" ? lastDaily?.Folio : null,
            Narration = type switch { "Daily" => $"Daily cash sheet {date:dd/MM/yyyy}", "Opening" => $"Opening balances {year.Name}", _ => null },
        };
        var lines = new List<VoucherLine>();
        if (type == "Daily" && _cash is not null)
        {
            var carry = PreviousCarry(date, 0);
            lines.Add(new VoucherLine { AccountId = _cash.Id, Description = "Opening balance (brought forward)", Source = "Auto", Debit = Math.Max(-carry, 0), Credit = Math.Max(carry, 0), IsOk = true });
            lines.Add(new VoucherLine { AccountId = _cash.Id, Description = "Closing balance (carried forward)", Source = "Auto", Debit = Math.Max(carry, 0), Credit = Math.Max(-carry, 0), IsOk = true });
        }
        ShowVoucher(voucher, lines);
        _dirty = true;
        RaiseStatusChanged();
        _grid.Focus();
        if (_grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.IsNewRow) is { } newRow && _grid.Columns.Count > 1)
            _grid.CurrentCell = newRow.Cells[1];
    }

    private static DateTime NextWorkingDay(DateTime date)
    {
        var next = date.Date.AddDays(1);
        return next.DayOfWeek == DayOfWeek.Sunday ? next.AddDays(1) : next;
    }

    public override bool SaveChanges()
    {
        if (_db is null || _voucher is null || _loading || !_canEdit) return true;
        CommitPendingEdits();
        if (!HasChanges) return true;
        if (!Editable) { Inform("This day is locked; only an Admin can change it."); return false; }
        var v = _voucher;

        if (Problem() is { } problem)
        {
            Inform(problem);
            return false;
        }

        // Header.
        var date = ColumnFilter.ParseDate(_date.Text)!.Value;
        v.VoucherDate = date;
        v.Folio = int.TryParse(_folio.Text.Trim(), out var folio) ? folio : null;
        v.Narration = string.IsNullOrWhiteSpace(_narration.Text) ? null : _narration.Text.Trim();

        // Lines: blank ones dropped, cash brought forward from the day before, then numbered.
        foreach (var blank in _list!.AllItems.Where(r => r.IsBlank && !r.IsFixed).ToList())
            _list.Remove(blank);
        var (bf, _) = HasCarryLines ? CarryLines() : (null, null);
        if (bf is not null)
        {
            var carry = PreviousCarry(date, v.Id);
            bf.SetNet(-carry);
            _previousCarry = carry;
        }
        ShowTotals();
        var rows = Ordered();
        if (rows.Sum(r => r.Line.Debit) != rows.Sum(r => r.Line.Credit))
        {
            Inform($"The voucher does not balance: debits {rows.Sum(r => r.Line.Debit):#,##0.##}, credits {rows.Sum(r => r.Line.Credit):#,##0.##}.");
            return false;
        }

        var n = 0;
        foreach (var r in rows)
        {
            r.Line.LineNumber = ++n;
            if (r.Line.Id == 0 && !v.Lines.Contains(r.Line)) v.Lines.Add(r.Line);
        }
        foreach (var line in _removed.Where(l => l.Id != 0)) _db.VoucherLines.Remove(line);

        var now = DateTime.Now;
        var user = Ctx.Session.UserName;
        _db.ChangeTracker.DetectChanges();
        foreach (var line in v.Lines)
        {
            if (line.Id == 0) (line.EnteredBy, line.EnteredOn) = (user, now);
            else if (_db.Entry(line) is { State: EntityState.Modified } entry
                     && entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(VoucherLine.LineNumber)))
                (line.ChangedBy, line.ChangedOn) = (user, now);
        }
        if (v.Id == 0)
        {
            (v.CreatedBy, v.CreatedOn) = (Ctx.Session.UserName, now);
            _db.Vouchers.Add(v);
        }
        else (v.UpdatedBy, v.UpdatedOn) = (Ctx.Session.UserName, now);

        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            Inform("Someone else changed this voucher meanwhile. Your changes were not saved; it will be loaded again.");
            _dirty = false;
            _removed.Clear();
            _db.ChangeTracker.Clear();
            var id = v.Id;
            _voucher = null;
            Reload();
            if (id > 0) Open(id);
            return false;
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save the voucher.", ex); return false; }

        _dirty = false;
        _removed.Clear();
        var savedId = v.Id;
        var wasLoading = _loading;
        _loading = true;
        try { LoadVoucherList(); }
        finally { _loading = wasLoading; }
        Open(savedId);
        return true;
    }

    /// <summary>What stops the voucher from being saved, or null.</summary>
    private string? Problem()
    {
        var v = _voucher!;
        if (ColumnFilter.ParseDate(_date.Text) is not { } date) return "Type the date as dd/mm/yyyy.";
        if (_year is { } y && (date < y.StartDate || date > y.EndDate))
            return $"{date:dd/MM/yyyy} is not in the financial year {y.Name} ({y.StartDate:dd/MM/yyyy} to {y.EndDate:dd/MM/yyyy}).";
        if (IsLocked(date) && !_isAdmin) return $"{date:dd/MM/yyyy} is in the locked days; only an Admin can add or move a voucher there.";
        if (_folio.Text.Trim() is { Length: > 0 } folio && !int.TryParse(folio, out _)) return "The folio is the page number: digits only.";
        if (v.VoucherType == "Daily"
            && _db!.Vouchers.AsNoTracking().Any(o => o.VoucherType == "Daily" && o.VoucherDate == date && o.Id != v.Id))
            return $"There is already a daily cash voucher for {date:dd/MM/yyyy}. A day has one daily voucher: open it from the list.";

        var n = 0;
        foreach (var r in Ordered())
        {
            if (r.IsBlank) continue;
            n++;
            if (r.Line.AccountId == 0) return $"Line {n} needs an account.";
            if (r.Line.Debit == 0 && r.Line.Credit == 0 && !r.IsAuto) return $"Line {n} ({r.AccountName}) needs a debit or a credit amount.";
        }
        if (n == 0) return "The voucher has no lines.";
        return null;
    }

    private void CancelChanges()
    {
        if (_voucher is null) return;
        if (HasChanges && KryptonMessageBox.Show(this, "Undo your changes to this voucher?", Title, KryptonMessageBoxButtons.YesNo,
                KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _dirty = false;
        _removed.Clear();
        var id = _voucher.Id;
        _voucher = null;
        if (id > 0) Open(id);
        else { _listId = null; OpenSelected(); }
    }

    /// <summary>Deletes the current line (not cash carried forward or posted fees).</summary>
    public override void DeleteRecord()
    {
        if (!Editable || _list is null || _grid.CurrentRow is not { IsNewRow: false } gridRow || gridRow.DataBoundItem is not LineRow row) return;
        if (row.IsFixed) { Inform("Cash brought or carried forward and posted fee lines cannot be deleted here."); return; }
        if (KryptonMessageBox.Show(this, $"Delete line {row.No}{(row.AccountName.Length > 0 ? $" ({row.AccountName})" : "")}? It is removed when you press Save.",
                Title, KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _list.Remove(row);
        _dirty = true;
        ShowTotals();
    }

    private void DeleteVoucher()
    {
        if (_voucher is null || _db is null || !_canEdit) return;
        if (!Editable) { Inform("This day is locked; only an Admin can delete it."); return; }
        if (_voucher.Id == 0) { CancelChanges(); return; }
        var v = _voucher;
        if (KryptonMessageBox.Show(this, $"Delete voucher {v.VoucherNo} of {v.VoucherDate:dd/MM/yyyy} with all its {v.Lines.Count} lines? This cannot be undone.",
                Title, KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            _db.Vouchers.Remove(v);
            _db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not delete the voucher.", ex); }
        _dirty = false;
        _removed.Clear();
        _voucher = null;
        Reload();
    }

    /// <summary>
    /// Locks every voucher up to this day (the Accountant or an Admin), or for an Admin unlocks this day and the days after it.
    /// </summary>
    private void LockDay()
    {
        if (_voucher is not { Id: > 0 } v || _year is not { IsClosed: false } year || !_canEdit || _db is null) return;
        if (!ConfirmLeave()) return;
        var date = v.VoucherDate.Date;
        if (!IsLocked(date))
        {
            if (KryptonMessageBox.Show(this, $"Lock every voucher up to {date:dd/MM/yyyy}? After this only an Admin can change them.", Title,
                    KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
                return;
            year.LockedUpTo = date;
        }
        else
        {
            if (!_isAdmin) { Inform("Only an Admin can unlock a day."); return; }
            if (KryptonMessageBox.Show(this, $"Unlock {date:dd/MM/yyyy} and the days after it?", Title,
                    KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes)
                return;
            var before = date.AddDays(-1);
            year.LockedUpTo = before < year.StartDate ? null : before;
        }
        try { _db.SaveChanges(); }
        catch (DbUpdateException ex) { Program.ShowError("Could not change the lock.", ex); }
        var id = v.Id;
        _voucher = null;
        Reload();
        Open(id);
    }

    public override void SortAscending() => (_grid.ContainsFocus ? _grid : _vgrid).SortSelectedColumn(ListSortDirection.Ascending);
    public override void SortDescending() => (_grid.ContainsFocus ? _grid : _vgrid).SortSelectedColumn(ListSortDirection.Descending);
    public override void FocusSearch() => _lineSearch.Focus();

    public override void ClearFilters()
    {
        _lineSearch.Text = "";
        _grid.ClearAllFilters();
        FilterLines();
    }

    // ----- Output -----

    private void PrintVoucher()
    {
        if (_voucher is null || _list is null) return;
        CommitPendingEdits();
        var v = _voucher;
        var date = ColumnFilter.ParseDate(_date.Text) ?? v.VoucherDate;
        var lines = Ordered().Where(r => !r.IsBlank).Select((r, i) => new Printing.VoucherPrintLine(i + 1, r.AccountNo, r.AccountName, r.Description,
            r.BillNo, r.ChequeNo, r.ChequeDate, r.Line.Debit, r.Line.Credit)).ToList();
        var type = _type.Text;
        var school = SchoolDetails();
        var name = $"{type} voucher {v.VoucherNo} {date:yyyy-MM-dd}";
        var folio = int.TryParse(_folio.Text, out var fo) ? fo : (int?)null;
        if (v.VoucherType == "Daily")
        {
            name = $"Cash book {date:yyyy-MM-dd}";
            PrintPreviewForm.Show(this, () => Printing.VoucherPrints.CashBook(date, folio, lines), name, name);
            return;
        }
        PrintPreviewForm.Show(this, () => Printing.VoucherPrints.Create(type, v.VoucherNo, date, int.TryParse(_folio.Text, out var f) ? f : null,
            _narration.Text, lines, _year?.Name ?? "", PrintKit.DefaultSchoolName, school), name, name);
    }

    private void ExportExcel()
    {
        if (_voucher is null) return;
        var title = $"Voucher {_voucher.VoucherNo} {_voucher.VoucherDate:yyyy-MM-dd}";
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"{title}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { Printing.ListExport.From(_grid, title).SaveExcel(dialog.FileName); }
        catch (IOException ex) { Program.ShowError("Could not save the Excel file. Is it open in Excel?", ex); }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, Title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}
