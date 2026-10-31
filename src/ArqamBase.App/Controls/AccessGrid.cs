using System.ComponentModel;
using System.Drawing.Drawing2D;
using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Controls;

/// <summary>
/// Data grid that behaves like an Access datasheet: click a heading to sort,
/// click the arrow on a heading to filter that column by value or by text.
/// </summary>
public class AccessGrid : KryptonDataGridView
{
    private const int GlyphWidth = 18;

    public event EventHandler? FilterChanged;

    public AccessGrid()
    {
        AutoGenerateColumns = false;
        AllowUserToResizeRows = false;
        AllowUserToOrderColumns = true;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        RowHeadersWidth = 28;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = 30;
        RowTemplate.Height = 26;
        Dock = DockStyle.Fill;
        DataError += (_, e) =>
        {
            e.ThrowException = false;
            KryptonMessageBox.Show($"That value is not valid for {Columns[e.ColumnIndex].HeaderText}.", "ArqamBase",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
        };
    }

    public IFilterableList? FilterList => DataSource as IFilterableList;

    // ----- Column helpers -----

    public DataGridViewTextBoxColumn AddText(string property, string header, int width = 120, bool readOnly = false, bool fill = false, string? format = null)
    {
        var col = new DataGridViewTextBoxColumn
        {
            DataPropertyName = property, Name = property, HeaderText = header, Width = width, ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        if (fill)
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col.MinimumWidth = width;
        }
        if (format is not null)
            col.DefaultCellStyle.Format = format;
        if (readOnly)
            col.DefaultCellStyle.ForeColor = Theme.Muted;
        Add(col);
        return col;
    }

    public DataGridViewCheckBoxColumn AddCheck(string property, string header, int width = 80)
    {
        var col = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = property, Name = property, HeaderText = header, Width = width,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        Add(col);
        return col;
    }

    /// <summary>Drop-down column. Items are (value, text) pairs; the filter shows the text.</summary>
    public DataGridViewComboBoxColumn AddCombo<TValue>(string property, string header, IEnumerable<(TValue Value, string Text)> items, int width = 160, bool fill = false)
    {
        var list = items.Select(i => new ComboItem<TValue>(i.Value, i.Text)).ToList();
        var col = new DataGridViewComboBoxColumn
        {
            DataPropertyName = property, Name = property, HeaderText = header, Width = width,
            DataSource = list, ValueMember = nameof(ComboItem<TValue>.Value), DisplayMember = nameof(ComboItem<TValue>.Text),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        if (fill)
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col.MinimumWidth = width;
        }
        Add(col);
        _comboTexts[property] = list.Where(i => i.Value is not null).ToDictionary(i => (object)i.Value!, i => i.Text);
        return col;
    }

    private readonly Dictionary<string, Dictionary<object, string>> _comboTexts = [];

    private void Add(DataGridViewColumn col)
    {
        col.HeaderCell.Style.Padding = new Padding(0, 0, GlyphWidth, 0);
        Columns.Add(col);
    }

    /// <summary>Binds the list and tells it how drop-down columns display, so filters match what users see.</summary>
    public void Bind(IFilterableList list)
    {
        foreach (var (property, texts) in _comboTexts)
            list.SetFormatter(property, v => v is not null && texts.TryGetValue(v, out var t) ? t : "");
        DataSource = list;
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetColumns()
    {
        DataSource = null;
        Columns.Clear();
        _comboTexts.Clear();
        CanEditCell = null;
    }

    /// <summary>Optional rule (row, column) deciding whether a cell may be edited.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<int, int, bool>? CanEditCell { get; set; }

    protected override void OnCellBeginEdit(DataGridViewCellCancelEventArgs e)
    {
        if (CanEditCell?.Invoke(e.RowIndex, e.ColumnIndex) == false)
            e.Cancel = true;
        base.OnCellBeginEdit(e);
    }

    // ----- Filter glyph on each heading -----

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        base.OnCellPainting(e);
        if (e.RowIndex != -1 || e.ColumnIndex < 0 || e.Graphics is null)
            return;

        if (!e.Handled)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            e.Handled = true;
        }

        var filtered = FilterList?.HasFilter(Columns[e.ColumnIndex].DataPropertyName) == true;
        var r = GlyphRect(e.CellBounds);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (filtered)
        {
            g.DrawImage(Icons.Get(Icons.Filter, 14), r.X + (r.Width - 14) / 2, r.Y + (r.Height - 14) / 2);
        }
        else
        {
            using var brush = new SolidBrush(Theme.Muted);
            var cx = r.X + r.Width / 2f;
            var cy = r.Y + r.Height / 2f;
            g.FillPolygon(brush, [new PointF(cx - 4, cy - 2), new PointF(cx + 4, cy - 2), new PointF(cx, cy + 3)]);
        }
    }

    private static Rectangle GlyphRect(Rectangle cell) => new(cell.Right - GlyphWidth - 2, cell.Top, GlyphWidth, cell.Height);

    protected override void OnColumnHeaderMouseClick(DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex >= 0)
        {
            var cell = GetCellDisplayRectangle(e.ColumnIndex, -1, false);
            var inGlyph = e.X >= cell.Width - GlyphWidth - 4;
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && inGlyph))
            {
                ShowFilterMenu(e.ColumnIndex, new Point(cell.Left + Math.Max(0, cell.Width - 220), cell.Bottom));
                return;
            }
        }
        base.OnColumnHeaderMouseClick(e);
    }

    public void ShowFilterMenu(int columnIndex, Point location)
    {
        var list = FilterList;
        if (list is null)
            return;
        EndEdit();

        var col = Columns[columnIndex];
        var prop = col.DataPropertyName;
        var menu = new FilterMenu(col.HeaderText, list.DistinctValues(prop), list.HasFilter(prop));
        menu.SortRequested += dir => { list.Sort(prop, dir); col.HeaderCell.SortGlyphDirection = dir == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending; };
        menu.ClearRequested += () => { list.ClearFilter(prop); OnFilterChanged(); };
        menu.FilterRequested += (allowed, contains) => { list.SetFilter(prop, allowed, contains); OnFilterChanged(); };
        menu.Show(this, location);
    }

    public void SortSelectedColumn(ListSortDirection dir)
    {
        if (CurrentCell is null || FilterList is null)
            return;
        var col = Columns[CurrentCell.ColumnIndex];
        FilterList.Sort(col.DataPropertyName, dir);
        col.HeaderCell.SortGlyphDirection = dir == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
    }

    public void ClearAllFilters()
    {
        FilterList?.ClearAllFilters();
        OnFilterChanged();
    }

    private void OnFilterChanged()
    {
        Invalidate();
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public string RecordText()
    {
        var list = FilterList;
        if (list is null)
            return "";
        var shown = list.Count;
        var pos = CurrentRow is { IsNewRow: false } ? CurrentRow.Index + 1 : 0;
        var text = pos > 0 ? $"Record {pos} of {shown}" : $"{shown} records";
        return list.AnyFilter ? $"{text} (filtered from {list.TotalCount})" : text;
    }

    private sealed record ComboItem<TValue>(TValue Value, string Text);
}
