using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>
/// Sign-in window: app user name and password, plus the Windows account used to connect to SQL Server.
/// The SQL Server login can be remembered on this computer per app user.
/// </summary>
public sealed class LoginForm : KryptonForm
{
    private readonly AppConfig _config;
    private readonly UserSettings _settings;
    private readonly KryptonTextBox _user = new() { Dock = DockStyle.Fill };
    private readonly KryptonTextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly KryptonTextBox _sqlUser = new() { Dock = DockStyle.Fill };
    private readonly KryptonTextBox _sqlPassword = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly KryptonCheckBox _remember = new() { Text = "Remember on this computer", Checked = true, AutoSize = true };
    private readonly KryptonLabel _savedInfo = new() { AutoSize = true };
    private readonly KryptonLinkLabel _changeSql = new() { Text = "Use a different login", AutoSize = true };
    private readonly KryptonLabel _error = new() { Text = "", AutoSize = true, MaximumSize = new Size(420, 0), StateCommon = { ShortText = { Color1 = Color.Firebrick } } };
    private readonly KryptonButton _signIn = new() { Text = "Sign in", Width = 100, Height = 30 };
    private readonly KryptonButton _cancel = new() { Text = "Cancel", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
    private readonly TableLayoutPanel _sqlFields = new() { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent };
    private readonly FlowLayoutPanel _sqlSaved = new() { AutoSize = true, Dock = DockStyle.Top, BackColor = Color.Transparent, WrapContents = false };
    private readonly TableLayoutPanel _root;
    private KryptonGroupBox? _sqlGroup;
    private SqlLogin? _saved;

    /// <summary>Krypton forms do not auto-size, so size the window to its content.</summary>
    private void FitToContent()
    {
        _root.PerformLayout();
        ClientSize = _root.GetPreferredSize(Size.Empty);
    }

    public UserSession? Session { get; private set; }

    public LoginForm(AppConfig config, UserSettings settings)
    {
        _config = config;
        _settings = settings;
        Text = "Sign in to ArqamBase";
        Icon = Program.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AcceptButton = _signIn;
        CancelButton = _cancel;

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), BackColor = Color.Transparent };
        _root = root;

        root.Controls.Add(new KryptonLabel { Text = "ArqamBase", LabelStyle = LabelStyle.TitlePanel, AutoSize = true });
        root.Controls.Add(new KryptonLabel { Text = $"{config.Database} on {config.Server}", AutoSize = true, Padding = new Padding(0, 0, 0, 10) });

        var app = new KryptonGroupBox { Text = "1. Your ArqamBase account", Width = 440, Height = 110 };
        app.Panel.Controls.Add(Fields(("User name", _user), ("Password", _password)));
        root.Controls.Add(app);

        if (config.UsesWindowsLogin)
        {
            var sql = new KryptonGroupBox { Text = $"2. Your Windows login on {config.ServerMachine} (for SQL Server)", Width = 440, Height = 150 };
            _sqlGroup = sql;
            var holder = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _sqlFields.Controls.Clear();
            AddRow(_sqlFields, "Windows user", _sqlUser);
            AddRow(_sqlFields, "Password", _sqlPassword);
            _sqlFields.Controls.Add(new Label { AutoSize = true });
            _sqlFields.Controls.Add(_remember);
            _sqlSaved.Controls.Add(_savedInfo);
            _sqlSaved.Controls.Add(_changeSql);
            holder.Controls.Add(_sqlFields);
            holder.Controls.Add(_sqlSaved);
            sql.Panel.Controls.Add(holder);
            root.Controls.Add(sql);
            _sqlUser.CueHint.CueHintText = $"for example {config.ServerMachine}\\feeclerk1";
            _changeSql.LinkClicked += (_, _) => ShowSqlFields(true);
        }

        root.Controls.Add(_error);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_signIn);
        root.Controls.Add(buttons);
        Controls.Add(root);
        FitToContent();

        _user.Text = settings.LastUserName ?? "";
        _user.Leave += (_, _) => LoadSavedSqlLogin();
        _signIn.Click += (_, _) => TrySignIn();
        Shown += (_, _) =>
        {
            LoadSavedSqlLogin();
            if (_user.Text.Length > 0) _password.Focus(); else _user.Focus();

            // For automated testing only: ARQAMBASE_TEST_SIGNIN=user:password signs in with those (still fully checked).
            if (Environment.GetEnvironmentVariable("ARQAMBASE_TEST_SIGNIN") is { } test && test.Split(':', 2) is [var u, var p])
            {
                _user.Text = u;
                _password.Text = p;
                TrySignIn();
            }
        };
    }

    private static TableLayoutPanel Fields(params (string Label, Control Box)[] rows)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, BackColor = Color.Transparent };
        foreach (var (label, box) in rows)
            AddRow(t, label, box);
        return t;
    }

    private static void AddRow(TableLayoutPanel t, string label, Control box)
    {
        t.ColumnStyles.Clear();
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.Width = 290;
        t.Controls.Add(new KryptonLabel { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        t.Controls.Add(box);
    }

    private void LoadSavedSqlLogin()
    {
        if (!_config.UsesWindowsLogin) return;
        _saved = _user.Text.Trim().Length > 0 ? SqlLoginStore.Load(_user.Text.Trim()) : null;
        if (_saved is not null)
            _savedInfo.Text = $"Using your saved login {_saved.FullName(_config.ServerMachine)}.";
        ShowSqlFields(_saved is null);
    }

    private void ShowSqlFields(bool show)
    {
        _sqlFields.Visible = show;
        _sqlSaved.Visible = !show;
        if (_sqlGroup is not null)
            _sqlGroup.Height = show ? 150 : 64;
        FitToContent();
        if (show && _saved is not null && _sqlUser.Text.Length == 0)
            _sqlUser.Text = _saved.UserName;
    }

    private void TrySignIn()
    {
        _error.Text = "";
        SqlLogin? sql = null;
        var typedSql = _sqlFields.Visible;
        if (_config.UsesWindowsLogin)
            sql = typedSql ? new SqlLogin(_sqlUser.Text.Trim(), _sqlPassword.Text) : _saved;

        UseWaitCursor = true;
        _signIn.Enabled = false;
        SignInResult result;
        try
        {
            result = SignIn.Run(_config, _user.Text, _password.Text, sql, () =>
                KryptonMessageBox.Show(this,
                    $"There are no ArqamBase users yet.\n\nCreate \"{_user.Text.Trim()}\" as the first Admin, with the password you typed?",
                    "First sign-in", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) == DialogResult.Yes);
        }
        finally
        {
            UseWaitCursor = false;
            _signIn.Enabled = true;
        }

        if (result.Session is null)
        {
            _error.Text = result.Message;
            FitToContent();
            if (result.Problem is SignInProblem.SqlLoginRejected or SignInProblem.SqlLoginMissing)
            {
                ShowSqlFields(true);
                (_sqlUser.Text.Length == 0 ? _sqlUser : _sqlPassword).Focus();
            }
            else if (result.Problem == SignInProblem.WrongPassword)
            {
                _password.SelectAll();
                _password.Focus();
            }
            return;
        }

        var appUser = result.Session.UserName;
        if (sql is not null && typedSql)
        {
            if (_remember.Checked) SqlLoginStore.Save(appUser, sql);
            else SqlLoginStore.Forget(appUser);
        }
        _settings.LastUserName = appUser;
        try { _settings.Save(); } catch (IOException) { }

        Session = result.Session;
        DialogResult = DialogResult.OK;
        Close();
    }
}
