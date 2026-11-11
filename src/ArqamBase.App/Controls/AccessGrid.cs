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
        SizeRowsToFont();
        Dock = DockStyle.Fill;
        DataError += (_, e) =>
        {
            e.ThrowException = false;
            KryptonMessageBox.Show($"That value is not valid for {Columns[e.ColumnIndex].HeaderText}.", "ArqamBase",
                KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
        };
    }

    public IFilterableList? FilterList => DataSource as IFilterableList;

    /// <summary>Whether the headings have the filter arrow and menu (off for small summary grids).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowFilters { get; set; } = true;

    /// <summary>Rows tall enough for two lines of text (a name with a smaller line under it), drawn by the screen.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool TwoLineRows
    {
        get => _twoLineRows;
        set { _twoLineRows = value; SizeRowsToFont(); }
    }
    private bool _twoLineRows;

    /// <summary>Row and heading heights for the font chosen in Settings.</summary>
    private void SizeRowsToFont()
    {
        var rowHeight = _twoLineRows ? Theme.Scaled(1f).Height + Theme.Scaled(0.85f).Height + 16 : Theme.RowHeight;
        if (ColumnHeadersHeight != Theme.RowHeight + 4) ColumnHeadersHeight = Theme.RowHeight + 4;
        if (RowTemplate.Height == rowHeight) return;
        RowTemplate.Height = rowHeight;
        foreach (DataGridViewRow row in Rows) row.Height = rowHeight;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        SizeRowsToFont();
    }

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

    /// <summary>Drop-down column of plain text choices; the property holds the text itself.</summary>
    public DataGridViewComboBoxColumn AddChoices(string property, string header, IEnumerable<string> items, int width = 120)
    {
        var col = new DataGridViewComboBoxColumn
        {
            DataPropertyName = property, Name = property, HeaderText = header, Width = width,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, FlatStyle = FlatStyle.Flat,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        col.Items.AddRange(items.Cast<object>().ToArray());
        Add(col);
        return col;
    }

    private readonly Dictionary<string, Dictionary<object, string>> _comboTexts = [];
    private readonly Dictionary<string, Func<string?, string>> _numberFormats = [];

    /// <summary>
    /// Shows a text column through a layout (e.g. <see cref="NumberFormat.Phone"/>) and saves edits as plain digits.
    /// Filters show the same layout.
    /// </summary>
    public void FormatNumbers(string property, Func<string?, string> format) => _numberFormats[property] = format;

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex >= 0 && _numberFormats.TryGetValue(Columns[e.ColumnIndex].DataPropertyName, out var format))
        {
            e.Value = format(e.Value as string);
            e.FormattingApplied = true;
        }
        base.OnCellFormatting(e);
    }

    protected override void OnCellParsing(DataGridViewCellParsingEventArgs e)
    {
        if (e.ColumnIndex >= 0 && _numberFormats.ContainsKey(Columns[e.ColumnIndex].DataPropertyName))
        {
            e.Value = NumberFormat.Plain(e.Value as string);
            e.ParsingApplied = true;
        }
        base.OnCellParsing(e);
    }

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
        foreach (var (property, format) in _numberFormats)
            list.SetFormatter(property, v => format(v as string));
        DataSource = list;
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetColumns()
    {
        DataSource = null;
        Columns.Clear();
        _comboTexts.Clear();
        _numberFormats.Clear();
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
        if (e.RowIndex != -1 || e.ColumnIndex < 0 || e.Graphics is null || !ShowFilters)
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
        if (e.ColumnIndex >= 0 && ShowFilters)
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
        if (prop.Length == 0)
            return; // an unbound column, e.g. a blank one to write in on the printout
        var menu = new FilterMenu(col.HeaderText, list.KindOf(prop), list.FilterOf(prop), list.HasFilter(prop));
        menu.SortRequested += dir => { list.Sort(prop, dir); col.HeaderCell.SortGlyphDirection = dir == ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending; };
        menu.ClearRequested += () => { list.ClearFilter(prop); OnFilterChanged(); };
        menu.FilterRequested += filter => { list.SetFilter(prop, filter); OnFilterChanged(); };
        menu.Show(this, location);
    }

    public void SortSelectedColumn(ListSortDirection dir)
    {
        if (CurrentCell is null || FilterList is null || Columns[CurrentCell.ColumnIndex].DataPropertyName.Length == 0)
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
