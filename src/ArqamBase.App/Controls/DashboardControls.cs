using System.Drawing.Drawing2D;
using ArqamBase.App.Core;

namespace ArqamBase.App.Controls;

/// <summary>A dashboard figure: a caption with a small icon, a large value and a line under it. Opens its screen when clicked.</summary>
public sealed class DashTile : Control
{
    private static Font CaptionFont => Theme.Scaled(0.9f);
    private static Font ValueFont => Theme.Scaled(1.6f, FontStyle.Bold);
    private static Font NoteFont => Theme.Scaled(0.9f);
    private readonly char _glyph;
    private string _value = "…", _note = "";
    private Color? _valueColor;
    private bool _hover;

    public DashTile(string caption, char glyph, Action? open)
    {
        Text = caption;
        _glyph = glyph;
        Open = open;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.PushButton;
        if (open is not null) { Cursor = Cursors.Hand; TabStop = true; }
        SizeToFont();
    }

    public Action? Open { get; }

    public void SizeToFont() => Height = 10 + CaptionFont.Height + 2 + ValueFont.Height + 2 + NoteFont.Height + 10;

    public void SetValue(string value, string note, Color? color = null)
    {
        (_value, _note, _valueColor) = (value, note, color);
        AccessibleDescription = $"{value}. {note}";
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = Open is not null; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); Open?.Invoke(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) Open?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Card.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6))
        using (var fill = new SolidBrush(Theme.CardBack))
        using (var pen = new Pen(_hover || Focused ? Theme.Accent : Theme.CardBorder, _hover || Focused ? 1.5f : 1f))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        var left = 12;
        var y = 10;
        var icon = Icons.Get(_glyph, CaptionFont.Height, Theme.Accent);
        g.DrawImage(icon, left, y);
        var w = Width - left - 12;
        TextRenderer.DrawText(g, Text, CaptionFont, new Rectangle(left + icon.Width + 6, y, w - icon.Width - 6, CaptionFont.Height), Theme.Muted, flags);
        y += CaptionFont.Height + 2;
        TextRenderer.DrawText(g, _value, ValueFont, new Rectangle(left, y, w, ValueFont.Height), _valueColor ?? Theme.Text, flags);
        y += ValueFont.Height + 2;
        TextRenderer.DrawText(g, _note, NoteFont, new Rectangle(left, y, w, NoteFont.Height), Theme.Muted, flags);
    }
}

/// <summary>Places its children in as many equal columns as fit (each at least <see cref="MinWidth"/> wide), wrapping to more rows.</summary>
public sealed class TileGrid : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MinWidth { get; set; } = 170;

    public TileGrid()
    {
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
    }

    private int _laidOutWidth = -1;

    /// <summary>A width change during the parent's layout (a scroll bar appearing) is placed again once that layout is done.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width == _laidOutWidth || !IsHandleCreated) return;
        BeginInvoke(() => { if (!IsDisposed) PerformLayout(); });
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _laidOutWidth = Width;
        var items = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (items.Count == 0) { if (Height != 0) Height = 0; return; }
        var columns = Math.Clamp((ClientSize.Width + Card.Gap) / (MinWidth + Card.Gap), 1, items.Count);
        var rows = (items.Count + columns - 1) / columns;
        columns = (items.Count + rows - 1) / rows; // even rows: 6 tiles become 3 + 3, not 5 + 1
        var width = (ClientSize.Width - Card.Gap * (columns - 1)) / (double)columns;
        var y = 0;
        for (var r = 0; r < rows; r++)
        {
            var row = items.Skip(r * columns).Take(columns).ToList();
            var h = row.Max(c => c.Height);
            for (var i = 0; i < row.Count; i++)
                row[i].SetBounds((int)(i * (width + Card.Gap)), y, (int)width, h);
            y += h + Card.Gap;
        }
        if (Height != y) Height = y;
    }
}

/// <summary>Places its children side by side with widths in the given proportions; below <see cref="StackBelow"/> pixels they go one under the other.</summary>
public sealed class RatioColumns : Panel
{
    private readonly float[] _weights;

