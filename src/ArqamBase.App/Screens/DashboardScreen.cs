using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// Start page: the day's figures, fee received each day of the month, what needs attention, the latest receipts and today's
/// attendance registers. Each part shows only if the user's role can see that area. Loads in the background; Refresh reloads.
/// </summary>
public sealed class DashboardScreen : ScreenBase
{
    private readonly bool _students, _fees, _attendance, _staff;
    private readonly HashSet<string> _canOpen;
    private readonly KryptonComboBox _school = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly KryptonComboBox _month = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly KryptonLabel _updated = new() { AutoSize = false, Width = 120, Height = 24, StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly DashTile _onRoll, _today, _monthTile, _unpaid, _present, _staffTile;
    private readonly DayBarChart _chart = new() { Dock = DockStyle.Top };
    private readonly Label _facts = new() { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(0, 8, 0, 0) };
    private readonly AttentionList _todo = new() { Dock = DockStyle.Top };
    private readonly AccessGrid _receipts = Grid();
    private readonly AccessGrid _registers = Grid();
    private readonly Label _registersInfo = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    private readonly Card _feeCard;
    private CancellationTokenSource? _loading;
    private bool _ready;

    public DashboardScreen(ScreenContext ctx) : base(ctx)
    {
        var s = ctx.Session;
        (_students, _fees, _attendance, _staff) = (s.CanView(AppArea.Students), s.CanView(AppArea.Fees), s.CanView(AppArea.Attendance), s.CanView(AppArea.Staff));
        _canOpen = ScreenCatalog.For(s).Where(i => i.Create is not null).Select(i => i.Key).ToHashSet();

        _onRoll = Tile("Students on roll", Icons.Person, "students");
        _today = Tile("Fee received today", Icons.Money, "ledger");
        _monthTile = Tile("Received this month", Icons.Chart, "reports");
        _unpaid = Tile("Unpaid challans", Icons.Receipt, "ledger");
        _present = Tile("Students present", Icons.Check, "attendance");
        _staffTile = Tile("Staff present", Icons.IdCard, "staffattendance");
        var tiles = new TileGrid();
        if (_students) tiles.Controls.Add(_onRoll);
        if (_fees) tiles.Controls.AddRange([_today, _monthTile, _unpaid]);
        if (_attendance) tiles.Controls.Add(_present);
        if (_staff) tiles.Controls.Add(_staffTile);

        var content = new StackPanel { Padding = new Padding(0, 0, 4, 0) };
        content.AddTop(Header(), tiles);

        _feeCard = new Card("Fee received each day");
        _feeCard.SetHeaderControl(_month);
        _feeCard.AddTop(_chart, _facts);
        var todoCard = new Card("Needs attention");
        todoCard.AddTop(_todo);
        var middle = new RatioColumns(2f, 1f);
        if (_fees) { _feeCard.TabIndex = 0; middle.Controls.Add(_feeCard); }
        todoCard.TabIndex = 1;
        middle.Controls.Add(todoCard);
        content.AddTop(middle);

        var bottom = new RatioColumns(1f, 1f) { StackBelow = 900 };
        if (_fees)
        {
            var card = new Card("Latest fee receipts");
            card.SetHeaderControl(Button("Open fee ledger", "ledger"));
            card.AddTop(_receipts);
            card.TabIndex = 0;
            bottom.Controls.Add(card);
            _receipts.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && _receipts.Rows[e.RowIndex].DataBoundItem is ReceiptRow r && _canOpen.Contains("ledger")) Ctx.OpenScreenAt("ledger", r.CC);
            };
        }
        if (_attendance)
        {
            var card = new Card("Attendance today");
            card.SetHeaderControl(Button("Mark attendance", "attendance"));
            var info = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent };
            info.Controls.Add(_registersInfo);
            card.AddTop(info, _registers);
            card.TabIndex = 1;
            bottom.Controls.Add(card);
            _registers.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || e.CellStyle is null || _registers.Rows[e.RowIndex].DataBoundItem is not RegisterRow r) return;
                if (_registers.Columns[e.ColumnIndex].DataPropertyName == nameof(RegisterRow.State))
                    e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = r.Marked ? Theme.Good : Theme.Warning;
            };
            _registers.CellPainting += PaintRate;
        }
        content.AddTop(bottom);

        var scroll = new KryptonPanel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(content);
        Controls.Add(scroll);

        LoadChoices();
        Theme.Changed += OnThemeChanged;
        _ready = true;
        _ = LoadAsync();
    }

    public override string StatusText => "Dashboard";
    public override IReadOnlyList<ScreenCommandGroup> Commands => [new("Dashboard", new ScreenCommand("Refresh", Icons.Refresh, () => _ = LoadAsync()))];
    public override void Reload() => _ = LoadAsync();

    // ----- Building -----

    private Control Header()
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        var year = DateTime.Today.Month >= 7 ? DateTime.Today.Year : DateTime.Today.Year - 1;
        var title = new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.TitlePanel, Text = $"{greeting}, {Ctx.Session.DisplayName}" };
        var date = new KryptonLabel { AutoSize = true, Text = $"{DateTime.Today:dddd, d MMMM yyyy}  ·  Academic year {year}-{(year + 1) % 100:00}", Margin = new Padding(3, 9, 3, 3) };
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        left.Controls.AddRange([title, date]);

        var refresh = new KryptonButton { Text = "Refresh", Width = 100, Height = 28, Values = { Image = Icons.Small(Icons.Refresh) } };
        refresh.Click += (_, _) => _ = LoadAsync();
        _updated.Margin = new Padding(3, 8, 3, 3);
        _school.Margin = new Padding(3, 4, 3, 3);
        refresh.Margin = new Padding(3, 3, 0, 3);
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        right.Controls.AddRange([_school, _updated, refresh]);

        // Fixed widths, so the right side never runs past the edge whatever the text says.
        var row = new Panel { Height = 36 + Card.Gap, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, Card.Gap) };
        row.Controls.Add(left);
        row.Controls.Add(right);
        row.Layout += (_, _) =>
        {
            var width = right.Controls.Cast<Control>().Where(c => c.Visible).Sum(c => c.Width + c.Margin.Horizontal);
            if (right.Width != width) right.Width = width;
        };
        return row;
    }

    private DashTile Tile(string caption, char glyph, string screen) =>
        new(caption, glyph, _canOpen.Contains(screen) ? () => Ctx.OpenScreen(screen) : null);

    private Control Button(string text, string screen)
    {
        var b = new KryptonButton { Text = text, AutoSize = true, Enabled = _canOpen.Contains(screen) };
        b.Click += (_, _) => Ctx.OpenScreen(screen);
        return b;
    }

    private static AccessGrid Grid() => new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, ShowFilters = false, RowHeadersVisible = false,
        Dock = DockStyle.Top, ScrollBars = ScrollBars.Horizontal,
    };

    /// <summary>Schools for the filter, and the last twelve months for the chart.</summary>
    private void LoadChoices()
    {
        _school.Items.Add(new Inputs.Item<int?>(null, "All schools"));
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            foreach (var s in db.Schools.AsNoTracking().Where(s => !s.IsDeleted).OrderBy(s => s.Name).ToList())
                _school.Items.Add(new Inputs.Item<int?>(s.Id, s.Name ?? s.Code ?? $"School {s.Id}"));
        }
        catch (Microsoft.Data.SqlClient.SqlException) { }
        _school.DisplayMember = nameof(Inputs.Item<int?>.Text);
        _school.SelectedIndex = 0;
        _school.Visible = _school.Items.Count > 2;
        _school.SelectedIndexChanged += (_, _) => { if (_ready) _ = LoadAsync(); };

        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < 12; i++) _month.Items.Add(new Inputs.Item<DateTime>(month.AddMonths(-i), month.AddMonths(-i).ToString("MMMM yyyy")));
        _month.DisplayMember = nameof(Inputs.Item<DateTime>.Text);
        _month.SelectedIndex = 0;
        _month.SelectedIndexChanged += (_, _) => { if (_ready) _ = LoadAsync(); };
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        _updated.StateCommon.ShortText.Color1 = Theme.Muted;
        _facts.ForeColor = _registersInfo.ForeColor = Theme.Text;
        foreach (var c in new Control[] { _onRoll, _today, _monthTile, _unpaid, _present, _staffTile }) { ((DashTile)c).SizeToFont(); c.Invalidate(); }
        _chart.Invalidate();
        _todo.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Theme.Changed -= OnThemeChanged; _loading?.Cancel(); }
        base.Dispose(disposing);
    }

    // ----- Loading -----

    private async Task LoadAsync()
    {
        _loading?.Cancel();
        var cancel = (_loading = new CancellationTokenSource()).Token;
        var school = (_school.SelectedItem as Inputs.Item<int?>)?.Value;
        var month = (_month.SelectedItem as Inputs.Item<DateTime>)?.Value ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _updated.Text = "Loading…";
        DashData data;
        try
        {
            data = await Task.Run(() => Query(school, month), cancel);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            if (cancel.IsCancellationRequested || IsDisposed) return;
            _updated.Text = "Could not load";
            Program.ShowError("Could not load the dashboard.", ex);
            return;
        }
        if (cancel.IsCancellationRequested || IsDisposed) return;
        Show(data);
        _updated.Text = $"Updated {DateTime.Now:HH:mm}";
    }

    private sealed record DashData(
        int OnRoll, int Girls, int Boys, int NewThisMonth,
        long TodayAmount, int TodayCount, long TodayCash,
        DateTime Month, long[] Days, int MonthCount, long MonthCash, long Before, string BeforeText,
        int Unpaid, int PastDue, int PastDueFamilies, long PastDueAmount,
        List<ReceiptRow> Receipts,
        List<RegisterRow> Registers, int Here, int Marks,
        int StaffOn, int StaffMarked, int StaffHere, int StaffLate, int StaffLeave,
        bool SchoolDay, string? MissingPeriod, DateTime PeriodMonth, string? SalaryNote);

    private sealed class ReceiptRow
    {
        public DateTime? Date { get; init; }
        public int? Receipt { get; init; }
        public int CC { get; init; }
        public string Student { get; init; } = "";
        public string Class { get; init; } = "";
        public int? Amount { get; init; }
        public string Mode { get; init; } = "";
    }

    private sealed class RegisterRow
    {
        public string Class { get; init; } = "";
        public string Section { get; init; } = "";
        public string Present { get; init; } = "—";
        public string Absent { get; init; } = "—";
        public int? Rate { get; init; }
        public string State { get; init; } = "";
        public bool Marked { get; init; }
        public string SortName { get; init; } = "";
    }

    /// <summary>Cash receipts have codes starting CR (CR, CRB); the rest are bank and online deposits.</summary>
    private static bool IsCash(string? code) => code?.StartsWith("CR", StringComparison.OrdinalIgnoreCase) == true;

    private DashData Query(int? schoolId, DateTime month)
    {
        var today = DateTime.Today;
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        using var db = Ctx.Config.CreateContext(Ctx.Session);

        var classes = db.Classes.AsNoTracking().Select(c => new { c.Id, c.Name, c.SortName, c.DepartmentId }).ToList();
        var schoolOf = db.Departments.AsNoTracking().Select(d => new { d.Id, d.SchoolId }).ToDictionary(d => d.Id, d => d.SchoolId);
        var classIds = schoolId is null ? null
            : classes.Where(c => c.DepartmentId is int d && schoolOf.GetValueOrDefault(d) == schoolId).Select(c => c.Id).ToList();
        var className = classes.ToDictionary(c => c.Id, c => c.Name ?? "");
        var department = classes.ToDictionary(c => c.Id, c => c.DepartmentId);

        var students = db.Students.AsNoTracking().Where(s => s.OnRoll);
        if (classIds is not null) students = students.Where(s => s.ClassId != null && classIds.Contains(s.ClassId.Value));
        var challans = db.FeeChallans.AsNoTracking().Where(c => !c.IsDeleted);
        if (classIds is not null)
            challans = challans.Where(c => db.Students.Any(s => s.Id == c.StudentId && s.ClassId != null && classIds.Contains(s.ClassId.Value)));

        int onRoll = 0, girls = 0, boys = 0, newThisMonth = 0;
        if (_students)
        {
            var bySex = students.GroupBy(s => s.Sex).Select(g => new { g.Key, Count = g.Count() }).ToList();
            onRoll = bySex.Sum(g => g.Count);
            girls = bySex.Where(g => g.Key == "FEMALE").Sum(g => g.Count);
            boys = bySex.Where(g => g.Key == "MALE").Sum(g => g.Count);
            newThisMonth = students.Count(s => s.AdmissionDate >= thisMonth);
        }

        long todayAmount = 0, todayCash = 0, monthCash = 0, before = 0;
        int todayCount = 0, monthCount = 0, unpaid = 0, pastDue = 0, pastDueFamilies = 0;
        long pastDueAmount = 0;
        var days = new long[DateTime.DaysInMonth(month.Year, month.Month)];
        var beforeText = "";
        var receipts = new List<ReceiptRow>();
        string? missingPeriod = null;
        var periodMonth = today.Day >= 20 ? thisMonth.AddMonths(1) : thisMonth;
        if (_fees)
        {
            var codes = db.CollectionTypes.AsNoTracking().Select(t => new { t.Id, t.Name, t.Code }).ToDictionary(t => t.Id);
            bool Cash(int? type) => type is int t && codes.TryGetValue(t, out var c) && IsCash(c.Code);

            var todays = challans.Where(c => c.ReceivedOn == today).Select(c => new { c.ReceivedAmount, c.CollectionTypeId }).ToList();
            todayCount = todays.Count;
            todayAmount = todays.Sum(c => (long)(c.ReceivedAmount ?? 0));
            todayCash = todays.Where(c => Cash(c.CollectionTypeId)).Sum(c => (long)(c.ReceivedAmount ?? 0));

            var end = month.AddMonths(1);
            var inMonth = challans.Where(c => c.ReceivedOn >= month && c.ReceivedOn < end)
                .Select(c => new { c.ReceivedOn, c.ReceivedAmount, c.CollectionTypeId }).ToList();
            foreach (var c in inMonth) days[c.ReceivedOn!.Value.Day - 1] += c.ReceivedAmount ?? 0;
            monthCount = inMonth.Count;
            monthCash = inMonth.Where(c => Cash(c.CollectionTypeId)).Sum(c => (long)(c.ReceivedAmount ?? 0));

            // Compare with the month before: up to the same day while the month is still running.
            var previous = month.AddMonths(-1);
            var running = month == thisMonth;
            var upTo = running ? previous.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(previous.Year, previous.Month))) : month;
            before = challans.Where(c => c.ReceivedOn >= previous && c.ReceivedOn < upTo).Sum(c => (long?)c.ReceivedAmount) ?? 0;
            beforeText = running ? $"{previous:MMMM} to the {today.Day}{Suffix(today.Day)}" : $"{previous:MMMM}";

            var open = challans.Where(c => !c.Paid)
                .Select(c => new
                {
                    c.DueDate, c.TuitionFee, c.ExamsFee, c.RegistrationFee, c.AdmissionFee, c.ProspectusFee, c.Fine, c.Other,
                    FamilyId = db.Students.Where(s => s.Id == c.StudentId).Select(s => s.FamilyId).FirstOrDefault(),
                }).ToList();
            unpaid = open.Count;
            var due = open.Where(c => c.DueDate < today).ToList();
            pastDue = due.Count;
            pastDueFamilies = due.Where(c => c.FamilyId != null).Select(c => c.FamilyId).Distinct().Count();
            pastDueAmount = due.Sum(c => (long)(c.TuitionFee ?? 0) + (c.ExamsFee ?? 0) + (c.RegistrationFee ?? 0) + (c.AdmissionFee ?? 0) + (c.ProspectusFee ?? 0) + (c.Fine ?? 0) + (c.Other ?? 0));

            receipts = challans.Where(c => c.ReceivedOn != null && c.ReceivedOn <= today && c.StudentId != null)
                .OrderByDescending(c => c.ReceivedOn).ThenByDescending(c => c.ReceiptNo).ThenByDescending(c => c.Id).Take(8)
                .Select(c => new
                {
                    c.ReceivedOn, c.ReceiptNo, StudentId = c.StudentId!.Value, c.ReceivedAmount, c.CollectionTypeId,
                    Student = db.Students.Where(s => s.Id == c.StudentId).Select(s => new { s.Name, s.ClassId }).FirstOrDefault(),
                }).ToList()
                .Select(c => new ReceiptRow
                {
                    Date = c.ReceivedOn, Receipt = c.ReceiptNo, CC = c.StudentId, Student = c.Student?.Name ?? "", Amount = c.ReceivedAmount,
                    Class = c.Student?.ClassId is int id ? className.GetValueOrDefault(id, "") : "",
                    Mode = c.CollectionTypeId is int t && codes.TryGetValue(t, out var type) ? (IsCash(type.Code) ? "Cash" : "Bank") : "",
                }).ToList();

            // Periodic fee: challans for this month (or next month from the 20th) should exist. Checked for all schools.
            var key = FeeChallanData.PeriodKey(FeeChallanData.Period(periodMonth));
            var periods = db.FeeChallans.AsNoTracking().Where(c => !c.IsDeleted && c.IssueDate >= periodMonth.AddMonths(-2))
                .Select(c => c.Period).Distinct().ToList();
            if (!periods.Any(p => FeeChallanData.PeriodKey(p) == key)) missingPeriod = FeeChallanData.Period(periodMonth);
        }

        var schoolDay = today.DayOfWeek != DayOfWeek.Sunday && !db.Calendar.AsNoTracking().Any(d => d.Date == today && d.IsHoliday);

        var registers = new List<RegisterRow>();
        int here = 0, marksCount = 0;
        if (_attendance)
        {
            var sectionName = db.Sections.AsNoTracking().Select(s => new { s.Id, s.Name }).ToDictionary(s => s.Id, s => s.Name ?? "");
            var sections = students.Where(s => s.ClassId != null && s.SectionId != null)
                .Select(s => new { ClassId = s.ClassId!.Value, SectionId = s.SectionId!.Value }).Distinct().ToList()
                .Where(s => Ctx.Session.Covers(department.GetValueOrDefault(s.ClassId), s.ClassId, s.SectionId)).ToList();
            var taken = db.AttendanceRegisters.AsNoTracking().Where(r => r.Date == today)
                .Select(r => new { r.ClassId, r.SectionId, r.IsLocked, Present = r.Marks.Count(m => m.Status == "P" || m.Status == "T"), All = r.Marks.Count() })
                .ToList();
            foreach (var s in sections)
            {
                var r = taken.FirstOrDefault(t => t.ClassId == s.ClassId && t.SectionId == s.SectionId);
                var marked = r is { All: > 0 };
                if (marked) { here += r!.Present; marksCount += r.All; }
                registers.Add(new RegisterRow
                {
                    Class = className.GetValueOrDefault(s.ClassId, ""),
                    SortName = classes.FirstOrDefault(c => c.Id == s.ClassId)?.SortName ?? "",
                    Section = sectionName.GetValueOrDefault(s.SectionId, ""),
                    Present = marked ? r!.Present.ToString() : "—",
                    Absent = marked ? (r!.All - r.Present).ToString() : "—",
                    Rate = marked ? (int)Math.Round(100.0 * r!.Present / r.All) : null,
                    State = !marked ? "Not marked" : r!.IsLocked ? "Locked" : "Saved",
                    Marked = marked,
                });
            }
            registers = registers.OrderBy(r => r.Marked).ThenBy(r => r.SortName).ThenBy(r => r.Section).ToList();
        }

        int staffOn = 0, staffMarked = 0, staffHere = 0, staffLate = 0, staffLeave = 0;
        if (_staff)
        {
            staffOn = db.Staff.AsNoTracking().Count(s => s.LeftOn == null);
            var marks = db.StaffAttendance.AsNoTracking().Where(a => a.Date == today).Select(a => a.Status).ToList();
            staffMarked = marks.Count;
            staffHere = marks.Count(s => s is "P" or "T");
            staffLate = marks.Count(s => s == "T");
            staffLeave = marks.Count(s => s == "L");
        }

        string? salaryNote = null;
        if (_staff && Ctx.Session.IsAdmin && _canOpen.Contains("salary"))
        {
            var last = thisMonth.AddMonths(-1);
            var period = Payroll.PeriodKey(last);
            var head = db.SalaryHeads.AsNoTracking().Where(h => h.Period == period).Select(h => new { h.IsFinal }).FirstOrDefault();
            salaryNote = head is null ? $"Salary sheet for {last:MMMM} not made" : !head.IsFinal ? $"Salary sheet for {last:MMMM} is open" : null;
        }

        return new DashData(onRoll, girls, boys, newThisMonth, todayAmount, todayCount, todayCash,
            month, days, monthCount, monthCash, before, beforeText, unpaid, pastDue, pastDueFamilies, pastDueAmount, receipts,
            registers, here, marksCount, staffOn, staffMarked, staffHere, staffLate, staffLeave, schoolDay, missingPeriod, periodMonth, salaryNote);
    }

    private static string Suffix(int d) => d is 11 or 12 or 13 ? "th" : (d % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

    // ----- Showing -----

    private void Show(DashData d)
    {
        var running = d.Month == new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _onRoll.SetValue($"{d.OnRoll:N0}", $"Girls {d.Girls:N0} · Boys {d.Boys:N0}" + (d.NewThisMonth > 0 ? $" · {d.NewThisMonth:N0} new" : ""));
        _today.SetValue($"Rs {d.TodayAmount:N0}", d.TodayCount == 0 ? "No receipts yet today" : $"{d.TodayCount:N0} receipts · cash Rs {d.TodayCash:N0}");
        var total = d.Days.Sum();
        var change = d.Before > 0 ? (total - d.Before) * 100.0 / d.Before : (double?)null;
        var changeText = change is double c ? $"{(c >= 0 ? "▲" : "▼")} {Math.Abs(c):N0}% on {d.Month.AddMonths(-1):MMMM}" : "";
        _monthTile.Text = running ? "Received this month" : $"Received in {d.Month:MMMM}";
        _monthTile.SetValue($"Rs {total:N0}", $"{d.MonthCount:N0} receipts" + (changeText.Length > 0 ? " · " + changeText : ""));
        _unpaid.SetValue($"{d.Unpaid:N0}", $"{d.PastDue:N0} past due · Rs {d.PastDueAmount:N0}", d.PastDue > 0 ? Theme.Warning : null);
        var taken = d.Registers.Count(r => r.Marked);
        _present.SetValue(d.Marks == 0 ? "—" : $"{100.0 * d.Here / d.Marks:N0}%", $"{taken} of {d.Registers.Count} registers marked",
            d.Marks == 0 ? null : Theme.Good);
        _staffTile.SetValue(d.StaffMarked == 0 ? "Not taken" : $"{d.StaffHere:N0} of {d.StaffOn:N0}",
            d.StaffMarked == 0 ? $"{d.StaffOn:N0} on staff" : $"{d.StaffLate:N0} late · {d.StaffLeave:N0} on leave");

        if (_fees)
        {
            _chart.SetData(d.Month, d.Days);
            var facts = $"Received Rs {total:N0} on {d.MonthCount:N0} receipts   ·   cash Rs {d.MonthCash:N0}   ·   bank and online Rs {total - d.MonthCash:N0}";
            if (d.Before > 0 || change is not null) facts += $"\n{d.BeforeText}: Rs {d.Before:N0}" + (change is double ch ? $"  ({(ch >= 0 ? "up" : "down")} {Math.Abs(ch):N0}%)" : "");
            _facts.Text = facts;
            _facts.ForeColor = Theme.Text;
            BindReceipts(d.Receipts);
        }
        if (_attendance)
        {
            _registersInfo.Text = d.Registers.Count == 0 ? "No classes to mark." : $"{taken} of {d.Registers.Count} registers marked" +
                (d.Marks > 0 ? $"  ·  {100.0 * d.Here / d.Marks:N0}% present" : "") + (d.SchoolDay ? "" : "  ·  not a school day");
            _registersInfo.ForeColor = Theme.Text;
            BindRegisters(d.Registers);
        }
        _todo.SetItems(Attention(d));
        PerformLayout();
    }

    private List<AttentionItem> Attention(DashData d)
    {
        var items = new List<AttentionItem>();
        Action? Open(string key) => _canOpen.Contains(key) ? () => Ctx.OpenScreen(key) : null;
        if (_attendance && d.SchoolDay && d.Registers.Count > 0)
        {
            var missing = d.Registers.Where(r => !r.Marked).ToList();
            items.Add(missing.Count > 0
                ? new(AttentionKind.Warning, $"{missing.Count} attendance register{(missing.Count == 1 ? "" : "s")} not marked",
                    string.Join(", ", missing.Take(3).Select(r => $"{r.Class} {r.Section}")) + (missing.Count > 3 ? $" and {missing.Count - 3} more" : ""), "Mark now", Open("attendance"))
                : new(AttentionKind.Done, "All attendance registers marked", $"{d.Registers.Count} registers", "View", Open("attendance")));
        }
        if (_fees && d.MissingPeriod is not null)
            items.Add(new(AttentionKind.Warning, $"{d.PeriodMonth:MMMM} periodic fee not generated", $"No challans for {d.MissingPeriod} yet", "Generate", Open("periodicfee")));
        if (_fees && d.PastDue > 0)
            items.Add(new(AttentionKind.Info, $"{d.PastDue:N0} challans past their due date", $"{d.PastDueFamilies:N0} families · Rs {d.PastDueAmount:N0}", "Fee ledger", Open("ledger")));
        if (d.SalaryNote is not null)
            items.Add(new(AttentionKind.Info, d.SalaryNote, "Not final yet", "Open", Open("salary")));
        if (_staff && d.SchoolDay)
            items.Add(d.StaffMarked == 0
                ? new(AttentionKind.Warning, "Staff attendance not marked", $"{d.StaffOn:N0} on staff", "Mark now", Open("staffattendance"))
                : new(AttentionKind.Done, "Staff attendance marked", $"{d.StaffHere:N0} present · {d.StaffLate:N0} late · {d.StaffLeave:N0} on leave", "View", Open("staffattendance")));
        return items;
    }

    private void BindReceipts(List<ReceiptRow> rows)
    {
        _receipts.ResetColumns();
        _receipts.AddText(nameof(ReceiptRow.Date), "Date", 85, format: "dd/MM/yyyy");
        _receipts.AddText(nameof(ReceiptRow.Receipt), "Receipt", 70);
        _receipts.AddText(nameof(ReceiptRow.CC), "CC", 60);
        _receipts.AddText(nameof(ReceiptRow.Student), "Student", 140, fill: true);
        _receipts.AddText(nameof(ReceiptRow.Class), "Class", 80);
        _receipts.AddText(nameof(ReceiptRow.Amount), "Amount", 80, format: "N0").DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _receipts.AddText(nameof(ReceiptRow.Mode), "Mode", 60);
        _receipts.Bind(new FilterBindingList<ReceiptRow>(rows));
        _receipts.ClearSelection();
        FitHeight(_receipts, 8);
    }

    private void BindRegisters(List<RegisterRow> rows)
    {
        _registers.ResetColumns();
        _registers.AddText(nameof(RegisterRow.Class), "Class", 100, fill: true);
        _registers.AddText(nameof(RegisterRow.Section), "Section", 90);
        _registers.AddText(nameof(RegisterRow.Present), "Present", 65).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _registers.AddText(nameof(RegisterRow.Absent), "Absent", 65).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _registers.AddText(nameof(RegisterRow.Rate), "Present %", 110);
        _registers.AddText(nameof(RegisterRow.State), "Status", 85);
        _registers.Bind(new FilterBindingList<RegisterRow>(rows));
        _registers.ClearSelection();
        FitHeight(_registers, 8);
    }

    /// <summary>Tall enough for the rows (at most <paramref name="max"/>, then it scrolls), so the cards size to their grids.</summary>
    private static void FitHeight(DataGridView grid, int max)
    {
        var rows = Math.Clamp(grid.Rows.Count, 1, max);
        grid.ScrollBars = grid.Rows.Count > max ? ScrollBars.Both : ScrollBars.Horizontal;
        grid.Height = grid.ColumnHeadersHeight + rows * grid.RowTemplate.Height + SystemInformation.HorizontalScrollBarHeight + 4;
    }

    /// <summary>The Present % column: a bar the length of the share present, with the figure beside it.</summary>
    private void PaintRate(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics is null || _registers.Columns[e.ColumnIndex].DataPropertyName != nameof(RegisterRow.Rate)) return;
        if (_registers.Rows[e.RowIndex].DataBoundItem is not RegisterRow { Rate: int value }) return;
        e.PaintBackground(e.ClipBounds, true);
        var font = e.CellStyle?.Font ?? _registers.Font;
        var labelWidth = TextRenderer.MeasureText("100%", font).Width;
        var track = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y + e.CellBounds.Height / 2 - 3, Math.Max(10, e.CellBounds.Width - labelWidth - 14), 6);
        using (var back = new SolidBrush(Theme.Mix(Theme.CardBorder, Theme.CardBack, 0.3))) e.Graphics.FillRectangle(back, track);
        using (var fill = new SolidBrush(Theme.Good)) e.Graphics.FillRectangle(fill, track with { Width = track.Width * value / 100 });
        TextRenderer.DrawText(e.Graphics, $"{value}%", font, new Rectangle(track.Right + 4, e.CellBounds.Y, labelWidth + 4, e.CellBounds.Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        e.Handled = true;
    }
}
