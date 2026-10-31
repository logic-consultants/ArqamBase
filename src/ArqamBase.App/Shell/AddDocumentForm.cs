using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>Picks a file and its type to attach to a student.</summary>
public sealed class AddDocumentForm : KryptonForm
{
    private readonly KryptonComboBox _type = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonTextBox _file = new() { Width = 300, ReadOnly = true };
    private readonly KryptonTextBox _notes = new() { Width = 300 };
    private readonly KryptonLabel _error = new() { Text = "", AutoSize = true, MaximumSize = new Size(420, 0), StateCommon = { ShortText = { Color1 = Color.Firebrick } } };

    public string DocType => ((Inputs.Item<string>)_type.SelectedItem!).Value!;
    public string FilePath => _file.Text;
    public string Notes => _notes.Text;

    public AddDocumentForm(string studentName, string? initialType = null)
    {
        Text = $"Add picture or document – {studentName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var types = DocumentTypes.All.Select(t => new Inputs.Item<string>(t.Type, t.Label)).ToList();
        _type.DataSource = types;
        _type.DisplayMember = nameof(Inputs.Item<string>.Text);
        _type.SelectedItem = types.FirstOrDefault(t => t.Value == initialType) ?? types[0];

        var browse = new KryptonButton { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => Browse();

        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        t.Controls.Add(new KryptonLabel { Text = "Type", AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(_type);
        t.SetColumnSpan(_type, 2);
        t.Controls.Add(new KryptonLabel { Text = "File", AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(_file);
        t.Controls.Add(browse);
        t.Controls.Add(new KryptonLabel { Text = "Notes", AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(_notes);
        t.SetColumnSpan(_notes, 2);
        var hint = new KryptonLabel
        {
            Text = "Pictures over 1 MB are made smaller automatically. PDF, Word and Excel documents can be up to 3 MB.",
            AutoSize = true, MaximumSize = new Size(420, 0), StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        t.Controls.Add(hint);
        t.SetColumnSpan(hint, 3);
        t.Controls.Add(_error);
        t.SetColumnSpan(_error, 3);

        var ok = new KryptonButton { Text = "Add", Width = 90 };
        var cancel = new KryptonButton { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        t.Controls.Add(buttons);
        t.SetColumnSpan(buttons, 3);
        Controls.Add(t);
        ClientSize = t.GetPreferredSize(Size.Empty) + new Size(0, 40);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            _error.Text = _file.Text.Length == 0 ? "Choose a file."
                : DocType == DocumentTypes.ProfilePicture && !StudentDocumentStore.IsPicture(_file.Text) ? "The profile picture must be a picture."
                : "";
            if (_error.Text.Length > 0) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += (_, _) => { if (_file.Text.Length == 0) Browse(); };
    }

    private void Browse()
    {
        var pictures = string.Join(";", StudentDocumentStore.PictureExtensions.Select(e => "*" + e));
        var documents = string.Join(";", StudentDocumentStore.DocumentExtensions.Select(e => "*" + e));
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the picture or document",
            Filter = DocType == DocumentTypes.ProfilePicture
                ? $"Pictures|{pictures}"
                : $"Pictures and documents|{pictures};{documents}|Pictures|{pictures}|Documents|{documents}",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _file.Text = dlg.FileName;
    }
}
