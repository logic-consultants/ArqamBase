using ArqamBase.App.Controls;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// Fees received between two dates (replaces Daily Fee Received Report, rptDailyFeeReceived, rptDailyFeeRR, rptDailyFeeRR_DepositWise,
/// rpt_DailyFeeReceipt_Receiptwise): each challan, each receipt or a summary, by date and collection type.
/// </summary>
public sealed class DailyFeeReport : ReportView
{
    public sealed record Row
    {
        public DateTime? ReceivedOn { get; init; }
        public string CollectionType { get; init; } = "";
        public int? ReceiptNo { get; init; }
        public int? Cc { get; init; }
        public int? FamilyId { get; init; }
        public string Name { get; init; } = "";
        public string FatherName { get; init; } = "";
        public string ClassName { get; init; } = "";
        public string Period { get; init; } = "";
        public int Receipts { get; init; }
        public int Challans { get; init; }
        public int Tuition { get; init; }
        public int Exams { get; init; }
        public int Registration { get; init; }
        public int Admission { get; init; }
        public int Prospectus { get; init; }
        public int Fine { get; init; }
        public int Other { get; init; }
        public int OnSlip { get; init; }
        public int Received { get; init; }
        public string ReceivedBy { get; init; } = "";
        public string Remarks { get; init; } = "";
    }

    private sealed record Paid(DateTime? ReceivedOn, int? TypeId, string Type, int? ReceiptNo, int? Cc, int? FamilyId, string Name, string Father,
        string ClassName, string Period, int Tuition, int Exams, int Registration, int Admission, int Prospectus, int Fine, int Other, int OnSlip,
        int Received, string By, string Remarks);

    private enum View { Challan, Receipt, Summary }

    private readonly KryptonDateTimePicker _from = new() { Width = 120, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Margin = new Padding(4, 3, 0, 0) };
    private readonly KryptonDateTimePicker _to = new() { Width = 120, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Margin = new Padding(4, 3, 0, 0) };
    private readonly ComboBox _type = Combo(190);
    private readonly ComboBox _view = Combo(150);
    private List<Paid> _paid = [];
    private FilterBindingList<Row>? _list;
    private View? _columnsFor;
    private bool _filling;

    public DailyFeeReport(ScreenContext ctx) : base(ctx)
    {
        _from.Value = _to.Value = DateTime.Today;
        _filling = true;
        Fill<View>(_view, null, [(View.Challan, "Each challan"), (View.Receipt, "Each receipt"), (View.Summary, "Summary")]);
        _filling = false;
        Filters.Controls.AddRange([Caption("From"), _from, Caption("To"), _to, _type, _view]);
        AddShowButton();
        _from.ValueChanged += (_, _) => { if (_to.Value < _from.Value) _to.Value = _from.Value; };
        _type.SelectedIndexChanged += (_, _) => Apply();
        _view.SelectedIndexChanged += (_, _) => Apply();
        Hint = "Fees received in the dates chosen (click Show after changing them), by date and collection type. Print and Excel use the rows shown.";
    }

    public override string Title => "Daily fee received";
    public override string Replaces => "Daily Fee Received Report, rptDailyFeeReceived, rptDailyFeeRR, rptDailyFeeRR_DepositWise, rpt_DailyFeeReceipt_Receiptwise";
    protected override IReadOnlyList<string> Signatures => ["Prepared by", "Accountant", "Manager"];

    public override string StatusText
    {
        get
        {
            var rows = _list?.Cast<Row>().ToList() ?? [];
            return $"{rows.Sum(r => r.Receipts):N0} receipts · {rows.Sum(r => r.Challans):N0} challans · Rs {rows.Sum(r => r.Received):N0} received";
        }
    }

