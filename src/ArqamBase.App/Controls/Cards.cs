using System.Drawing.Drawing2D;
using ArqamBase.App.Core;

namespace ArqamBase.App.Controls;

/// <summary>
/// A panel docked to the top whose height follows its contents, so boxes can be stacked and the screen scrolls as a whole.
/// Add children with <see cref="AddTop"/> (in reading order).
/// </summary>
public class StackPanel : Panel
{
    public StackPanel()
    {
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
    }

    /// <summary>Adds controls one under the other, in the order given.</summary>
    public void AddTop(params Control[] controls)
    {
        foreach (var c in controls)
        {
            c.Dock = DockStyle.Top;
            Controls.Add(c);
            c.BringToFront(); // docking places the last control in the z-order first, so this keeps reading order
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var bottom = Padding.Top;
        foreach (Control c in Controls)
            if (c.Visible && c.Dock == DockStyle.Top) bottom = Math.Max(bottom, c.Bottom);
        var height = bottom + Padding.Bottom;
        if (Height != height) Height = height;
    }
}

/// <summary>A white box with a rounded border and an optional title, as in the screen designs. Leaves a gap below itself.</summary>
public class Card : StackPanel
{
    public const int Gap = 12;
    private const int Radius = 6;
    private static Font TitleFont => Theme.Scaled(1.05f, FontStyle.Bold);
    private readonly string _title;
    private Control? _headerControl;

    public Card(string title = "")
    {
        _title = title;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        SizeToFont();
    }

    /// <summary>Sets the room for the title (and, in derived boxes, the height) from the font chosen in Settings.</summary>
    protected virtual void SizeToFont() =>
        Padding = new Padding(16, _title.Length > 0 ? TitleFont.Height + 20 : 12, 16, 12 + Gap);

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        var (padding, height) = (Padding, Height);
        SizeToFont();
        if (Padding == padding && Height == height) return;
        PerformLayout();
        Invalidate();
    }

    /// <summary>A control (e.g. a button) at the right of the title line.</summary>
    public void SetHeaderControl(Control control)
    {
        _headerControl = control;
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(control);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_headerControl is { } c)
            c.Location = new Point(Width - Padding.Right - c.Width, Math.Max(4, (Padding.Top - c.Height) / 2));
    }

    private Rectangle Box => new(0, 0, Width - 1, Height - 1 - Gap);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(Box, Radius);
        using var fill = new SolidBrush(Theme.CardBack);
        using var pen = new Pen(Theme.CardBorder);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_title.Length > 0)
            TextRenderer.DrawText(e.Graphics, _title, TitleFont, new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Padding.Top),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    internal static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>A small box with a caption and one large figure, e.g. "Family balance / Rs 8,600 due".</summary>
public sealed class StatCard : Card
{
    private static Font CaptionFont => Theme.Scaled(0.9f);
    private static Font ValueFont => Theme.Scaled(1.5f, FontStyle.Bold);
    private readonly string _caption;
    private string _value = "";
    private Color? _valueColor;

    public StatCard(string caption)
    {
        _caption = caption;
        Dock = DockStyle.None;
        SizeToFont();
    }

    protected override void SizeToFont()
    {
        Padding = new Padding(16, 0, 16, Card.Gap);
        Height = 10 + CaptionFont.Height + 4 + ValueFont.Height + 10 + Card.Gap;
    }

    public void SetValue(string value, Color? color = null)
    {
        _value = value;
        _valueColor = color;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs levent) { } // fixed height

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var w = Width - Padding.Horizontal;
        TextRenderer.DrawText(e.Graphics, _caption, CaptionFont, new Rectangle(Padding.Left, 10, w, CaptionFont.Height), Theme.Muted, flags);
        TextRenderer.DrawText(e.Graphics, _value, ValueFont, new Rectangle(Padding.Left, 14 + CaptionFont.Height, w, ValueFont.Height), _valueColor ?? Theme.Text, flags);
    }
}

/// <summary>Places its children side by side in equal widths, with a gap between them.</summary>
public sealed class EqualColumns : Panel
{
    public EqualColumns()
    {
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var items = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (items.Count == 0) return;
        var width = (ClientSize.Width - Card.Gap * (items.Count - 1)) / (double)items.Count;
        for (var i = 0; i < items.Count; i++)
            items[i].SetBounds((int)(i * (width + Card.Gap)), 0, (int)width, items[i].Height);
        var height = items.Max(c => c.Height);
        if (Height != height) Height = height;
    }
}
