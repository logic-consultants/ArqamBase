using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using ArqamBase.App.Shell;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens.Reports;

/// <summary>
/// One report of the Reports centre: its filters along the top, the result in a grid (which can be filtered and sorted further),
/// and Print preview / Excel of the rows shown.
/// </summary>
public abstract partial class ReportView : KryptonPanel
{
    protected ScreenContext Ctx { get; }
    protected FlowLayoutPanel Filters { get; } = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 6) };
    protected AccessGrid Grid { get; } = new();
    private readonly KryptonButton _show = new() { Text = "Show", Values = { Image = Icons.Small(Icons.Refresh) } };
    private readonly KryptonLabel _hint = new()
    {
        Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 8, Padding = new Padding(0, 6, 0, 0),
        StateCommon = { ShortText = { Color1 = Theme.Muted } }
    };
    private readonly KryptonLabel _summary = new()
    {
        Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.RowHeight + 10, Padding = new Padding(0, 8, 0, 0), Visible = false,
        StateCommon = { ShortText = { Font = new Font(Theme.BaseFont, FontStyle.Bold) } }
    };
    private bool _loading;

    public event EventHandler? StatusChanged;

    protected ReportView(ScreenContext ctx)
    {
        Ctx = ctx;
        Dock = DockStyle.Fill;
        Grid.AllowUserToAddRows = Grid.AllowUserToDeleteRows = false;
        Grid.ReadOnly = true;
        Grid.FilterChanged += (_, _) => RaiseStatusChanged();
        _show.Click += (_, _) => _ = ShowReportAsync();
        Theme.MainButton(_show);
        Controls.Add(Grid);
        Controls.Add(_summary);
        Controls.Add(_hint);
        Controls.Add(Filters);
        HandleCreated += (_, _) => _ = ShowReportAsync();
    }

    public abstract string Title { get; }
    /// <summary>The Access reports this one replaces, shown under the title.</summary>
    public abstract string Replaces { get; }
    public virtual string StatusText => "";

    protected string Hint { set => _hint.Text = value; }

    /// <summary>Loads the report with the current filters (on a worker thread) and shows it in the grid.</summary>
    protected abstract Task LoadAsync();

    /// <summary>The filters as one line, for the printout.</summary>
    protected abstract string FilterText { get; }

    /// <summary>The grid columns that become group headings on the printout (and are left out of its table), outermost first.</summary>
    protected abstract IReadOnlyList<(string Name, string Property)> PrintGroups { get; }

    /// <summary>Grid columns that get group and grand totals on the printout.</summary>
    protected HashSet<string> SumProperties { get; } = [];

    /// <summary>The school whose name heads the printout; none gives the default name.</summary>
    protected virtual int? PrintSchoolId => null;
    protected virtual IReadOnlyList<string> Signatures => [];

    /// <summary>Summary figures of the rows shown, under the grid and in a box on the printout.</summary>
    protected virtual IReadOnlyList<(string Label, string Value)> Summary => [];

    /// <summary>The grid's rows as shown (after its column filters), in order.</summary>
    protected IEnumerable<T> ShownRows<T>() =>
        Grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Visible).Select(r => r.DataBoundItem).OfType<T>();

    /// <summary>
    /// The printout of the grid as shown: its visible columns in their order and its rows (after filters and sort),
    /// kept together by group in the order the groups first appear.
    /// </summary>
    private GroupedReport BuildPrint()
    {
        var groups = PrintGroups.Where(g => Grid.Columns.Contains(g.Property)).ToList();
        var groupProps = groups.Select(g => g.Property).ToHashSet();
        var columns = Grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible && !groupProps.Contains(c.DataPropertyName))
            .OrderBy(c => c.DisplayIndex).ToList();
        var rows = Grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && r.Visible).ToList();
        string Text(DataGridViewRow r, string property) => r.Cells[property].FormattedValue?.ToString() ?? "";
        var order = new Dictionary<string, int>();
        foreach (var r in rows)
        {
            var key = "";
            foreach (var g in groups) { key += Text(r, g.Property) + "\u001f"; order.TryAdd(key, order.Count); }
        }
        // Each row's groups as "000003,000012" (the order each group first appeared), so rows of a group stay together.
        var lines = rows
            .Select((r, i) => (r, i, sort: string.Join(",", groups.Select((_, l) =>
                order[string.Concat(groups.Take(l + 1).Select(x => Text(r, x.Property) + "\u001f"))].ToString("D6")))))
            .OrderBy(x => x.sort, StringComparer.Ordinal);
        var header = PrintHeader(PrintSchoolId);
        return new GroupedReport
        {
            Title = Title,
            Subtitle = FilterText,
            Columns = columns.Select(c => new ReportColumn(c.HeaderText, c.Width,
                c.DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight, SumProperties.Contains(c.DataPropertyName))).ToList(),
            Lines = lines.ThenBy(x => x.i).Select(x => new ReportLine(
                columns.Select(c => x.r.Cells[c.Index].FormattedValue?.ToString() ?? "").ToArray(),
                columns.Select((c, ci) => (c, ci)).Where(y => SumProperties.Contains(y.c.DataPropertyName))
                    .ToDictionary(y => y.ci, y => x.r.Cells[y.c.Index].Value is { } v and not DBNull ? Convert.ToDecimal(v) : 0m),
                groups.Select(g => Text(x.r, g.Property)).ToArray())).ToList(),
            GroupNames = groups.Select(g => g.Name).ToList(),
            Landscape = columns.Sum(c => c.Width) > 900,
            Signatures = Signatures,
            Summary = Summary,
            SchoolName = header.Name,
            Logo = header.Logo,
        };
    }

    /// <summary>Adds the Show button after the report's own filters.</summary>
    protected void AddShowButton()
    {
        _show.Size = new Size(TextRenderer.MeasureText(_show.Text, Theme.BaseFont).Width + 48, Theme.RowHeight + 2);
        _show.Margin = new Padding(8, 2, 0, 0);
        Filters.Controls.Add(_show);
    }

    /// <summary>A caption in front of a filter box.</summary>
    protected static KryptonLabel Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 5, 2, 0) };

    protected static ComboBox Combo(int width) => new() { Width = width, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 3, 0, 0) };

    protected static void Fill<T>(ComboBox combo, string? all, IEnumerable<(T? Value, string Text)> items)
    {
        var keep = combo.SelectedItem is Inputs.Item<T> k ? k.Value : default;
        var list = new List<Inputs.Item<T>>();
        if (all is not null) list.Add(new(default, all));
        list.AddRange(items.Select(i => new Inputs.Item<T>(i.Value, i.Text)));
        combo.DataSource = list;
        combo.DisplayMember = nameof(Inputs.Item<T>.Text);
        combo.SelectedItem = list.FirstOrDefault(i => keep is not null && Equals(i.Value, keep)) ?? list.FirstOrDefault();
    }

    protected static T? Selected<T>(ComboBox combo) => combo.SelectedItem is Inputs.Item<T> i ? i.Value : default;
    protected static string SelectedText(ComboBox combo) => (combo.SelectedItem as Inputs.Item<int?>)?.Text ?? combo.Text;

    public async Task ShowReportAsync()
    {
        if (_loading) return;
        _loading = true;
        UseWaitCursor = true;
        _show.Enabled = false;
        try
        {
            await LoadAsync();
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError($"Could not load {Title}.", ex);
        }
        finally
        {
            _loading = false;
            _show.Enabled = true;
            UseWaitCursor = false;
            RaiseStatusChanged();
        }
    }

    public virtual void Preview()
    {
        if (Grid.Rows.Count == 0) { Inform("There is nothing to print. Change the filters and click Show."); return; }
        var report = BuildPrint();
        PrintPreviewForm.Show(this, report.ToPdf, Title, $"{Title} {DateTime.Today:yyyy-MM-dd}");
    }

    public void ExportExcel()
    {
        if (Grid.Rows.Count == 0) { Inform("There is nothing to export. Change the filters and click Show."); return; }
        using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"{Title} {DateTime.Today:yyyy-MM-dd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ListExport.From(Grid, Title).SaveExcel(dialog.FileName);
        }
        catch (IOException ex)
        {
            Program.ShowError("Could not save the Excel file. Is it open in Excel?", ex);
            return;
        }
        if (KryptonMessageBox.Show(this, $"Saved {Path.GetFileName(dialog.FileName)}. Open it now?", "Excel",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Information) == DialogResult.Yes)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }

    /// <summary>The school name (of the chosen school, else the default) and logo for the printout header.</summary>
    protected (string Name, byte[] Logo) PrintHeader(int? schoolId = null)
    {
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var campus = db.Campuses.OrderBy(c => c.Id).FirstOrDefault();
            var school = schoolId is null ? null : db.Schools.Where(s => s.Id == schoolId).Select(s => s.Name).FirstOrDefault();
            return (school is { Length: > 0 } n ? n.Trim() : PrintKit.DefaultSchoolName, SchoolPrint.From(campus).Logo);
        }
        catch (Microsoft.Data.SqlClient.SqlException) { return (PrintKit.DefaultSchoolName, PrintKit.Logo); }
    }

    protected static string Money(int value) => value.ToString("N0");

    /// <summary>A right-aligned amount column; with sum, it gets group and grand totals on the printout.</summary>
    protected void AddNumber(string property, string header, int width, bool sum = true)
    {
        var col = Grid.AddText(property, header, width, format: "N0");
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        if (sum) SumProperties.Add(property);
    }

    protected static KryptonDateTimePicker DatePicker() =>
        new() { Width = 120, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Margin = new Padding(4, 3, 0, 0) };

    protected void RaiseStatusChanged()
    {
        var summary = Summary;
        _summary.Visible = summary.Count > 0;
        _summary.Text = string.Join("        ", summary.Select(s => $"{s.Label}: {s.Value}"));
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void Inform(string message) =>
        KryptonMessageBox.Show(this, message, Title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
