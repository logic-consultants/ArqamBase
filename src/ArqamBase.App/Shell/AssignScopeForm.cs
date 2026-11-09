using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>
/// Admin ticks what a user looks after: departments for a coordinator, or classes and sections for a teacher in charge.
/// </summary>
public sealed class AssignScopeForm : KryptonForm
{
    private readonly CheckedListBox _list = new() { Width = 380, Height = 360, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
    private readonly IReadOnlyList<(int Id, int? SectionId, string Text)> _items;

    /// <summary>The ticked items: a department ID (section empty), or a class ID and section ID.</summary>
    public IEnumerable<(int Id, int? SectionId)> Chosen =>
        _list.CheckedIndices.Cast<int>().Select(i => (_items[i].Id, _items[i].SectionId));

    public AssignScopeForm(string userName, string what, IReadOnlyList<(int Id, int? SectionId, string Text)> items, IEnumerable<(int Id, int? SectionId)> chosen)
    {
        _items = items;
        Text = $"Assign {what} to {userName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => Core.Theme.FitButtons(this);
        _list.Font = Core.Theme.BaseFont;
        var ticked = chosen.ToHashSet();
        for (var i = 0; i < items.Count; i++)
            _list.Items.Add(items[i].Text, ticked.Contains((items[i].Id, items[i].SectionId)));

        var t = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        t.Controls.Add(new KryptonLabel { Text = $"Tick the {what} {userName} marks attendance for.", AutoSize = true, MaximumSize = new Size(380, 0) });
        t.Controls.Add(_list);
        var ok = new KryptonButton { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
        var cancel = new KryptonButton { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        t.Controls.Add(buttons);
        Controls.Add(t);
        ClientSize = t.GetPreferredSize(Size.Empty) + new Size(0, 20);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
