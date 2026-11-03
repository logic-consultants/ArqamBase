using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// The tuition fee changes saved in Fee definition and the annual increase (replaces rptFeeIncreasedLog, table tblFeeUpdateList),
/// by class and section, for a date range.
/// </summary>
public sealed class FeeIncreaseReport : ReportView
{
    public sealed class Row
    {
        public int Cc { get; init; }
        public string Name { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string SectionName { get; init; } = "";
        public string Status { get; init; } = "";
        public DateTime? UpdatedOn { get; init; }
        public int CurrentFee { get; init; }
        public int Increase { get; init; }
        public int NewFee { get; init; }
        public string Reason { get; init; } = "";
        internal string ClassSort { get; init; } = "";
    }

    private sealed record Change(int Cc, string Name, int? ClassId, int? SectionId, bool OnRoll, string Status,
        DateTime? UpdatedOn, decimal CurrentFee, decimal Increase, decimal NewFee, string Reason);

    private const string AllReasons = "All reasons";
    private readonly ClassFilter _classes = new();
    private readonly KryptonDateTimePicker _from = DatePicker();
    private readonly KryptonDateTimePicker _to = DatePicker();
    private readonly ComboBox _reason = Combo(200);
    private readonly ComboBox _group = Combo(170);
    private readonly KryptonCheckBox _left = new() { Text = "Include left students", AutoSize = true, Margin = new Padding(10, 4, 0, 0) };
    private List<Change> _changes = [];
    private FilterBindingList<Row>? _list;
    private bool _filling;

    public FeeIncreaseReport(ScreenContext ctx) : base(ctx)
    {
        _filling = true;
        Fill<bool>(_group, null, [(true, "By class and section"), (false, "No grouping")]);
        _filling = false;
        _from.Value = new DateTime(DateTime.Today.Year, 1, 1);
        _to.Value = DateTime.Today;
        Filters.Controls.AddRange([Caption("From"), _from, Caption("To"), _to, .. _classes.Controls, _reason, _group, _left]);
        AddShowButton();
        _from.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _to.Value = _from.Value; };
        _to.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _from.Value = _to.Value; };
        _classes.Changed += (_, _) => Apply();
        _reason.SelectedIndexChanged += (_, _) => Apply();
        _group.SelectedIndexChanged += (_, _) => Apply();
        _left.CheckedChanged += (_, _) => Apply();
        Hint = "Each change of a student's tuition fee, saved in Fee definition and by the annual increase. Change the dates and click Show. Print and Excel use the rows shown.";
        BuildColumns();
    }

    public override string Title => "Fee increase log";
    public override string Replaces => "rptFeeIncreasedLog";

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<Row>().ToList() ?? [];
            return rows.Count == 0 ? "No fee changes"
                : $"{rows.Count:N0} changes · {rows.Select(r => r.Cc).Distinct().Count():N0} students · increase Rs {rows.Sum(r => r.Increase):N0} a month · average {rows.Average(r => r.Increase):N0}";
        }
    }

    protected override string FilterText
    {
        get
        {
            var parts = new List<string> { $"{_from.Value:dd/MM/yyyy} to {_to.Value:dd/MM/yyyy}" };
            parts.AddRange(_classes.TextParts());
            if (_reason.SelectedItem as string is { } r && r != AllReasons) parts.Add(r);
            if (_left.Checked) parts.Add("Including left students");
            return string.Join(" · ", parts);
        }
    }

    protected override int? PrintSchoolId => _classes.SchoolId;

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<bool>(_group)
        ? [("Class", nameof(Row.ClassName)), ("Section", nameof(Row.SectionName))]
        : [];

    protected override async Task LoadAsync()
    {
        var (first, after) = (_from.Value.Date, _to.Value.Date.AddDays(1));
        var (lookup, changes) = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var lookup = ClassLookup.Read(db).For(Ctx.Session);
            var changes = (from f in db.FeeIncreases.AsNoTracking()
                           join s in db.Students.AsNoTracking() on f.StudentId equals s.Id
                           where f.UpdatedOn >= first && f.UpdatedOn < after
                           select new { f, s.Id, s.Name, s.ClassId, s.SectionId, s.OnRoll, s.LeavingType }).AsEnumerable()
                .Select(x => new Change(x.Id, x.Name ?? "", x.ClassId, x.SectionId, x.OnRoll,
                    x.OnRoll ? "Active" : string.IsNullOrWhiteSpace(x.LeavingType) ? "Not active" : x.LeavingType!,
                    x.f.UpdatedOn, x.f.CurrentFee ?? 0, x.f.IncreasedAmount ?? 0, x.f.NewFee ?? 0, x.f.Reason?.Trim() ?? ""))
                .ToList();
            return (lookup, changes);
        });
        if (IsDisposed) return;
        _changes = changes;
        _classes.Set(lookup);
        _filling = true;
        var keep = _reason.SelectedItem as string;
        _reason.Items.Clear();
        _reason.Items.Add(AllReasons);
        foreach (var r in _changes.Select(c => c.Reason).Where(r => r.Length > 0).Distinct().Order()) _reason.Items.Add(r);
        _reason.SelectedItem = keep is not null && _reason.Items.Contains(keep) ? keep : AllReasons;
        _filling = false;
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var reason = _reason.SelectedItem as string is { } r && r != AllReasons ? r : null;
        var rows = _changes
            .Where(c => (_left.Checked || c.OnRoll) && _classes.Matches(c.ClassId, c.SectionId) && (reason is null || c.Reason == reason))
            .Select(c => new Row
            {
                Cc = c.Cc, Name = c.Name, ClassName = _classes.ClassName(c.ClassId), ClassSort = _classes.ClassSort(c.ClassId),
                SectionName = _classes.SectionName(c.SectionId), Status = c.Status, UpdatedOn = c.UpdatedOn,
                CurrentFee = (int)c.CurrentFee, Increase = (int)c.Increase, NewFee = (int)c.NewFee, Reason = c.Reason,
            });
        rows = Selected<bool>(_group)
            ? rows.OrderBy(x => x.ClassSort).ThenBy(x => x.SectionName).ThenBy(x => x.Name).ThenBy(x => x.UpdatedOn)
            : rows.OrderBy(x => x.UpdatedOn).ThenBy(x => x.Cc);
        Grid.Columns[nameof(Row.Status)]!.Visible = _left.Checked;
        _list = new FilterBindingList<Row>(rows.ToList());
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    private void BuildColumns()
    {
        Grid.AddText(nameof(Row.Cc), "CC", 60);
        Grid.AddText(nameof(Row.Name), "Name", 190, fill: true);
        Grid.AddText(nameof(Row.ClassName), "Class", 90);
        Grid.AddText(nameof(Row.SectionName), "Section", 80);
        Grid.AddText(nameof(Row.UpdatedOn), "Changed on", 90, format: "dd/MM/yyyy");
        AddNumber(nameof(Row.CurrentFee), "Old fee", 85);
        AddNumber(nameof(Row.Increase), "Increase", 85);
        AddNumber(nameof(Row.NewFee), "New fee", 85);
        Grid.AddText(nameof(Row.Reason), "Reason", 200, fill: true);
        Grid.AddText(nameof(Row.Status), "Status", 80);
    }
}