    public RatioColumns(params float[] weights)
    {
        _weights = weights;
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int StackBelow { get; set; } = 820;

    private int _laidOutWidth = -1;

    /// <summary>A width change during the parent's layout (a scroll bar appearing) is placed again once that layout is done.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width == _laidOutWidth || !IsHandleCreated) return;
        BeginInvoke(() => { if (!IsDisposed) PerformLayout(); });
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        _laidOutWidth = Width;
        var items = Controls.Cast<Control>().Where(c => c.Visible).OrderBy(c => c.TabIndex).ToList();
        if (items.Count == 0) { if (Height != 0) Height = 0; return; }
        int height;
        if (ClientSize.Width < StackBelow || items.Count == 1)
        {
            var y = 0;
            foreach (var c in items) { c.SetBounds(0, y, ClientSize.Width, c.Height); y += c.Height; }
            height = y;
        }
        else
        {
            var weights = items.Select((_, i) => i < _weights.Length ? _weights[i] : 1f).ToArray();
            var room = ClientSize.Width - Card.Gap * (items.Count - 1);
            var x = 0.0;
            for (var i = 0; i < items.Count; i++)
            {
                var w = room * weights[i] / weights.Sum();
                items[i].SetBounds((int)x, 0, (int)w, items[i].Height);
                x += w + Card.Gap;
            }
            height = items.Max(c => c.Height);
        }
        if (Height != height) Height = height;
    }
}

/// <summary>One bar per day of a month (fee received), with the day numbers under it. Sundays' numbers are bold; hovering shows the amount.</summary>
public sealed class DayBarChart : Control
{
    private readonly ToolTip _tip = new();
    private DateTime _month = DateTime.Today;
    private long[] _values = [];
    private int _hoverDay = -1;

