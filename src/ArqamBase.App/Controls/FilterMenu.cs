using System.ComponentModel;
using ArqamBase.App.Core;

namespace ArqamBase.App.Controls;

/// <summary>Access-style column menu: sort, clear filter, "contains" text and a tick list of values.</summary>
public sealed class FilterMenu : ContextMenuStrip
{
    private const string SelectAll = "(Select all)";
    private const string Blank = "(Blanks)";
    private const int MaxValues = 1000;

    public event Action<ListSortDirection>? SortRequested;
    public event Action? ClearRequested;
    public event Action<HashSet<string>?, string?>? FilterRequested;

    private readonly CheckedListBox _values = new() { CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, Width = 220, Height = 200 };
    private readonly TextBox _contains = new() { Width = 220, PlaceholderText = "Text contains…" };
    private bool _syncing;

    public FilterMenu(string column, IReadOnlyList<string> values, bool isFiltered)
    {
        ShowImageMargin = true;

        Items.Add(new ToolStripMenuItem("Sort A to Z", Icons.Small(Icons.SortUp), (_, _) => SortRequested?.Invoke(ListSortDirection.Ascending)));
        Items.Add(new ToolStripMenuItem("Sort Z to A", Icons.Small(Icons.SortDown), (_, _) => SortRequested?.Invoke(ListSortDirection.Descending)));
        Items.Add(new ToolStripSeparator());
        Items.Add(new ToolStripMenuItem($"Clear filter from {column}", Icons.Small(Icons.Clear), (_, _) => ClearRequested?.Invoke()) { Enabled = isFiltered });
        Items.Add(new ToolStripSeparator());

        Items.Add(new ToolStripControlHost(_contains) { AutoSize = false, Width = 224, Height = 26 });

        _values.Items.Add(SelectAll, true);
        foreach (var v in values.Take(MaxValues))
            _values.Items.Add(v.Length == 0 ? Blank : v, true);
        _values.ItemCheck += OnItemCheck;
        Items.Add(new ToolStripControlHost(_values) { AutoSize = false, Width = 224, Height = 204 });
        if (values.Count > MaxValues)
            Items.Add(new ToolStripLabel($"Showing first {MaxValues} values") { ForeColor = Theme.Muted });

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 220, Height = 32, Margin = Padding.Empty, BackColor = Color.Transparent };
        var ok = new Button { Text = "OK", Width = 80, Height = 26 };
        var cancel = new Button { Text = "Cancel", Width = 80, Height = 26 };
        ok.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Items.Add(new ToolStripControlHost(buttons) { AutoSize = false, Width = 224, Height = 34 });

        _contains.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Apply(); } };
        Opened += (_, _) => _contains.Focus();
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_syncing || e.Index != 0)
            return;
        _syncing = true;
        BeginInvoke(() =>
        {
            var check = _values.GetItemChecked(0);
            for (var i = 1; i < _values.Items.Count; i++)
                _values.SetItemChecked(i, check);
            _syncing = false;
        });
    }

    private void Apply()
    {
        HashSet<string>? allowed = null;
        var total = _values.Items.Count - 1;
        var checkedCount = Enumerable.Range(1, total).Count(i => _values.GetItemChecked(i));
        if (checkedCount < total)
        {
            allowed = Enumerable.Range(1, total)
                .Where(i => _values.GetItemChecked(i))
                .Select(i => (string)_values.Items[i]!)
                .Select(s => s == Blank ? "" : s)
                .ToHashSet();
        }
        FilterRequested?.Invoke(allowed, _contains.Text);
        Close();
    }
}