    protected override string FilterText
    {
        get
        {
            var dates = _from.Value.Date == _to.Value.Date ? $"{_from.Value:dd/MM/yyyy}" : $"{_from.Value:dd/MM/yyyy} to {_to.Value:dd/MM/yyyy}";
            var type = Selected<int?>(_type) is null ? "" : " · " + SelectedText(_type);
            return dates + type;
        }
    }

    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => Selected<View>(_view) == View.Summary
        ? [("Date", nameof(Row.ReceivedOn))]
        : [("Date", nameof(Row.ReceivedOn)), ("Collection type", nameof(Row.CollectionType))];

    protected override async Task LoadAsync()
    {
        var (first, after) = (_from.Value.Date, _to.Value.Date.AddDays(1));
        var data = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var types = db.CollectionTypes.AsNoTracking().OrderBy(t => t.Id).AsEnumerable().Select(t => ((int?)t.Id, t.Name ?? "")).ToList();
            var names = types.ToDictionary(t => t.Item1!.Value, t => t.Item2);
            var classes = db.Classes.AsNoTracking().AsEnumerable().ToDictionary(c => c.Id, c => c.Name ?? "");
            var paid = (from c in db.FeeChallans.AsNoTracking()
                        where c.Paid && c.ReceivedOn >= first && c.ReceivedOn < after
                        join s in db.Students.AsNoTracking() on c.StudentId equals s.Id into ss
                        from s in ss.DefaultIfEmpty()
                        select new { c, s, Father = s.Family!.FatherName }).AsEnumerable()
                .Select(x => new Paid(x.c.ReceivedOn, x.c.CollectionTypeId,
                    x.c.CollectionTypeId is int t && names.TryGetValue(t, out var n) ? n : "(none)", x.c.ReceiptNo, x.c.StudentId, x.s?.FamilyId,
                    x.s?.Name ?? "", x.Father ?? "", x.s?.ClassId is int ci && classes.TryGetValue(ci, out var cn) ? cn : "", x.c.Period ?? "",
                    x.c.TuitionFee ?? 0, x.c.ExamsFee ?? 0, x.c.RegistrationFee ?? 0, x.c.AdmissionFee ?? 0, x.c.ProspectusFee ?? 0, x.c.Fine ?? 0,
                    x.c.Other ?? 0, x.c.Total, x.c.ReceivedAmount ?? 0, x.c.EditedBy ?? "", x.c.Remarks ?? ""))
                .OrderBy(p => p.ReceivedOn).ThenBy(p => p.TypeId).ThenBy(p => p.ReceiptNo).ThenBy(p => p.Cc).ToList();
            return (types, paid);
        });
        if (IsDisposed) return;
        _paid = data.paid;
        _filling = true;
        Fill(_type, "All collection types", data.types);
        _filling = false;
        Apply();
    }

    private void Apply()
    {
        if (_filling) return;
        var type = Selected<int?>(_type);
        var view = Selected<View>(_view);
        var paid = _paid.Where(p => type is null || p.TypeId == type).ToList();

        // A receipt counts once, on its first challan.
        var seen = new HashSet<(DateTime?, int?, int?)>();
        Row Challan(Paid p) => new()
        {
            ReceivedOn = p.ReceivedOn, CollectionType = p.Type, ReceiptNo = p.ReceiptNo, Cc = p.Cc, FamilyId = p.FamilyId, Name = p.Name,
            FatherName = p.Father, ClassName = p.ClassName, Period = p.Period, Challans = 1, Receipts = seen.Add((p.ReceivedOn?.Date, p.TypeId, p.ReceiptNo)) ? 1 : 0,
            Tuition = p.Tuition, Exams = p.Exams, Registration = p.Registration, Admission = p.Admission, Prospectus = p.Prospectus,
            Fine = p.Fine, Other = p.Other, OnSlip = p.OnSlip, Received = p.Received, ReceivedBy = p.By, Remarks = p.Remarks,
        };
        static Row Total(IReadOnlyList<Paid> items) => new()
        {
            ReceivedOn = items[0].ReceivedOn, CollectionType = items[0].Type,
            Receipts = items.Select(p => (p.TypeId, p.ReceiptNo)).Distinct().Count(), Challans = items.Count,
            Tuition = items.Sum(p => p.Tuition), Exams = items.Sum(p => p.Exams), Registration = items.Sum(p => p.Registration),
            Admission = items.Sum(p => p.Admission), Prospectus = items.Sum(p => p.Prospectus), Fine = items.Sum(p => p.Fine),
            Other = items.Sum(p => p.Other), OnSlip = items.Sum(p => p.OnSlip), Received = items.Sum(p => p.Received),
            ReceivedBy = string.Join(", ", items.Select(p => p.By).Where(b => b.Length > 0).Distinct()),
        };

        var rows = view switch
        {
            View.Challan => paid.Select(Challan).ToList(),
            View.Receipt => paid.GroupBy(p => (p.ReceivedOn?.Date, p.TypeId, p.ReceiptNo)).Select(g => Total(g.ToList()) with
            {
                ReceiptNo = g.Key.ReceiptNo, FamilyId = g.First().FamilyId, FatherName = g.First().Father,
                Name = string.Join(", ", g.Select(p => p.Name).Distinct()),
                Period = string.Join(", ", g.Select(p => p.Period).Distinct()),
            }).ToList(),
            _ => paid.GroupBy(p => (p.ReceivedOn?.Date, p.TypeId)).Select(g => Total(g.ToList())).ToList(),
        };

        BuildColumns(view);
        _list = new FilterBindingList<Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }

    private void BuildColumns(View view)
    {
        if (_columnsFor == view) return;
        _columnsFor = view;
        Grid.ResetColumns();
        SumProperties.Clear();
        Grid.AddText(nameof(Row.ReceivedOn), "Date", 85, format: "dd/MM/yyyy");
        Grid.AddText(nameof(Row.CollectionType), "Collection type", 140);
        switch (view)
        {
            case View.Challan:
                Grid.AddText(nameof(Row.ReceiptNo), "R#", 60);
                Grid.AddText(nameof(Row.Cc), "CC", 60);
                Grid.AddText(nameof(Row.FamilyId), "Family", 60);
                Grid.AddText(nameof(Row.Name), "Name", 160, fill: true);
                Grid.AddText(nameof(Row.ClassName), "Class", 80);
                Grid.AddText(nameof(Row.Period), "Period", 70);
                break;
            case View.Receipt:
                Grid.AddText(nameof(Row.ReceiptNo), "R#", 60);
                Grid.AddText(nameof(Row.FamilyId), "Family", 60);
                Grid.AddText(nameof(Row.FatherName), "Father name", 150, fill: true);
                Grid.AddText(nameof(Row.Name), "Students", 180, fill: true);
                Grid.AddText(nameof(Row.Period), "Periods", 130, fill: true);
                Number(nameof(Row.Challans), "Periods paid", 70);
                break;
            default:
                Number(nameof(Row.Receipts), "Receipts", 70);
                Number(nameof(Row.Challans), "Challans", 70);
                break;
        }
        if (view != View.Receipt)
            foreach (var (p, h) in new[] { (nameof(Row.Tuition), "Tuition"), (nameof(Row.Exams), "Exams/AF"), (nameof(Row.Registration), "Reg."),
                         (nameof(Row.Admission), "Adm."), (nameof(Row.Prospectus), "Prosp."), (nameof(Row.Fine), "Fine"), (nameof(Row.Other), "Other") })
                Number(p, h, 68);
        Number(nameof(Row.Received), "Received", 85);
        Grid.AddText(nameof(Row.ReceivedBy), "Received by", 100);
        if (view == View.Challan) Grid.AddText(nameof(Row.Remarks), "Remarks", 120).Visible = false;
    }

    private void Number(string property, string header, int width)
    {
        var col = Grid.AddText(property, header, width, format: "N0");
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        SumProperties.Add(property);
    }
}