    public DayBarChart()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 170;
        AccessibleRole = AccessibleRole.Chart;
    }

    /// <summary>Amounts for day 1 to the last day of <paramref name="month"/>.</summary>
    public void SetData(DateTime month, long[] values)
    {
        _month = new DateTime(month.Year, month.Month, 1);
        _values = values;
        var best = values.Select((v, i) => (v, d: i + 1)).Where(x => x.v > 0).OrderByDescending(x => x.v).Take(2).ToList();
        AccessibleName = $"Fee received each day of {_month:MMMM yyyy}" + (best.Count > 0 ? $"; most on the {string.Join(" and ", best.Select(b => Ordinal(b.d)))}." : "; none.");
        Invalidate();
    }

    private static string Ordinal(int d) => d + (d is 11 or 12 or 13 ? "th" : (d % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    private Font DayFont => Theme.Scaled(0.75f);
    private Font DayBold => Theme.Scaled(0.75f, FontStyle.Bold);

    private (Rectangle plot, double step) PlotArea()
    {
        var bottom = Height - DayFont.Height - 6;
        var plot = new Rectangle(0, 6, Width, Math.Max(10, bottom - 6));
        return (plot, _values.Length == 0 ? 0 : plot.Width / (double)_values.Length);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (_, step) = PlotArea();
        var day = step <= 0 ? -1 : (int)(e.X / step) + 1;
        if (day < 1 || day > _values.Length) day = -1;
        if (day == _hoverDay) return;
        _hoverDay = day;
        Invalidate();
        if (day > 0) _tip.Show($"{_month.AddDays(day - 1):ddd d MMM}: Rs {_values[day - 1]:N0}", this, e.X + 12, e.Y + 12, 4000);
        else _tip.Hide(this);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverDay = -1;
        _tip.Hide(this);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var (plot, step) = PlotArea();
        using (var axis = new Pen(Theme.CardBorder)) g.DrawLine(axis, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        if (_values.Length == 0) return;
        var max = Math.Max(1, _values.Max());
        var gap = Math.Max(1, (int)(step * 0.18));
        using var bar = new SolidBrush(Theme.Accent);
        using var hot = new SolidBrush(Theme.Mix(Theme.Accent, Theme.Text, 0.35));
        for (var i = 0; i < _values.Length; i++)
        {
            var x = (int)(i * step) + gap / 2;
            var w = Math.Max(1, (int)step - gap);
            var h = (int)Math.Round(_values[i] / (double)max * plot.Height);
            if (h > 0) g.FillRectangle(i + 1 == _hoverDay ? hot : bar, x, plot.Bottom - h, w, h);
            var day = _month.AddDays(i);
            var sunday = day.DayOfWeek == DayOfWeek.Sunday;
            TextRenderer.DrawText(g, (i + 1).ToString(), sunday ? DayBold : DayFont, new Rectangle((int)(i * step), plot.Bottom + 3, (int)Math.Ceiling(step), DayFont.Height + 2),
                sunday ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

public enum AttentionKind { Warning, Info, Done }

/// <summary>One line of the dashboard's "Needs attention" list.</summary>
public sealed record AttentionItem(AttentionKind Kind, string Title, string Detail, string Action, Action? Run);

/// <summary>The "Needs attention" list: a coloured mark, a title with a muted line under it, and a link at the right.</summary>
public sealed class AttentionList : Control
{
    private IReadOnlyList<AttentionItem> _items = [];
    private readonly List<(Rectangle Link, AttentionItem Item)> _links = [];
    private int _focus = -1;

    public AttentionList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Needs attention";
    }

    private static Font TitleFont => Theme.BaseFont;
    private static Font DetailFont => Theme.Scaled(0.9f);
    private static Font LinkFont => Theme.Scaled(1f, FontStyle.Bold);
    private static int RowHeight => TitleFont.Height + DetailFont.Height + 16;

    public void SetItems(IReadOnlyList<AttentionItem> items)
    {
        _items = items;
        _focus = -1;
        Height = Math.Max(RowHeight, items.Count * RowHeight);
        AccessibleDescription = string.Join(". ", items.Select(i => $"{i.Title}, {i.Detail}"));
        Invalidate();
    }

    private AttentionItem? LinkAt(Point p) => _links.FirstOrDefault(l => l.Link.Contains(p)).Item;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = LinkAt(e.Location)?.Run is not null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        LinkAt(e.Location)?.Run?.Invoke();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var runnable = _items.Select((item, i) => (item, i)).Where(x => x.item.Run is not null).Select(x => x.i).ToList();
        if (runnable.Count == 0) return;
        if (e.KeyCode == Keys.Down) _focus = runnable.FirstOrDefault(i => i > _focus, runnable[0]);
        else if (e.KeyCode == Keys.Up) _focus = runnable.LastOrDefault(i => i < _focus, runnable[^1]);
        else if (e.KeyCode is Keys.Enter or Keys.Space && _focus >= 0) _items[_focus].Run?.Invoke();
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_focus < 0) _focus = _items.Select((item, i) => (item, i)).FirstOrDefault(x => x.item.Run is not null, (null!, -1)).i;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _links.Clear();
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, "Nothing needs attention.", TitleFont, ClientRectangle, Theme.Muted, flags);
            return;
        }
        using var line = new Pen(Theme.CardBorder);
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var top = i * RowHeight;
            var (fore, glyph) = item.Kind switch
            {
                AttentionKind.Warning => (Theme.Warning, '!'),
                AttentionKind.Done => (Theme.Good, '✓'),
                _ => (Theme.Accent, 'i'),
            };
            var d = TitleFont.Height + 4;
            var circle = new Rectangle(0, top + (RowHeight - d) / 2, d, d);
            using (var back = new SolidBrush(Theme.Mix(fore, Theme.CardBack, 0.82))) g.FillEllipse(back, circle);
            TextRenderer.DrawText(g, glyph.ToString(), Theme.Scaled(0.9f, FontStyle.Bold), circle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var linkSize = TextRenderer.MeasureText(item.Action, LinkFont);
            var link = new Rectangle(Width - linkSize.Width - 2, top + (RowHeight - linkSize.Height) / 2, linkSize.Width, linkSize.Height);
            if (item.Run is not null)
            {
                TextRenderer.DrawText(g, item.Action, LinkFont, link, Theme.Accent, TextFormatFlags.NoPrefix);
                _links.Add((Rectangle.Inflate(link, 4, 6), item));
                if (Focused && i == _focus) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(link, 3, 2));
            }
            var textLeft = d + 10;
            var textWidth = link.Left - textLeft - 10;
            var y = top + 8;
            TextRenderer.DrawText(g, item.Title, TitleFont, new Rectangle(textLeft, y, textWidth, TitleFont.Height), Theme.Text, flags);
            TextRenderer.DrawText(g, item.Detail, DetailFont, new Rectangle(textLeft, y + TitleFont.Height, textWidth, DetailFont.Height), Theme.Muted, flags);
            if (i < _items.Count - 1) g.DrawLine(line, 0, top + RowHeight - 1, Width, top + RowHeight - 1);
        }
    }
}
