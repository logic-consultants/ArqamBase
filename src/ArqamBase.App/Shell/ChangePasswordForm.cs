using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>Changes the signed-in user's own app password. "Required" is used after an Admin sets a temporary password.</summary>
public sealed class ChangePasswordForm : KryptonForm
{
    private readonly KryptonTextBox _current = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly KryptonTextBox _new = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly KryptonTextBox _confirm = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly KryptonLabel _error = new() { Text = "", AutoSize = true, MaximumSize = new Size(400, 0), StateCommon = { ShortText = { Color1 = Color.Firebrick } } };

    public ChangePasswordForm(AppConfig config, UserSession session, bool required)
    {
        Text = "Change password";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Shown += (_, _) => Core.Theme.FitButtons(this); // buttons as wide as their text at any font size
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        var intro = required
            ? "An Admin set a temporary password for you. Choose your own password to continue."
            : $"Signed in as {session.UserName}.";
        t.Controls.Add(new KryptonLabel { Text = intro, AutoSize = true, MaximumSize = new Size(400, 0) });
        t.SetColumnSpan(t.Controls[0], 2);
        if (!required)
            Row(t, "Current password", _current);
        Row(t, "New password", _new);
        Row(t, "Confirm", _confirm);
        t.Controls.Add(new KryptonLabel { Text = $"At least {PasswordHasher.MinLength} characters, with letters and numbers.", AutoSize = true });
        t.SetColumnSpan(t.Controls[^1], 2);
        t.Controls.Add(_error);
        t.SetColumnSpan(_error, 2);

        var ok = new KryptonButton { Text = "Change", Width = 100 };
        var cancel = new KryptonButton { Text = required ? "Exit" : "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
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
            if (_new.Text != _confirm.Text)
            {
                _error.Text = "The new passwords do not match.";
                return;
            }
            try
            {
                _error.Text = SignIn.ChangePassword(config, session, required ? null : _current.Text, _new.Text) ?? "";
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                _error.Text = ex.Message;
            }
            if (_error.Text.Length > 0) return;
            KryptonMessageBox.Show(this, "Your password is changed.", Text, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    private static void Row(TableLayoutPanel t, string label, Control box)
    {
        t.Controls.Add(new KryptonLabel { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(box);
    }
}
