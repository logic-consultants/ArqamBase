using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>
/// Asked when adding a student: does a brother or sister already study here?
/// Returns the chosen existing family, or IsNewFamily for a new one.
/// </summary>
public sealed class PickFamilyForm : KryptonForm
{
    private readonly IReadOnlyList<Family> _families;
    private readonly KryptonTextBox _search = new() { Dock = DockStyle.Top, CueHint = { CueHintText = "Type father name, family ID, phone or CNIC" } };
    private readonly KryptonListBox _results = new() { Dock = DockStyle.Fill };
    private readonly KryptonButton _useFamily = new() { Text = "Add to this family", Width = 150, Enabled = false };
    private readonly KryptonButton _newFamily = new() { Text = "New family", Width = 120 };

    public Family? SelectedFamily { get; private set; }
    public bool IsNewFamily { get; private set; }

    public PickFamilyForm(IReadOnlyList<Family> families, string title = "New student")
    {
        _families = families;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => Core.Theme.FitButtons(this); // buttons as wide as their text at any font size
        Size = new Size(620, 520);
        MinimizeBox = false;

        var intro = new KryptonLabel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8),
            Text = "Does a brother or sister already study here?\nFind their family and add the new student to it. Otherwise choose New family."
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var cancel = new KryptonButton { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([cancel, _newFamily, _useFamily]);

        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        body.Controls.Add(_results);
        body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent });
        body.Controls.Add(_search);
        body.Controls.Add(intro);
        body.Controls.Add(buttons);
        Controls.Add(body);
        CancelButton = cancel;

        _search.TextChanged += (_, _) => Fill();
        _results.SelectedIndexChanged += (_, _) => _useFamily.Enabled = _results.SelectedItem is Row;
        _results.DoubleClick += (_, _) => UseSelected();
        _useFamily.Click += (_, _) => UseSelected();
        _newFamily.Click += (_, _) => { IsNewFamily = true; DialogResult = DialogResult.OK; Close(); };
        Shown += (_, _) => _search.Focus();
    }

    private void UseSelected()
    {
        if (_results.SelectedItem is not Row row) return;
        SelectedFamily = row.Family;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Fill()
    {
        var term = _search.Text.Trim();
        _results.BeginUpdate();
        _results.Items.Clear();
        if (term.Length >= 2)
        {
            var digits = new string(term.Where(char.IsDigit).ToArray());
            foreach (var f in _families.Where(f => Matches(f, term, digits)).OrderBy(f => f.FatherName).Take(100))
                _results.Items.Add(new Row(f));
        }
        _results.EndUpdate();
        if (_results.Items.Count > 0) _results.SelectedIndex = 0;
    }

    private static bool Matches(Family f, string term, string digits) =>
        f.Id.ToString() == term
        || (f.FatherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (f.MotherName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || (digits.Length >= 4 && new[] { f.Mobile, f.MotherMobile, f.PhoneHome, f.Cnic }.Any(p => p is not null && new string(p.Where(char.IsDigit).ToArray()).Contains(digits)));

    private sealed record Row(Family Family)
    {
        public override string ToString()
        {
            var kids = string.Join(", ", Family.Students.Where(s => s.OnRoll).Select(s => s.Name));
            return $"{Family.Id}  ·  {Family.FatherName}  ·  {Core.NumberFormat.Phone(Family.Mobile)}{(kids.Length > 0 ? $"  ·  children: {kids}" : "")}";
        }
    }
}
