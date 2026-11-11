using System.ComponentModel;
using ArqamBase.App.Core;

namespace ArqamBase.App.Controls;

/// <summary>
/// Access-style column menu: sort, clear the filter, and a filter condition that suits the column:
/// text (anywhere in the field, starts with, exact), a number (equal, between, greater than, less than)
/// or a date (equal, between, before, after). Enter or OK applies it.
/// </summary>
public sealed class FilterMenu : ContextMenuStrip
{
    private const int BoxWidth = 236;

    public event Action<ListSortDirection>? SortRequested;
    public event Action? ClearRequested;
    public event Action<ColumnFilter>? FilterRequested;

    private readonly FilterKind _kind;
    private readonly List<(RadioButton Button, FilterOp Op)> _ops = [];
    private readonly TextBox _value = new() { Width = BoxWidth - 12 }, _value2 = new() { Width = BoxWidth - 12 };
    private readonly Label _and = new() { Text = "and", AutoSize = true, Margin = new Padding(3, 4, 3, 2) };
    private readonly Label _problem = new() { AutoSize = true, MaximumSize = new Size(BoxWidth - 12, 0), ForeColor = Theme.Error, Margin = new Padding(3, 2, 3, 0) };

    public FilterMenu(string column, FilterKind kind, ColumnFilter? current, bool isFiltered)
    {
        _kind = kind;
        ShowImageMargin = true;

        Items.Add(new ToolStripMenuItem(kind == FilterKind.Text ? "Sort A to Z" : kind == FilterKind.Number ? "Sort smallest to largest" : "Sort oldest to newest",
            Icons.Small(Icons.SortUp), (_, _) => SortRequested?.Invoke(ListSortDirection.Ascending)));
        Items.Add(new ToolStripMenuItem(kind == FilterKind.Text ? "Sort Z to A" : kind == FilterKind.Number ? "Sort largest to smallest" : "Sort newest to oldest",
            Icons.Small(Icons.SortDown), (_, _) => SortRequested?.Invoke(ListSortDirection.Descending)));
        Items.Add(new ToolStripSeparator());
        Items.Add(new ToolStripMenuItem($"Clear filter from {column}", Icons.Small(Icons.Clear), (_, _) => ClearRequested?.Invoke()) { Enabled = isFiltered });
        Items.Add(new ToolStripSeparator());

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = BoxWidth, Padding = new Padding(2), BackColor = Color.Transparent,
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 2, 3, 4),
            Text = kind switch { FilterKind.Number => "Number filter", FilterKind.Date => "Date filter", _ => "Text filter" },
        });

        var choices = kind switch
        {
            FilterKind.Number => new[] { (FilterOp.Equal, "Equal to"), (FilterOp.Between, "Between"), (FilterOp.Greater, "Greater than"), (FilterOp.Less, "Less than") },
            FilterKind.Date => [(FilterOp.Equal, "On"), (FilterOp.Between, "Between"), (FilterOp.Before, "Before"), (FilterOp.After, "After")],
            _ => [(FilterOp.Contains, "Anywhere in the field"), (FilterOp.StartsWith, "Starts with"), (FilterOp.Exact, "Exactly")],
        };
        var radios = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0), BackColor = Color.Transparent };
        foreach (var (op, text) in choices)
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 1, 8, 1) };
            rb.CheckedChanged += (_, _) => ShowSecondBox();
            _ops.Add((rb, op));
            radios.Controls.Add(rb);
        }
        if (kind == FilterKind.Text) radios.ColumnCount = 1;
        panel.Controls.Add(radios);

        var hint = kind switch { FilterKind.Number => "Number, e.g. 12,800", FilterKind.Date => "Date: dd/mm/yyyy", _ => "Text to find" };
        _value.PlaceholderText = hint;
        _value2.PlaceholderText = hint;
        panel.Controls.Add(_value);
        panel.Controls.Add(_and);
        panel.Controls.Add(_value2);
        panel.Controls.Add(_problem);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = BoxWidth - 6, Height = 34, Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
        var ok = new Button { Text = "OK", Width = 80, Height = 26 };
        var cancel = new Button { Text = "Cancel", Width = 80, Height = 26 };
        ok.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        panel.Controls.Add(buttons);

        // Sized with the second box showing, so choosing Between later still fits; the extra is for a message under the boxes.
        _and.Visible = _value2.Visible = kind != FilterKind.Text;
        var size = panel.GetPreferredSize(new Size(BoxWidth, 0));
        Show(current);
        Items.Add(new ToolStripControlHost(panel) { AutoSize = false, Width = BoxWidth + 4, Height = size.Height + 34 });
        Opened += (_, _) => { _value.Focus(); _value.SelectAll(); };
        foreach (var box in new[] { _value, _value2 })
            box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Apply(); } };
    }

    /// <summary>Starts from the column's filter, or the first choice.</summary>
    private void Show(ColumnFilter? f)
    {
        var op = f?.Op is { } o && _ops.Any(x => x.Op == o) ? o : _ops[0].Op;
        _ops.First(x => x.Op == op).Button.Checked = true;
        if (f is not null)
        {
            (_value.Text, _value2.Text) = _kind switch
            {
                FilterKind.Number => (f.Number1?.ToString("#,##0.##") ?? "", f.Number2?.ToString("#,##0.##") ?? ""),
                FilterKind.Date => (f.Date1?.ToString("dd/MM/yyyy") ?? "", f.Date2?.ToString("dd/MM/yyyy") ?? ""),
                _ => (f.Text, ""),
            };
        }
        ShowSecondBox();
    }

    private FilterOp Op => _ops.First(x => x.Button.Checked).Op;

    private void ShowSecondBox() => _and.Visible = _value2.Visible = _ops.Any(x => x.Button.Checked) && Op == FilterOp.Between;

    private void Apply()
    {
        var op = Op;
        var between = op == FilterOp.Between;
        ColumnFilter? filter;
        switch (_kind)
        {
            case FilterKind.Number:
                var a = ColumnFilter.ParseNumber(_value.Text);
                var b = between ? ColumnFilter.ParseNumber(_value2.Text) : null;
                if (a is null || (between && b is null)) { _problem.Text = "Type a number" + (between ? " in both boxes." : "."); return; }
                filter = new ColumnFilter(op, Number1: a, Number2: b);
                break;
            case FilterKind.Date:
                var d1 = ColumnFilter.ParseDate(_value.Text);
                var d2 = between ? ColumnFilter.ParseDate(_value2.Text) : null;
                if (d1 is null || (between && d2 is null)) { _problem.Text = "Type the date as dd/mm/yyyy" + (between ? " in both boxes." : "."); return; }
                if (d2 < d1) (d1, d2) = (d2, d1);
                filter = new ColumnFilter(op, Date1: d1, Date2: d2);
                break;
            default:
                var text = _value.Text.Trim();
                if (text.Length == 0) { _problem.Text = "Type the text to find."; return; }
                filter = new ColumnFilter(op, Text: text);
                break;
        }
        FilterRequested?.Invoke(filter);
        Close();
    }
}
