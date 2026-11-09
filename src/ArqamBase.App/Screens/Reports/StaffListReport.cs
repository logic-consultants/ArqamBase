using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// The staff list with campus, salary sheet, post and phone. Only an Admin sees the current salary and the security deposited,
/// which is the total of the Sec. deductions on the salary sheets.
/// </summary>
public sealed class StaffListReport : ReportView
{
    public sealed class Row
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public DateTime? Joined { get; init; }
        public DateTime? LeftOn { get; init; }
        public string Campus { get; init; } = "";
        public string Sheet { get; init; } = "";
        public string Post { get; init; } = "";
        public string Cell { get; init; } = "";
        public decimal CurrentSalary { get; init; }
        public decimal SecurityDeposited { get; init; }
    }

    private readonly ComboBox _show = Combo(140);
    private readonly bool _isAdmin;
    private List<Row> _rows = [];
    private FilterBindingList<Row>? _list;

    public StaffListReport(ScreenContext ctx) : base(ctx)
    {
        _isAdmin = ctx.Session.IsAdmin;
        _show.Items.AddRange(["Working staff", "Staff who left", "All staff"]);
        _show.SelectedIndex = 0;
        _show.SelectedIndexChanged += (_, _) => Apply();
        Filters.Controls.Add(_show);
        AddShowButton();
        Grid.AddText(nameof(Row.Id), "Emp ID", 60);
        Grid.AddText(nameof(Row.Title), "Title", 50);
        Grid.AddText(nameof(Row.Name), "Name", 150, fill: true);
        Grid.AddText(nameof(Row.FatherName), "Father / spouse name", 150);
        Grid.AddText(nameof(Row.Joined), "Date of joining", 90, format: "dd/MM/yyyy");
        Grid.AddText(nameof(Row.Campus), "Campus", 80);
        Grid.AddText(nameof(Row.Sheet), "Salary sheet", 170);
        Grid.AddText(nameof(Row.Post), "Post", 105);
        Grid.AddText(nameof(Row.Cell), "Cell #", 100);
        if (!_isAdmin) return;
        Hint = "Total security deposited is the Sec. deductions on the salary sheets kept here, from July 2026; anything deducted before that is not included.";
        AddNumber(nameof(Row.CurrentSalary), "Current salary", 85);
        AddNumber(nameof(Row.SecurityDeposited), "Total security deposited", 100);
    }

    public override string Title => "Staff list";
    public override string Replaces => "nothing (new in this app)";
    public override string StatusText => $"{_list?.Count ?? 0:N0} staff";

    protected override string FilterText => $"{_show.SelectedItem}";
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [];

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            if (rows.Count == 0) return [];
            return _isAdmin
                ? [("Staff", $"{rows.Count:N0}"), ("Current salaries", $"Rs {rows.Sum(r => r.CurrentSalary):N0}"), ("Security deposited", $"Rs {rows.Sum(r => r.SecurityDeposited):N0}")]
                : [("Staff", $"{rows.Count:N0}")];
        }
    }

    protected override async Task LoadAsync()
    {
        var admin = _isAdmin;
        _rows = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var posts = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => t.Name?.Trim() ?? "");
            var schools = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => SalarySheetScreen.CampusName(s.Name));
            var deposited = admin
                ? db.SalaryLines.AsNoTracking().GroupBy(l => l.StaffId).Select(g => new { g.Key, Sum = g.Sum(l => l.Security ?? 0) }).ToDictionary(x => x.Key, x => x.Sum)
                : [];
            return db.Staff.AsNoTracking().ToList().Select(s => new Row
            {
                Id = s.Id, Title = s.Salute?.Trim() ?? "", Name = s.Name?.Trim() ?? "", FatherName = s.FatherName?.Trim() ?? "", Joined = s.DateHired, LeftOn = s.LeftOn,
                Campus = s.BranchId is int b ? schools.GetValueOrDefault(b, "") : "",
                Sheet = Payroll.Sheets.FirstOrDefault(x => x.Id == s.SalarySheet).Name ?? "",
                Post = s.TypeId is int t && posts.TryGetValue(t, out var p) ? p : s.Designation?.Trim() ?? "",
                Cell = s.CellNo?.Trim() ?? "", CurrentSalary = admin ? s.CurrentSalary ?? 0 : 0, SecurityDeposited = deposited.GetValueOrDefault(s.Id),
            }).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });
        if (!IsDisposed) Apply();
    }

    private void Apply()
    {
        var show = _show.SelectedIndex;
        _list = new FilterBindingList<Row>(_rows.Where(r => show == 2 || (show == 0) == (r.LeftOn is null)).ToList());
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}
