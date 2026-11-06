using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// The month's salary sheet (replaces the Excel workbook): Prepare sheet lists the staff with their salary, days absent or on leave
/// from Staff attendance, and advance instalments; everything can be changed for the month, then Finalise keeps it.
/// Prints the sheet, the bank letter and the summary.
/// </summary>
public sealed class SalarySheetScreen : ScreenBase
{
    public sealed class Row
    {
        public int StaffId { get; init; }
        public byte? Sheet { get; set; }
        public string SheetName => Payroll.SheetName(Sheet);
        public string Name { get; init; } = "";
        public string Designation { get; init; } = "";
        public string Campus { get; init; } = "";
        public DateTime? Joined { get; init; }
        public string Qualification { get; init; } = "";
        public decimal Salary { get; set; }
        public int Days { get; set; }
        public int AbsentLeave { get; set; }
        public decimal Incentive { get; set; }
        public decimal Deduction { get; set; }
        public decimal PreAdvance { get; set; }
        public decimal AdvanceDeducted { get; set; }
        public decimal AdvanceLeft => PreAdvance - AdvanceDeducted;
        public decimal Scf { get; set; }
        public decimal Security { get; set; }
        public decimal Paid => Payroll.Paid(Salary, Incentive, Deduction, AdvanceDeducted, Scf, Security);
        public string PayMethod { get; set; } = "";
        public string RemainingSecurity { get; set; } = "";
        public string Verify { get; set; } = "";
        public string BankAccount { get; set; } = "";
    }

