using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Controls;

/// <summary>
/// A form layout of labelled fields (small caption above each input) in equal columns.
/// Built for speed, because screens have dozens of fields: it places the inputs itself in one pass (nested auto-sizing
/// TableLayoutPanels took over a second, and again on every resize), and it draws the captions instead of using a
/// label control for each (every control is a window, and windows are slow to create).
/// </summary>
public sealed class FieldPanel : Panel
{
    private const int ColumnGap = 12, RowGap = 6, CaptionGap = 1;
    private static Font CaptionFont => Theme.Scaled(0.85f);
    private static int CaptionHeight => CaptionFont.Height;

    private sealed class Cell(string caption, Control? input, int span)
    {
        public string Caption { get; } = caption;
        public Control? Input { get; } = input;
        public int Span { get; } = span;
        public Rectangle CaptionBounds { get; set; }
    }

    private readonly int _columns;
    private readonly List<Cell> _cells = [];

    public FieldPanel(int columns)
    {
        _columns = columns;
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public T Add<T>(string label, T input, int span = 1) where T : Control
    {
        input.Dock = DockStyle.None;
        input.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _cells.Add(new Cell(label, input, Math.Clamp(span, 1, _columns)));
        Controls.Add(input);
        return input;
    }

    /// <summary>Leaves an empty cell.</summary>
    public void Skip(int span = 1) => _cells.Add(new Cell("", null, Math.Clamp(span, 1, _columns)));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var width = ClientSize.Width - Padding.Horizontal;
        if (width <= 0) return;
        var columnWidth = (width + ColumnGap) / (double)_columns;
        int column = 0, y = Padding.Top, rowHeight = 0;
        foreach (var cell in _cells)
        {
            if (column + cell.Span > _columns)
            {
                column = 0;
                y += rowHeight + RowGap;
                rowHeight = 0;
            }
            var x = Padding.Left + (int)(column * columnWidth);
            var w = (int)(cell.Span * columnWidth) - ColumnGap;
            cell.CaptionBounds = new Rectangle(x, y, w, CaptionHeight);
            if (cell.Input is { } input)
            {
                input.SetBounds(x, y + CaptionHeight + CaptionGap, w, input.Height, BoundsSpecified.Location | BoundsSpecified.Width);
                rowHeight = Math.Max(rowHeight, CaptionHeight + CaptionGap + input.Height);
            }
            column += cell.Span;
        }
        var height = y + rowHeight + RowGap + Padding.Bottom;
        if (Height != height) Height = height;
        Invalidate();
    }

    private int _captionHeight = CaptionHeight;

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_captionHeight == CaptionHeight) return;
        _captionHeight = CaptionHeight;
        PerformLayout(); // caption and input heights follow the font chosen in Settings
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var color = Enabled ? Theme.Muted : SystemColors.GrayText;
        foreach (var cell in _cells)
            if (cell.Caption.Length > 0 && cell.CaptionBounds.IntersectsWith(e.ClipRectangle))
                TextRenderer.DrawText(e.Graphics, cell.Caption, CaptionFont, cell.CaptionBounds, color, flags);
    }
}

/// <summary>Helpers that create inputs and bind them to a property of the current record.</summary>
public static class Inputs
{
    public static KryptonTextBox Text(BindingSource bs, string property, bool readOnly = false, bool rightToLeft = false, bool multiline = false)
    {
        var box = new KryptonTextBox { ReadOnly = readOnly, Multiline = multiline, Height = multiline ? 54 : 25 };
        if (rightToLeft) box.RightToLeft = RightToLeft.Yes;
        var b = box.DataBindings.Add(nameof(box.Text), bs, property, true, DataSourceUpdateMode.OnValidation, "");
        b.DataSourceNullValue = null;
        return box;
    }

    /// <summary>A phone number box: shows (0300) 123-4321, saves the digits only.</summary>
    public static KryptonTextBox Phone(BindingSource bs, string property) => Number(bs, property, NumberFormat.Phone);

    /// <summary>A CNIC or Form-B box: shows 33100-1001001-7, saves the digits only.</summary>
    public static KryptonTextBox Cnic(BindingSource bs, string property) => Number(bs, property, NumberFormat.Cnic);

