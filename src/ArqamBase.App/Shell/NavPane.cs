using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>Access-style navigation pane: grouped list of screens with search, collapsible to a thin strip.</summary>
public sealed class NavPane : KryptonPanel
{
    private const int CollapsedWidth = 34;

    private readonly KryptonPanel _header = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 4, 4) };
    private readonly KryptonLabel _title = new() { Text = "All screens", Dock = DockStyle.Fill, LabelStyle = LabelStyle.BoldControl };
    private readonly KryptonButton _toggle = new() { Dock = DockStyle.Right, Width = 28, ButtonStyle = ButtonStyle.ButtonSpec, Text = "«" };
    private readonly KryptonTextBox _search = new() { Dock = DockStyle.Top, CueHint = { CueHintText = "Search screens" } };
    private readonly KryptonTreeView _tree = new() { Dock = DockStyle.Fill, ShowLines = false, ShowRootLines = false, FullRowSelect = true, HotTracking = true, ItemHeight = Theme.RowHeight };
    private readonly KryptonPanel _searchHost = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 2, 8, 6) };
    private readonly ImageList _images = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    private int _expandedWidth;
    private string? _activeKey;

    public event Action<string>? ScreenSelected;
    public event EventHandler? CollapsedChanged;

    public bool Collapsed { get; private set; }

    public NavPane(int width)
    {
        _expandedWidth = Math.Max(180, width);
        Width = _expandedWidth;
        Dock = DockStyle.Left;
        Padding = new Padding(0, 0, 1, 0);

        _tree.ImageList = _images;
        _tree.NodeMouseClick += (_, e) => Open(e.Node);
        _tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Open(_tree.SelectedNode); };
        _search.TextChanged += (_, _) => Fill();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            var first = _tree.Nodes.Cast<TreeNode>().SelectMany(g => g.Nodes.Cast<TreeNode>()).FirstOrDefault();
            if (first is not null) Open(first);
        };
        _toggle.Click += (_, _) => SetCollapsed(!Collapsed);
        _toggle.ToolTipValues.Description = "Show or hide the navigation pane";
        _toggle.ToolTipValues.EnableToolTips = true;

        _header.Controls.Add(_title);
        _header.Controls.Add(_toggle);
        var searchHost = _searchHost;
        searchHost.Controls.Add(_search);
        Controls.Add(_tree);
        Controls.Add(searchHost);
        Controls.Add(_header);

        Theme.Changed += (_, _) => { ApplyColours(); Fill(); };
        ApplyColours();
        Fill();
    }

    public int ExpandedWidth => _expandedWidth;

    public void SetCollapsed(bool collapsed)
    {
        if (Collapsed == collapsed) return;
        if (!Collapsed) _expandedWidth = Width;
        Collapsed = collapsed;
        Width = collapsed ? CollapsedWidth : _expandedWidth;
        _title.Visible = _search.Parent!.Visible = _tree.Visible = !collapsed;
        _toggle.Text = collapsed ? "»" : "«";
        _toggle.Dock = collapsed ? DockStyle.Fill : DockStyle.Right;
        CollapsedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActive(string? key)
    {
        _activeKey = key;
        foreach (TreeNode g in _tree.Nodes)
            foreach (TreeNode n in g.Nodes)
            {
                var active = (string)n.Tag! == key;
                n.NodeFont = active ? new Font(_tree.Font, FontStyle.Bold) : null;
                if (active) _tree.SelectedNode = n; // the open screen shows as the lighter band
            }
    }

    public void FocusSearch()
    {
        SetCollapsed(false);
        _search.Focus();
    }

    private void Open(TreeNode? node)
    {
        if (node?.Tag is string key)
            ScreenSelected?.Invoke(key);
    }

    private void Fill()
    {
        _images.Images.Clear();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        var term = _search.Text.Trim();
        foreach (var group in ScreenCatalog.All.GroupBy(s => s.Group))
        {
            var items = group.Where(s => term.Length == 0 || s.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (items.Count == 0) continue;

            var gNode = new TreeNode(group.Key.ToUpperInvariant()) { ForeColor = Muted, ImageKey = "none", SelectedImageKey = "none" };
            foreach (var s in items)
            {
                var imageKey = s.Glyph.ToString();
                if (!_images.Images.ContainsKey(imageKey))
                    _images.Images.Add(imageKey, Theme.ColouredNavigation ? Icons.Get(s.Glyph, 16, Theme.NavText) : Icons.Small(s.Glyph));
                var text = s.Create is null ? $"{s.Title}  ·  phase {s.Phase}" : s.Title;
                gNode.Nodes.Add(new TreeNode(text) { Tag = s.Key, ImageKey = imageKey, SelectedImageKey = imageKey, ForeColor = s.Create is null ? Muted : Color.Empty });
            }
            _tree.Nodes.Add(gNode);
        }
        _images.Images.Add("none", new Bitmap(1, 1));
        _tree.ExpandAll();
        _tree.EndUpdate();
        SetActive(_activeKey);
    }

    private static Color Muted => Theme.ColouredNavigation ? Theme.NavMuted : Theme.Muted;

    /// <summary>
    /// Flat themes: the pane has the primary colour with white text and icons, and a lighter band for the open screen.
    /// Classic 2007 Blue keeps the palette's own light pane.
    /// </summary>
    private void ApplyColours()
    {
        var on = Theme.ColouredNavigation;
        var back = on ? Theme.NavBack : Color.Empty;
        var text = on ? Theme.NavText : Color.Empty;
        foreach (var panel in new[] { this, _header, _searchHost })
        {
            panel.StateCommon.ColorStyle = on ? PaletteColorStyle.Solid : PaletteColorStyle.Inherit;
            panel.StateCommon.Color1 = panel.StateCommon.Color2 = back;
        }
        _title.StateCommon.ShortText.Color1 = text;
        _toggle.StateCommon.Content.ShortText.Color1 = text;

        _tree.StateCommon.Back.ColorStyle = on ? PaletteColorStyle.Solid : PaletteColorStyle.Inherit;
        _tree.StateCommon.Back.Color1 = _tree.StateCommon.Back.Color2 = back;
        _tree.StateCommon.Border.DrawBorders = on ? PaletteDrawBorders.None : PaletteDrawBorders.Inherit;
        _tree.StateCommon.Node.Content.ShortText.Color1 = text;
        void Node(PaletteTriple node, Color color)
        {
            node.Back.ColorStyle = on ? PaletteColorStyle.Solid : PaletteColorStyle.Inherit;
            node.Back.Color1 = node.Back.Color2 = on ? color : Color.Empty;
            node.Border.DrawBorders = on ? PaletteDrawBorders.None : PaletteDrawBorders.Inherit;
            node.Content.ShortText.Color1 = text;
        }
        var hover = Theme.Mix(Theme.NavBack, Color.White, 0.12);
        Node(_tree.StateTracking.Node, hover);
        Node(_tree.StateCheckedNormal.Node, Theme.NavSelected);
        Node(_tree.StateCheckedTracking.Node, Theme.NavSelected);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_tree.ItemHeight != Theme.RowHeight) _tree.ItemHeight = Theme.RowHeight;
    }
}
