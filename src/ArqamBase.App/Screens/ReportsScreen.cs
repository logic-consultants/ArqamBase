using ArqamBase.App.Core;
using ArqamBase.App.Screens.Reports;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>
/// The Reports centre (replaces Classselect, Report Date Range and the 62 Access reports): the reports on the left,
/// the chosen one with its filters, result, Print preview and Excel on the right.
/// </summary>
public sealed class ReportsScreen : ScreenBase
{
    private sealed record ReportInfo(string Title, string Group, Func<ScreenContext, ReportView>? Create, string Replaces = "")
    {
        public override string ToString() => Create is null ? $"{Title}  ·  coming" : Title;
    }

    private static readonly ReportInfo[] All =
    [
        new("Unpaid list", "Fees", c => new UnpaidReport(c)),
        new("Daily fee received", "Fees", c => new DailyFeeReport(c)),
        new("Defaulter letter", "Fees", c => new DefaulterLetterReport(c)),
        new("Fee breakup", "Fees", c => new FeeBreakupReport(c)),
        new("Fee increase log", "Fees", c => new FeeIncreaseReport(c)),
        new("Fee delivery proforma", "Fees", c => new DeliveryProformaReport(c)),
        new("Pending task list", "Fees", c => new PendingTaskReport(c)),
        new("Class-wise students and fees", "Students", c => new ClassWiseReport(c)),
        new("Student list", "Students", c => new StudentListReport(c)),
        new("Delivery list", "Students", c => new DeliveryListReport(c)),
        new("Admissions and withdrawals", "Students", c => new AdmissionsReport(c)),
        new("Labels", "Students", null, "Labels Students, WorkBooks Labels"),
        new("Monthly attendance", "Attendance", c => new MonthlyAttendanceReport(c)),
        new("Absentee list", "Attendance", c => new AbsenteeReport(c)),
        new("Staff list", "Staff", c => new StaffListReport(c)),
        new("Staff attendance", "Staff", c => new StaffAttendanceReport(c)),
        new("Staff personal", "Staff", null, "TeachersPersonal"),
    ];

    private readonly ListBox _reports = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, IntegralHeight = false
    };
    private readonly KryptonLabel _title = new() { Dock = DockStyle.Top, LabelStyle = LabelStyle.TitlePanel, AutoSize = true };
    private readonly KryptonLabel _replaces = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8), StateCommon = { ShortText = { Color1 = Theme.Muted } } };
    private readonly KryptonPanel _host = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, ReportView> _views = [];
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private ReportView? _current;

    public ReportsScreen(ScreenContext ctx) : base(ctx)
    {
        _reports.ItemHeight = Theme.RowHeight + 6;
        _reports.DrawItem += DrawReport;
        // Each user sees the reports of the areas they may view (a teacher in charge only attendance).
        foreach (var group in All.Where(r => ctx.Session.CanView(r.Group switch
                     {
                         "Fees" => AppArea.Fees, "Students" => AppArea.Students, "Attendance" => AppArea.Attendance, _ => AppArea.Staff,
                     })).GroupBy(r => r.Group))
        {
            _reports.Items.Add(group.Key); // a caption
            foreach (var r in group) _reports.Items.Add(r);
        }
        _reports.SelectedIndexChanged += (_, _) =>
        {
            if (_reports.SelectedItem is ReportInfo r) Open(r);
            else if (_reports.SelectedIndex >= 0 && _reports.SelectedIndex + 1 < _reports.Items.Count) _reports.SelectedIndex++; // captions are skipped
        };

        var left = new KryptonPanel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(0, 0, 12, 0) };
        var listFrame = new KryptonGroupBox { Dock = DockStyle.Fill, Text = "Reports" };
        listFrame.Panel.Controls.Add(_reports);
        left.Controls.Add(listFrame);

        var right = new KryptonPanel { Dock = DockStyle.Fill };
        right.Controls.Add(_host);
        right.Controls.Add(_replaces);
        right.Controls.Add(_title);
        Controls.Add(right);
        Controls.Add(left);

        _commands =
        [
            new("Report",
                new ScreenCommand("Show", Icons.Refresh, () => _ = _current?.ShowReportAsync()),
                new ScreenCommand("Print preview", Icons.Print, () => _current?.Preview()),
                new ScreenCommand("Excel", Icons.Excel, () => _current?.ExportExcel())),
        ];
        _reports.SelectedIndex = 1;
        Theme.Changed += (_, _) => _reports.Invalidate();
    }

    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override string StatusText => _current?.StatusText ?? "";
    public override void Reload() => _ = _current?.ShowReportAsync();

    /// <summary>A report title, e.g. "Unpaid list".</summary>
    public override void Navigate(object target)
    {
        if (target is string title && All.FirstOrDefault(r => r.Title.Equals(title, StringComparison.OrdinalIgnoreCase)) is { } r)
            _reports.SelectedItem = r;
    }

    private void Open(ReportInfo info)
    {
        _title.Text = info.Title;
        _host.Controls.Clear();
        if (info.Create is null)
        {
            _current = null;
            _replaces.Text = $"Replaces {info.Replaces}";
            _host.Controls.Add(new KryptonLabel
            {
                Dock = DockStyle.Top, AutoSize = false, Height = Theme.RowHeight * 2,
                Text = "This report comes later in phase 4.", StateCommon = { ShortText = { Color1 = Theme.Muted } }
            });
            RaiseStatusChanged();
            return;
        }
        if (!_views.TryGetValue(info.Title, out var view))
        {
            view = info.Create(Ctx);
            view.StatusChanged += (_, _) => RaiseStatusChanged();
            _views[info.Title] = view;
        }
        _current = view;
        _replaces.Text = $"Replaces {view.Replaces}";
        _host.Controls.Add(view);
        Theme.FitButtons(view);
        RaiseStatusChanged();
    }

    /// <summary>Group captions in small capitals, then the reports; reports still to come are dimmer.</summary>
    private void DrawReport(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var item = _reports.Items[e.Index];
        var selected = item is ReportInfo && (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selected : Theme.CardBack))
            e.Graphics.FillRectangle(back, e.Bounds);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
        if (item is string group)
            TextRenderer.DrawText(e.Graphics, group.ToUpperInvariant(), Theme.BaseFont, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted, flags);
        else
            TextRenderer.DrawText(e.Graphics, item.ToString(), Theme.BaseFont, new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 24, e.Bounds.Height),
                ((ReportInfo)item).Create is null ? Theme.Muted : Theme.Text, flags);
    }
}
