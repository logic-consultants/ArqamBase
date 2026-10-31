using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>Admin sets a temporary password; the user must change it at their next sign-in.</summary>
public sealed class SetPasswordForm : KryptonForm
{
    private readonly KryptonTextBox _password = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly KryptonTextBox _confirm = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly KryptonLabel _error = new() { Text = "", AutoSize = true, MaximumSize = new Size(380, 0), StateCommon = { ShortText = { Color1 = Color.Firebrick } } };

    public string Password => _password.Text;

    public SetPasswordForm(string userName)
    {
        Text = $"Set password for {userName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        var intro = new KryptonLabel { Text = "This is a temporary password. The user chooses their own when they next sign in.", AutoSize = true, MaximumSize = new Size(380, 0) };
        t.Controls.Add(intro);
        t.SetColumnSpan(intro, 2);
        t.Controls.Add(new KryptonLabel { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(_password);
        t.Controls.Add(new KryptonLabel { Text = "Confirm", AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(_confirm);
        t.Controls.Add(_error);
        t.SetColumnSpan(_error, 2);

        var ok = new KryptonButton { Text = "OK", Width = 90 };
        var cancel = new KryptonButton { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        t.Controls.Add(buttons);
        t.SetColumnSpan(buttons, 2);
        Controls.Add(t);
        ClientSize = t.GetPreferredSize(Size.Empty) + new Size(0, 40);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            _error.Text = _password.Text != _confirm.Text ? "The passwords do not match." : PasswordHasher.Weakness(_password.Text) ?? "";
            if (_error.Text.Length > 0) return;
            DialogResult = DialogResult.OK;
            Close();
        };
    }
}
