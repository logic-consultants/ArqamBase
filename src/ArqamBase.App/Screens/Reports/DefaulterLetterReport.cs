using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Families with unpaid fees, one row each, and the letter to them (replaces LetterUnpaid, UnpaidLetter, rpt_TopDefaulters_Notice,
/// StruckOff Notice): a fee reminder, or a struck-off notice with a last date. Print preview gives the letters or the list.
/// </summary>
public sealed class DefaulterLetterReport : ReportView
{
    public sealed class Row
    {
        public int? FamilyId { get; init; }
        public string FatherName { get; init; } = "";
        public int Children { get; init; }
        public string Names { get; init; } = "";
        public int Months { get; init; }
        public int Total { get; init; }
        public DateTime? LastPaid { get; init; }
        public string? Mobile { get; init; }
        public string? PhoneHome { get; init; }
        public string? Address { get; init; }
        internal List<DefaulterChild> Dues { get; init; } = [];
    }

    private sealed record Charge(int Cc, string Name, int? FamilyId, string Father, int? ClassId, int? SectionId,
        string? Mobile, string? PhoneHome, string? Address, string Period, DateTime? IssueDate, int Total);

    private readonly ClassFilter _classes = new();
    private readonly ComboBox _notice = Combo(160);
    private readonly KryptonDateTimePicker _lastDate = DatePicker();
    private readonly ComboBox _print = Combo(140);
    private readonly NumericUpDown _months = new() { Width = 60, Minimum = 1, Maximum = 120, Value = 1, Margin = new Padding(4, 3, 0, 0) };
    private readonly NumericUpDown _minimum = new() { Width = 90, Maximum = 10_000_000, Increment = 1000, ThousandsSeparator = true, Margin = new Padding(4, 3, 0, 0) };
    private readonly NumericUpDown _top = new() { Width = 70, Maximum = 10000, Margin = new Padding(4, 3, 0, 0) };
    private List<Charge> _charges = [];
    private Dictionary<int, DateTime> _lastPaid = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public DefaulterLetterReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<bool>(_notice, null, [(false, "Fee reminder"), (true, "Struck-off notice")]);
        Fill<bool>(_print, null, [(true, "Print letters"), (false, "Print list")]);
        _filling = false;
        _lastDate.Value = DateTime.Today.AddDays(7);
        _lastDate.Enabled = false;
        Filters.Controls.AddRange([.. _classes.Controls, Caption("Months at least"), _months, Caption("Rs at least"), _minimum, Caption("Top"), _top,
            _notice, Caption("Last date"), _lastDate, _print]);
        AddShowButton();
        _classes.Changed += (_, _) => Apply();
        _months.ValueChanged += (_, _) => Apply();
        _minimum.ValueChanged += (_, _) => Apply();
        _top.ValueChanged += (_, _) => Apply();
        _notice.SelectedIndexChanged += (_, _) => _lastDate.Enabled = Selected<bool>(_notice);
        Hint = "Families of students on roll with unpaid challans, biggest dues first. Print preview gives one letter per family shown (or the list). " +
               "Months and Rs at least are on the family's total; Top 0 = every family.";
    }

    public override string Title => "Defaulter letter";
    public override string Replaces => "LetterUnpaid, UnpaidLetter, rpt_TopDefaulters_Notice, StruckOff Notice";

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<Row>().ToList() ?? [];
            return $"{rows.Count:N0} families · {rows.Sum(r => r.Children):N0} students · {rows.Sum(r => r.Months):N0} challans · Rs {rows.Sum(r => r.Total):N0} unpaid";
        }
    }

    protected override string FilterText
    {
        get
        {
            var parts = _classes.TextParts().ToList();
            if (_months.Value > 1) parts.Add($"At least {_months.Value:N0} months");
            if (_minimum.Value > 0) parts.Add($"At least Rs {_minimum.Value:N0}");
            if (_top.Value > 0) parts.Add($"Top {_top.Value:N0}");
            return string.Join(" · ", parts.DefaultIfEmpty("All families"));
        }
    }

    protected override int? PrintSchoolId => _classes.SchoolId;
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [];

    protected override async Task LoadAsync()
    {
        var (lookup, charges, lastPaid) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var lookup = ClassLookup.Read(db).For(Ctx.Session);
            var charges = (from c in db.FeeChallans.AsNoTracking()
                           join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                           where !c.Paid && s.OnRoll
                           select new { c, s, s.Family }).AsEnumerable()
                .Select(x => new Charge(x.s.Id, x.s.Name ?? "", x.s.FamilyId, x.Family?.FatherName ?? "", x.s.ClassId, x.s.SectionId,
                    x.Family?.Mobile, x.Family?.PhoneHome is { Length: > 0 } home ? home : x.Family?.PhoneOffice, x.Family?.CurrentAddress,
                    x.c.Period ?? "", x.c.IssueDate, x.c.Total))
                .ToList();
            var lastPaid = db.FeeChallans.AsNoTracking().Where(c => c.Paid && c.StudentId != null && c.ReceivedOn != null)
                .GroupBy(c => c.StudentId!.Value).Select(g => new { g.Key, Last = g.Max(c => c.ReceivedOn!.Value) })
                .ToDictionary(x => x.Key, x => x.Last);
            return (lookup, charges, lastPaid);
        });
        if (IsDisposed) return;
        _charges = charges;
        _lastPaid = lastPaid;
        _classes.Set(lookup);
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        // A student with no family is a family of one.
        var families = _charges.Where(c => _classes.Matches(c.ClassId, c.SectionId))
            .GroupBy(c => c.FamilyId ?? -c.Cc)
            .Select(g =>
            {
                var f = g.First();
                var dues = g.GroupBy(c => c.Cc).Select(s =>
                    {
                        var items = s.OrderBy(c => c.IssueDate).ToList();
                        return new DefaulterChild(s.Key, items[0].Name, _classes.ClassName(items[0].ClassId),
                            string.Join(", ", items.Select(c => c.Period)), items.Count, items.Sum(c => c.Total));
                    })
                    .OrderBy(c => c.Name).ToList();
                var paid = dues.Select(d => _lastPaid.TryGetValue(d.Cc, out var p) ? p : (DateTime?)null).Max();
                return new Row
                {
                    FamilyId = f.FamilyId, FatherName = f.Father, Children = dues.Count, Names = string.Join(", ", dues.Select(d => d.Name)),
                    Months = dues.Sum(d => d.Months), Total = dues.Sum(d => d.Total), LastPaid = paid,
                    Mobile = f.Mobile, PhoneHome = f.PhoneHome, Address = f.Address, Dues = dues,
                };
            })
            .Where(r => r.Months >= _months.Value && r.Total >= _minimum.Value)
            .OrderByDescending(r => r.Total).ThenBy(r => r.FamilyId)
            .ToList();
        if (_top.Value > 0) families = families.Take((int)_top.Value).ToList();

        if (Grid.Columns.Count == 0) BuildColumns();
        _list = new FilterBindingList<Row>(families);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    /// <summary>The letters of the families shown (in the grid's order), or the list.</summary>
    public override void Preview()
    {
        if (!Selected<bool>(_print)) { base.Preview(); return; }
        var rows = Grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).Select(r => r.DataBoundItem).OfType<Row>().ToList();
        if (rows.Count == 0) { Inform("There are no families to write to. Change the filters and click Show."); return; }
        var letters = rows.Select(r => new DefaulterLetter(r.FamilyId, r.FatherName, r.Address, r.Dues)).ToList();
        string name;
        SchoolPrint school;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            school = SchoolPrint.From(db.Campuses.AsNoTracking().OrderBy(c => c.Id).FirstOrDefault());
            name = PrintHeader(PrintSchoolId).Name;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not load the school details.", ex); return; }
        var lastDate = Selected<bool>(_notice) ? _lastDate.Value.Date : (DateTime?)null;
        var kind = lastDate is null ? "Fee reminders" : "Struck-off notices";
        Shell.PrintPreviewForm.Show(this, () => DefaulterLetters.Create(letters, name, school, lastDate),
            $"{kind} · {letters.Count} families", $"{kind} {DateTime.Today:yyyy-MM-dd}");
    }

    private void BuildColumns()
    {
        Grid.AddText(nameof(Row.FamilyId), "Family", 60);
        Grid.AddText(nameof(Row.FatherName), "Father name", 160, fill: true);
        AddNumber(nameof(Row.Children), "Children", 70);
        Grid.AddText(nameof(Row.Names), "Students", 220, fill: true);
        AddNumber(nameof(Row.Months), "Challans", 70);
        AddNumber(nameof(Row.Total), "Total", 90);
        Grid.AddText(nameof(Row.LastPaid), "Last paid", 90, format: "dd/MM/yyyy");
        foreach (var (p, h) in new[] { (nameof(Row.Mobile), "Mobile"), (nameof(Row.PhoneHome), "Phone") })
        {
            Grid.AddText(p, h, 115);
            Grid.FormatNumbers(p, NumberFormat.Phone);
        }
    }
}
