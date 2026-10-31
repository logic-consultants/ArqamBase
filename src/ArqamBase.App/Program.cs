using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using Krypton.Toolkit;

namespace ArqamBase.App;

static class Program
{
    /// <param name="args">Optional: --open &lt;screen key&gt; opens that screen at start (for desktop shortcuts).</param>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError("Something went wrong", e.Exception);

        var config = AppConfig.Load();
        var settings = UserSettings.Load();

        // The theme manager must exist before the sign-in window so it is themed too.
        KryptonManager.UseAdministratorSuffix = false;
        using var themeManager = new KryptonManager { GlobalApplyToolstrips = true };
        Theme.Apply(themeManager, settings.Theme);

        using var login = new LoginForm(config, settings);
        if (login.ShowDialog() != DialogResult.OK || login.Session is null)
            return;
        using var session = login.Session;

        if (session.MustChangePassword)
        {
            using var change = new ChangePasswordForm(config, session, required: true);
            if (change.ShowDialog() != DialogResult.OK)
                return;
        }

        var openIndex = Array.FindIndex(args, a => a.Equals("--open", StringComparison.OrdinalIgnoreCase));
        var startScreen = openIndex >= 0 && openIndex + 1 < args.Length ? args[openIndex + 1] : null;
        Application.Run(new MainForm(config, session, settings, startScreen));
    }

    public static void ShowError(string message, Exception ex) =>
        KryptonMessageBox.Show($"{message}\n\n{ex.GetBaseException().Message}", "ArqamBase",
            KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Error);
}