    private const string AllSheets = "All sheets";
    private readonly ComboBox _month = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 3, 0, 0) };
    private readonly ComboBox _sheet = new() { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(8, 3, 0, 0) };
    private readonly KryptonTextBox _bankCheque = new() { Width = 110, Margin = new Padding(4, 3, 0, 0) };
    private readonly KryptonTextBox _cashCheque = new() { Width = 110, Margin = new Padding(4, 3, 0, 0) };
    private readonly KryptonLabel _banner = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6), StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly StatCard _totalCard = new("Total salary"), _deductCard = new("GP / security"), _advanceCard = new("Advance deducted"), _paidCard = new("Actual paid");
    private readonly AccessGrid _grid = new();
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private FilterBindingList<Row>? _list;
    private SalaryHead? _head;
    private bool _filling, _dirty;
    private string? _shownPeriod;

    private static readonly string[] Editable =
    [
        nameof(Row.Sheet), nameof(Row.Salary), nameof(Row.Days), nameof(Row.AbsentLeave), nameof(Row.Incentive), nameof(Row.Deduction), nameof(Row.AdvanceDeducted),
        nameof(Row.Scf), nameof(Row.Security), nameof(Row.PayMethod), nameof(Row.RemainingSecurity), nameof(Row.Verify), nameof(Row.BankAccount),
    ];

    public SalarySheetScreen(ScreenContext ctx) : base(ctx)
    {
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _filling = true;
        foreach (var m in Enumerable.Range(-1, 26).Select(i => month.AddMonths(-i))) _month.Items.Add(new Inputs.Item<string>(Payroll.PeriodKey(m), Payroll.PeriodText(m)));
        _month.DisplayMember = nameof(Inputs.Item<string>.Text);
        _month.SelectedIndex = 1;
        _sheet.Items.Add(AllSheets);
        foreach (var (_, name) in Payroll.Sheets) _sheet.Items.Add(name);
        _sheet.Items.Add(Payroll.SheetName(null));
        _sheet.SelectedIndex = 0;
        _filling = false;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent };
        bar.Controls.AddRange([Caption("Month"), _month, _sheet, Caption("Bank cheque no."), _bankCheque, Caption("Cash cheque no."), _cashCheque]);
        var stats = new EqualColumns();
        stats.Controls.AddRange([_totalCard, _deductCard, _advanceCard, _paidCard]);
        BuildGrid();
        var hint = new KryptonLabel
        {
            Dock = DockStyle.Bottom, Padding = new Padding(0, 6, 0, 0), StateCommon = { ShortText = { Color1 = Theme.Muted } },
            Text = $"D.D is a day's pay for each day of absence or leave after the first {Payroll.FreeLeaves}; it is worked out again when Salary, Days or Abs/Leave change, and you can type over it. Paid = Salary + Inc. - D.D - Adv DD - G.P - Sec.",
        };
        Controls.Add(_grid);
        Controls.Add(hint);
        Controls.Add(stats);
        Controls.Add(_banner);
        Controls.Add(bar);

        _month.SelectedIndexChanged += (_, _) => { if (!_filling) ShowMonth(); };
        _sheet.SelectedIndexChanged += (_, _) => { if (!_filling) ApplySheet(); };
        _bankCheque.TextChanged += (_, _) => Touched();
        _cashCheque.TextChanged += (_, _) => Touched();

        _commands =
        [
            new("Sheet",
                new ScreenCommand("Refresh absences", Icons.Refresh, RefreshAbsences),
                new ScreenCommand("Finalise / reopen", Icons.Lock, ToggleFinal)),
            new("Print",
                new ScreenCommand("Salary sheet", Icons.Print, PrintSheet),
                new ScreenCommand("Bank letter", Icons.Document, PrintBankLetter),
                new ScreenCommand("Summary", Icons.Book, PrintSummary),
                new ScreenCommand("Excel", Icons.Excel, ExportExcel)),
        ];
        HandleCreated += (_, _) => ShowMonth();
    }

    private static KryptonLabel Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 5, 2, 0) };

    public override bool SupportsRecords => true;
    public override string NewText => "Prepare sheet";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => _dirty;

    private string Period => ((Inputs.Item<string>)_month.SelectedItem!).Value!;
    private DateTime Month => Payroll.PeriodMonth(Period);
    private bool Final => _head is { IsFinal: true };

    public override string StatusText => _list is null ? "" : $"{_list.Count:N0} people{(_dirty ? "  ·  unsaved changes" : "")}";

    private void Touched()
    {
        if (_filling) return;
        _dirty = true;
        ShowBanner();
        RaiseStatusChanged();
    }

    public override void Reload()
    {
        if (!ConfirmLeave()) return;
        _dirty = false;
        ShowMonth();
    }

    private void ShowMonth()
    {
        if (_dirty && _shownPeriod is not null && _shownPeriod != Period)
        {
            var wanted = Period;
            _filling = true;
            _month.SelectedItem = _month.Items.Cast<Inputs.Item<string>>().First(i => i.Value == _shownPeriod);
            _filling = false;
            if (!ConfirmLeave()) return;
            _dirty = false;
            _filling = true;
            _month.SelectedItem = _month.Items.Cast<Inputs.Item<string>>().First(i => i.Value == wanted);
            _filling = false;
        }
        var period = Period;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            _head = db.SalaryHeads.AsNoTracking().Include(h => h.Lines).FirstOrDefault(h => h.Period == period);
            var staff = db.Staff.IgnoreQueryFilters().AsNoTracking().ToDictionary(s => s.Id);
            var posts = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => t.Name?.Trim() ?? "");
            var campus = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => CampusName(s.Name));
            var rows = _head?.Lines.Select(l => ToRow(l, staff.GetValueOrDefault(l.StaffId), posts, campus)).ToList() ?? [];
            Sort(rows);
            Bind(rows);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the salary sheet. Has sql/staff_payroll_install.sql been run on this database?", ex);
            return;
        }
        _filling = true;
        _bankCheque.Text = _head?.BankChequeNo ?? "";
        _cashCheque.Text = _head?.CashChequeNo ?? "";
        _filling = false;
        _shownPeriod = period;
        _dirty = false;
        ShowBanner();
    }

    internal static string CampusName(string? school) =>
        school?.Contains("GIRLS", StringComparison.OrdinalIgnoreCase) == true ? "Girls" : school?.Contains("BOYS", StringComparison.OrdinalIgnoreCase) == true ? "Boys" : school?.Trim() ?? "";

    private static void Sort(List<Row> rows) =>
        rows.Sort((a, b) => (a.Sheet ?? 99).CompareTo(b.Sheet ?? 99) is var c and not 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    private static Row ToRow(SalaryLine l, StaffMember? s, Dictionary<int, string> posts, Dictionary<int, string> campus) => new()
    {
        StaffId = l.StaffId, Sheet = l.Sheet, Name = s?.Name?.Trim() ?? "", Designation = Post(s, posts), Campus = CampusOf(s, campus),
        Joined = s?.DateHired, Qualification = s?.Education?.Trim() ?? "", Salary = l.BasicSalary ?? 0, Days = l.Days ?? 0, AbsentLeave = l.AbsentLeave ?? 0,
        Incentive = l.Incentive ?? 0, Deduction = l.Deduction ?? 0, PreAdvance = l.Advance ?? 0, AdvanceDeducted = l.AdvanceDeducted ?? 0, Scf = l.Scf ?? 0,
        Security = l.Security ?? 0, PayMethod = l.PayMethod ?? "", RemainingSecurity = l.RemainingSecurity ?? "", Verify = l.Verify ?? "", BankAccount = l.BankAccount ?? "",
    };

    private static string Post(StaffMember? s, Dictionary<int, string> posts) =>
        s?.TypeId is int t && posts.TryGetValue(t, out var p) ? p : s?.Designation?.Trim() ?? "";

    private static string CampusOf(StaffMember? s, Dictionary<int, string> campus) => s?.BranchId is int b ? campus.GetValueOrDefault(b, "") : "";

    private void Bind(List<Row> rows)
    {
        _list = new FilterBindingList<Row>(rows);
        ApplySheet();
        _grid.Bind(_list);
        _grid.ReadOnly = Final;
        UpdateTotals();
    }

    private void ApplySheet()
    {
        if (_list is null) return;
        var chosen = _sheet.SelectedItem as string;
        _list.SetSearch(r => chosen == AllSheets || r.SheetName == chosen);
        UpdateTotals();
    }

    private void ShowBanner()
    {
        var text = _head is null && _list is { AllItems.Count: 0 } ? $"No sheet for {Payroll.PeriodText(Month)} yet. Press Prepare sheet to list the staff."
            : Final ? $"Final. Prepared by {_head!.PreparedBy} on {_head.PreparedOn:dd/MM/yyyy HH:mm}. Finalise / reopen lets an Admin change it."
            : _head is null ? "Prepared, not saved. Check it, then Save."
            : $"Draft. Prepared by {_head.PreparedBy} on {_head.PreparedOn:dd/MM/yyyy HH:mm}. Prepare sheet adds anyone missing.";
        if (_dirty && _head is not null) text += "  Not saved.";
        _banner.Text = text;
    }

    private void UpdateTotals()
    {
        var rows = ShownRows();
        _totalCard.SetValue($"Rs {rows.Sum(r => r.Salary):N0}");
        _deductCard.SetValue($"Rs {rows.Sum(r => r.Scf + r.Security):N0}");
        _advanceCard.SetValue($"Rs {rows.Sum(r => r.AdvanceDeducted):N0}");
        _paidCard.SetValue($"Rs {rows.Sum(r => r.Paid):N0}", Theme.Good);
        RaiseStatusChanged();
    }

    /// <summary>The rows shown, in the grid's order (after filters and sort).</summary>
    private List<Row> ShownRows() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Select(r => r.DataBoundItem).OfType<Row>().ToList();

    /// <summary>Lists the staff not yet on the sheet: their pay from the Staff record, else last month's sheet, their absences and advance instalments.</summary>
    public override void NewRecord()
    {
        if (Final) { Inform("This month's sheet is final. Reopen it first."); return; }
        var period = Period;
        var month = Month;
        var after = month.AddMonths(1);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var listed = _list?.AllItems.Select(r => r.StaffId).ToHashSet() ?? [];
            var staff = db.Staff.AsNoTracking().Where(s => s.LeftOn == null || s.LeftOn >= month).ToList()
                .Where(s => !listed.Contains(s.Id) && (s.DateHired is null || s.DateHired < after)).ToList();
            var posts = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => t.Name?.Trim() ?? "");
            var campus = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => CampusName(s.Name));
            var absent = Absences(db, month);
            var last = db.SalaryHeads.AsNoTracking().Include(h => h.Lines).Where(h => string.Compare(h.Period, period) < 0)
                .OrderByDescending(h => h.Period).FirstOrDefault()?.Lines.ToDictionary(l => l.StaffId) ?? [];
            var deducted = db.SalaryLines.AsNoTracking().Where(l => db.SalaryHeads.Any(h => h.Id == l.HeadId && string.Compare(h.Period, period) < 0))
                .GroupBy(l => l.StaffId).Select(g => new { g.Key, Sum = g.Sum(l => l.AdvanceDeducted ?? 0) }).ToDictionary(x => x.Key, x => x.Sum);
            var deposited = db.SalaryLines.AsNoTracking().Where(l => db.SalaryHeads.Any(h => h.Id == l.HeadId && string.Compare(h.Period, period) < 0))
                .GroupBy(l => l.StaffId).Select(g => new { g.Key, Sum = g.Sum(l => l.Security ?? 0) }).ToDictionary(x => x.Key, x => x.Sum);
            var advances = db.StaffAdvances.AsNoTracking().Where(a => a.Date < after).ToList().ToLookup(a => a.StaffId);
            var rows = _list?.AllItems.ToList() ?? [];
            foreach (var s in staff)
            {
                var prev = last.GetValueOrDefault(s.Id);
                var salary = s.CurrentSalary is int cs ? cs : prev?.BasicSalary ?? 0;
                var mine = advances[s.Id].ToList();
                var before = deducted.GetValueOrDefault(s.Id);
                var security = Payroll.SecurityDue(s.Salary, s.SecDeduction ?? 0, deposited.GetValueOrDefault(s.Id));
                var securityLeft = Payroll.SecurityLeft(s.Salary, deposited.GetValueOrDefault(s.Id) + security);
                var row = new Row
                {
                    StaffId = s.Id, Sheet = s.SalarySheet ?? prev?.Sheet, Name = s.Name?.Trim() ?? "", Designation = Post(s, posts), Campus = CampusOf(s, campus),
                    Joined = s.DateHired, Qualification = s.Education?.Trim() ?? "", Salary = salary, Days = days, AbsentLeave = absent.GetValueOrDefault(s.Id),
                    PreAdvance = Math.Max(mine.Sum(a => a.Amount) - before, 0), AdvanceDeducted = Payroll.AdvanceDue(mine, before, period),
                    Scf = s.Scf ?? prev?.Scf ?? 0, Security = security, PayMethod = s.PayMethod ?? prev?.PayMethod ?? "",
                    RemainingSecurity = securityLeft ?? prev?.RemainingSecurity ?? "", BankAccount = s.BankAccount ?? prev?.BankAccount ?? "",
                };
                row.Deduction = Payroll.AbsenceDeduction(row.Salary, row.Days, row.AbsentLeave);
                rows.Add(row);
            }
            if (staff.Count == 0) { Inform("Everyone working this month is already on the sheet."); return; }
            Sort(rows);
            Bind(rows);
            Touched();
            ShowBanner();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not prepare the sheet.", ex);
        }
    }

    private static Dictionary<int, int> Absences(ArqamDbContext db, DateTime month)
    {
        var after = month.AddMonths(1);
        return db.StaffAttendance.AsNoTracking().Where(a => a.Date >= month && a.Date < after && (a.Status == "A" || a.Status == "L"))
            .GroupBy(a => a.StaffId).Select(g => new { g.Key, Count = g.Count() }).ToDictionary(x => x.Key, x => x.Count);
    }

    /// <summary>Takes the days absent or on leave from Staff attendance again, and works out D.D for those that changed.</summary>
    private void RefreshAbsences()
    {
        if (_list is null || Final) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var absent = Absences(db, Month);
            var changed = 0;
            foreach (var r in _list.AllItems)
            {
                var n = absent.GetValueOrDefault(r.StaffId);
                if (n == r.AbsentLeave) continue;
                r.AbsentLeave = n;
                r.Deduction = Payroll.AbsenceDeduction(r.Salary, r.Days, n);
                changed++;
            }
            _grid.Invalidate();
            UpdateTotals();
            if (changed > 0) Touched();
            Inform(changed == 0 ? "No changes: the sheet matches Staff attendance." : $"Updated {changed} people from Staff attendance. Press Save to keep it.");
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException) { Program.ShowError("Could not read Staff attendance.", ex); }
    }

    public override bool SaveChanges()
    {
        _grid.EndEdit();
        if (!_dirty) return true;
        if (Final || _list is null) return false;
        var period = Period;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var head = db.SalaryHeads.Include(h => h.Lines).FirstOrDefault(h => h.Period == period);
            if (head is null)
            {
                head = new SalaryHead { Period = period, SalaryDate = Month };
                db.SalaryHeads.Add(head);
            }
            else if (head.IsFinal) { Inform("This sheet was finalised meanwhile. Click Refresh."); return false; }
            head.BankChequeNo = string.IsNullOrWhiteSpace(_bankCheque.Text) ? null : _bankCheque.Text.Trim();
            head.CashChequeNo = string.IsNullOrWhiteSpace(_cashCheque.Text) ? null : _cashCheque.Text.Trim();
            head.PreparedBy = Ctx.Session.UserName;
            head.PreparedOn = DateTime.Now;
            foreach (var r in _list.AllItems)
            {
                var l = head.Lines.FirstOrDefault(x => x.StaffId == r.StaffId);
                if (l is null) head.Lines.Add(l = new SalaryLine { StaffId = r.StaffId });
                l.SalaryDate = Month;
                l.Sheet = r.Sheet;
                l.BasicSalary = r.Salary;
                l.Days = r.Days;
                l.AbsentLeave = r.AbsentLeave;
                l.Incentive = r.Incentive;
                l.Deduction = r.Deduction;
                l.Advance = r.PreAdvance;
                l.AdvanceDeducted = r.AdvanceDeducted;
                l.AdvanceBalance = r.AdvanceLeft;
                l.Scf = r.Scf;
                l.Security = r.Security;
                l.AmountPaid = r.Paid;
                l.PayMethod = Clean(r.PayMethod);
                l.BankAccount = Clean(r.BankAccount);
                l.RemainingSecurity = Clean(r.RemainingSecurity);
                l.Verify = Clean(r.Verify);
            }
            db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save the sheet. Someone may have saved this month at the same time; click Refresh and try again.", ex); return false; }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not save the sheet.", ex); return false; }
        _dirty = false;
        ShowMonth();
        return true;
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void ToggleFinal()
    {
        if (_head is null) { Inform("Save the sheet first."); return; }
        if (!ConfirmLeave()) return;
        var head = _head;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            if (head.IsFinal && db.SalaryHeads.Any(h => string.Compare(h.Period, head.Period) > 0))
            {
                Inform("A later month's sheet exists, and it was worked out from this one. Delete that sheet before reopening this one.");
                return;
            }
            var final = !head.IsFinal;
            db.SalaryHeads.Where(h => h.Id == head.Id).ExecuteUpdate(s => s.SetProperty(h => h.IsFinal, final));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not change the sheet.", ex); return; }
        ShowMonth();
    }

    public override void DeleteRecord()
    {
        if (_head is null) { Inform("This month has no saved sheet."); return; }
        if (Final) { Inform("This sheet is final. Reopen it first."); return; }
        var head = _head;
        if (KryptonMessageBox.Show($"Delete the salary sheet of {Payroll.PeriodText(Month)}?", "Delete salary sheet",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            db.SalaryHeads.Where(h => h.Id == head.Id).ExecuteDelete();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not delete the sheet.", ex); return; }
        _dirty = false;
        ShowMonth();
    }

    // ----- Printing -----

    private bool HasRows()
    {
        if (ShownRows().Count > 0) return true;
        Inform("There is nothing to print. Prepare the sheet first.");
        return false;
    }

    private void PrintSheet()
    {
        if (!HasRows()) return;
        var rows = ShownRows();
        string N(decimal v) => v == 0 ? "" : v.ToString("#,0.##");
        ReportColumn Sum(string h, float w) => new(h, w, Number: true, Sum: true);
        ReportColumn[] columns =
        [
            new("Name", 92), new("Desig.", 56), new("D.O.J", 46), new("Qual.", 52), Sum("Salary", 44), new("Day", 26, Number: true), new("Abs", 26, Number: true), Sum("Inc.", 30),
            Sum("D.D", 42), Sum("Pre Adv", 44), Sum("Adv DD", 40), Sum("Rem. Adv", 44), Sum("G.P", 26), Sum("Sec.", 34), Sum("Paid", 50), new("Paid by", 32), new("Rem. sec", 34),
            new("A/C #", 100), new("Signature", 62),
        ];
        var lines = rows.Select(r => new ReportLine(
            [r.Name, r.Designation, r.Joined?.ToString("dd/MM/yy") ?? "", r.Qualification, N(r.Salary), $"{r.Days}", $"{r.AbsentLeave}", N(r.Incentive), N(r.Deduction), N(r.PreAdvance),
                N(r.AdvanceDeducted), N(r.AdvanceLeft), N(r.Scf), N(r.Security), N(r.Paid), r.PayMethod, r.RemainingSecurity, r.BankAccount, ""],
            new Dictionary<int, decimal>
            {
                [4] = r.Salary, [7] = r.Incentive, [8] = r.Deduction, [9] = r.PreAdvance, [10] = r.AdvanceDeducted, [11] = r.AdvanceLeft, [12] = r.Scf, [13] = r.Security, [14] = r.Paid,
            },
            [r.SheetName])).ToList();
        var bank = rows.Where(r => r.PayMethod != "CASH").Sum(r => r.Paid);
        var report = new GroupedReport
        {
            Title = $"Staff salary for the month of {Payroll.PeriodText(Month)}",
            Subtitle = _head is { IsFinal: true } ? "Final" : "Draft",
            Columns = columns,
            Lines = lines,
            GroupNames = ["Sheet"],
            Landscape = true,
            Signatures = ["Checked by", "Prepared by", "Approved by"],
            Summary =
            [
                ("Total salary", $"Rs {rows.Sum(r => r.Salary):N0}"), ("GP / security", $"Rs {rows.Sum(r => r.Scf + r.Security):N0}"),
                ("Actual paid", $"Rs {rows.Sum(r => r.Paid):N2}"), ("By bank", $"Rs {bank:N2}"), ("In cash", $"Rs {rows.Where(r => r.PayMethod == "CASH").Sum(r => r.Paid):N2}"),
            ],
            Logo = SchoolDetails().Logo,
        };
        var name = $"Salary sheet {Payroll.PeriodText(Month)}";
        Shell.PrintPreviewForm.Show(this, report.ToPdf, name, name);
    }

    private void PrintBankLetter()
    {
        if (!HasRows()) return;
        var lines = ShownRows().Where(r => r.PayMethod is not ("CASH" or "") && r.Paid > 0).OrderBy(r => r.Sheet).ThenBy(r => r.Name)
            .Select(r => new BankTransferLine(r.Name, r.Designation, r.BankAccount, r.Paid)).ToList();
        if (lines.Count == 0) { Inform("No one here is paid through the bank. Choose BAHL under Paid by."); return; }
        var name = $"Bank letter {Payroll.PeriodText(Month)}";
        var school = SchoolDetails();
        Shell.PrintPreviewForm.Show(this, () => SalaryPrints.BankLetter(Payroll.PeriodText(Month), _bankCheque.Text, lines, PrintKit.DefaultSchoolName, school), name, name);
    }

    /// <summary>Cr. and Dr. of the whole month, whichever sheet is showing.</summary>
    private void PrintSummary()
    {
        var rows = _list?.AllItems.ToList() ?? [];
        if (rows.Count == 0) { Inform("There is nothing to print. Prepare the sheet first."); return; }
        var groups = rows.GroupBy(r => r.SheetName).ToList();
        var credit = new List<SummaryLine>();
        credit.AddRange(groups.Where(g => g.Sum(r => r.AdvanceDeducted) != 0).Select(g => new SummaryLine($"Adv return: {g.Key}", g.Sum(r => r.AdvanceDeducted))));
        credit.AddRange(groups.Select(g => new SummaryLine($"Bank transfer: {g.Key}", g.Where(r => r.PayMethod != "CASH").Sum(r => r.Paid))).Where(l => l.Amount != 0));
        credit.AddRange(groups.Select(g => new SummaryLine($"Cash salary: {g.Key}", g.Where(r => r.PayMethod == "CASH").Sum(r => r.Paid))).Where(l => l.Amount != 0));
        credit.Add(new SummaryLine("SCF / security (all staff)", rows.Sum(r => r.Scf + r.Security)));
        var debit = groups.Select(g => new SummaryLine(g.Key, g.Sum(r => r.Paid + r.AdvanceDeducted + r.Scf + r.Security))).ToList();
        var name = $"Salary summary {Payroll.PeriodText(Month)}";
        Shell.PrintPreviewForm.Show(this, () => SalaryPrints.Summary(Payroll.PeriodText(Month), credit, debit, PrintKit.DefaultSchoolName), name, name);
    }

    private void ExportExcel()
    {
        if (!HasRows()) return;
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"Salary {Payroll.PeriodText(Month)}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ListExport.From(_grid, $"Salary {Payroll.PeriodText(Month)}").SaveExcel(dialog.FileName); }
        catch (IOException ex) { Program.ShowError("Could not save the Excel file. Is it open in Excel?", ex); }
    }

    // ----- Grid -----

    private void BuildGrid()
    {
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.AddCombo<byte?>(nameof(Row.Sheet), "Sheet", new (byte?, string)[] { (null, "") }.Concat(Payroll.Sheets.Select(s => ((byte?)s.Id, s.Name))), 150);
        _grid.AddText(nameof(Row.Name), "Name", 170, fill: true);
        _grid.AddText(nameof(Row.Designation), "Desig.", 100);
        _grid.AddText(nameof(Row.Campus), "Campus", 60);
        _grid.AddText(nameof(Row.Joined), "D.O.J", 85, format: "dd/MM/yyyy");
        _grid.AddText(nameof(Row.Qualification), "Qual.", 90);
        Amount(nameof(Row.Salary), "Salary");
        Count(nameof(Row.Days), "Days");
        Count(nameof(Row.AbsentLeave), "Abs/Leave");
        Amount(nameof(Row.Incentive), "Inc.");
        Amount(nameof(Row.Deduction), "D.D");
        Amount(nameof(Row.PreAdvance), "Pre Adv");
        Amount(nameof(Row.AdvanceDeducted), "Adv DD");
        Amount(nameof(Row.AdvanceLeft), "Rem. Adv");
        Amount(nameof(Row.Scf), "G.P");
        Amount(nameof(Row.Security), "Sec.");
        Amount(nameof(Row.Paid), "Paid").DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        _grid.AddCombo<string>(nameof(Row.PayMethod), "Paid by", new (string, string)[] { ("", "") }.Concat(Payroll.PayMethods.Select(m => (m, m))), 75);
        _grid.AddText(nameof(Row.RemainingSecurity), "Rem. sec", 80);
        _grid.AddText(nameof(Row.Verify), "Verify", 80);
        _grid.AddText(nameof(Row.BankAccount), "A/C #", 170);
        _grid.CanEditCell = (_, column) => !Final && Editable.Contains(_grid.Columns[column].DataPropertyName);
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex >= 0 && !Editable.Contains(_grid.Columns[e.ColumnIndex].DataPropertyName)) e.CellStyle!.ForeColor = Theme.Muted;
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || _filling || _grid.Rows[e.RowIndex].DataBoundItem is not Row row) return;
            if (_grid.Columns[e.ColumnIndex].DataPropertyName is nameof(Row.Salary) or nameof(Row.Days) or nameof(Row.AbsentLeave))
                row.Deduction = Payroll.AbsenceDeduction(row.Salary, row.Days, row.AbsentLeave);
            _grid.InvalidateRow(e.RowIndex);
            UpdateTotals();
            Touched();
        };
    }

    private DataGridViewTextBoxColumn Amount(string property, string header)
    {
        var col = _grid.AddText(property, header, 85, format: "#,0.##");
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return col;
    }

    private void Count(string property, string header)
    {
        var col = _grid.AddText(property, header, 60);
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Salary sheet", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