    /// <summary>
    /// A Form-B box: takes digits only, at most 13, and puts in the dashes while typing (33100-1001001-7). Saves it as shown,
    /// with dashes. A stored value that does not fit is shown as it is and left alone unless the user types in the box.
    /// </summary>
    public static KryptonTextBox FormB(BindingSource bs, string property)
    {
        var box = Text(bs, property);
        box.MaxLength = NumberFormat.FormBLength + 2;
        var b = box.DataBindings[nameof(box.Text)]!;
        b.Format += (_, e) => e.Value = NumberFormat.Cnic(e.Value as string);
        b.Parse += (_, e) =>
        {
            var text = (e.Value as string)?.Trim();
            e.Value = string.IsNullOrEmpty(text) ? null : NumberFormat.Digits(text).Length == NumberFormat.FormBLength ? NumberFormat.Cnic(text) : text;
        };
        box.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar)) return;
            var selected = NumberFormat.Digits(box.SelectedText).Length;
            e.Handled = !char.IsDigit(e.KeyChar) || NumberFormat.Digits(box.Text).Length - selected >= NumberFormat.FormBLength;
        };
        var formatting = false;
        box.TextChanged += (_, _) =>
        {
            if (formatting || !box.ContainsFocus) return; // only what the user types or pastes, not values being shown
            var digitsBeforeCaret = NumberFormat.Digits(box.Text[..box.SelectionStart]).Length;
            var digits = NumberFormat.Digits(box.Text);
            if (digits.Length > NumberFormat.FormBLength) digits = digits[..NumberFormat.FormBLength];
            var shown = NumberFormat.Cnic(digits);
            if (shown == box.Text) return;
            formatting = true;
            box.Text = shown;
            var caret = 0;
            for (var seen = 0; caret < shown.Length && seen < digitsBeforeCaret; caret++)
                if (char.IsDigit(shown[caret])) seen++;
            box.SelectionStart = caret;
            formatting = false;
        };
        return box;
    }

    private static KryptonTextBox Number(BindingSource bs, string property, Func<string?, string> format)
    {
        var box = Text(bs, property);
        var b = box.DataBindings[nameof(box.Text)]!;
        b.Format += (_, e) => e.Value = format(e.Value as string);
        b.Parse += (_, e) => e.Value = NumberFormat.Plain(e.Value as string);
        // Show the layout as soon as the user leaves the box, even before the record is saved.
        box.Leave += (_, _) => box.Text = format(NumberFormat.Plain(box.Text));
        return box;
    }

    public static KryptonDateTimePicker Date(BindingSource bs, string property)
    {
        var picker = new KryptonDateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true };
        picker.DataBindings.Add(nameof(picker.ValueNullable), bs, property, true, DataSourceUpdateMode.OnPropertyChanged);
        return picker;
    }

    public static KryptonCheckBox Check(BindingSource bs, string property, string text)
    {
        var box = new KryptonCheckBox { Text = text, AutoSize = true };
        box.DataBindings.Add(nameof(box.Checked), bs, property, true, DataSourceUpdateMode.OnPropertyChanged);
        return box;
    }

    /// <summary>
    /// Drop-down of (value, text) items bound to a nullable value property. An item with a null value is the blank choice.
    /// A combo box cannot select a null value, so blanks are shown through a stand-in value and saved back as null.
    /// </summary>
    /// <remarks>A standard ComboBox: a KryptonComboBox takes about 150 ms to create, which made forms slow to open.</remarks>
    public static ComboBox Combo<TValue>(BindingSource bs, string property, IEnumerable<(TValue? Value, string Text)> items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 220 };
        SetItems(combo, items);
        var b = combo.DataBindings.Add(nameof(combo.SelectedValue), bs, property, true, DataSourceUpdateMode.OnPropertyChanged, Blank);
        b.DataSourceNullValue = null;
        b.Parse += (_, e) => { if (Blank.Equals(e.Value)) e.Value = null; };
        return combo;
    }

    /// <summary>Replaces the choices of a drop-down made by <see cref="Combo"/>, keeping it bound to the same record.</summary>
    public static void SetItems<TValue>(ComboBox combo, IEnumerable<(TValue? Value, string Text)> items)
    {
        var list = items.Select(i => new Item<object>(i.Value is null ? Blank : i.Value, i.Text)).ToList();
        if (!list.Any(i => Blank.Equals(i.Value)))
            list.Insert(0, new Item<object>(Blank, ""));
        combo.DataSource = list;
        combo.ValueMember = nameof(Item<object>.Value);
        combo.DisplayMember = nameof(Item<object>.Text);
        foreach (Binding b in combo.DataBindings) b.ReadValue();
    }

    /// <summary>Stand-in for "no value" in drop-downs.</summary>
    private const string Blank = "\u0001blank";

    public sealed record Item<TValue>(TValue? Value, string Text);
}
