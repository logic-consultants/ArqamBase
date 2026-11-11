using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>
/// Keeps the school's Google contacts (the phone's contact list) in step with the students: one contact per student,
/// added when a student joins, updated when the class, father's mobile or name changes, and moved to the "Left students"
/// label when the student leaves. Check shows what would change; nothing goes to Google until Apply. Admin only.
/// Replaces the Access GoogleContacts form and modGoogleContacts, which only read the contacts in.
/// </summary>
public sealed class GoogleContactsScreen : ScreenBase
{
    private readonly KryptonTextBox _account = new(), _clientId = new(), _secret = new() { UseSystemPasswordChar = true };
    private readonly KryptonLabel _signedIn = new() { Text = "", AutoSize = true };
    private readonly KryptonLabel _banner = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), Text = "Press Check to compare the students with the Google contacts." };
    private readonly StatCard _onRoll = new("Students on roll"), _add = new("New contacts"), _update = new("To update"), _left = new("Move to Left"), _same = new("Up to date");
    private readonly AccessGrid _grid = new() { AllowUserToAddRows = false, AllowUserToDeleteRows = false };
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private FilterBindingList<GoogleSyncRow>? _list;
    private GoogleSyncPlan? _plan;
    private CancellationTokenSource? _busy;
    private bool _accountChanged;

    public GoogleContactsScreen(ScreenContext ctx) : base(ctx)
    {
        var fields = new FieldPanel(3);
        fields.Add("Google account", _account);
        fields.Add("OAuth client ID (Desktop app)", _clientId);
        fields.Add("Client secret", _secret);
        foreach (var box in new[] { _account, _clientId, _secret }) box.TextChanged += (_, _) => { _accountChanged = true; RaiseStatusChanged(); };
        var account = new Card("Google account");
        account.AddTop(fields, _signedIn);

        var stats = new EqualColumns { Padding = new Padding(0, Card.Gap, 0, 0) };
        stats.Controls.AddRange([_onRoll, _add, _update, _left, _same]);

        _grid.AddCheck(nameof(GoogleSyncRow.Apply), "Apply", 55);
        _grid.AddText(nameof(GoogleSyncRow.ActionText), "Action", 110, readOnly: true);
        _grid.AddText(nameof(GoogleSyncRow.Cc), "CC", 60, readOnly: true);
        _grid.AddText(nameof(GoogleSyncRow.Name), "Student", 170, readOnly: true);
        _grid.AddText(nameof(GoogleSyncRow.Father), "Father", 160, readOnly: true);
        _grid.AddText(nameof(GoogleSyncRow.Class), "Class", 100, readOnly: true);
        _grid.AddText(nameof(GoogleSyncRow.Changes), "What changes", 300, readOnly: true, fill: true);
        _grid.AddText(nameof(GoogleSyncRow.Result), "Result", 140, readOnly: true);

        var body = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, Card.Gap, 0, 0) };
        body.Controls.Add(_grid);
        body.Controls.Add(stats);
        body.Controls.Add(_banner);
        Controls.Add(body);
        Controls.Add(account);
        Controls.Add(Heading("Google Contacts"));

        _commands =
        [
            new("Google",
                new ScreenCommand("Check", Icons.Search, () => _ = CheckAsync()),
                new ScreenCommand("Apply ticked", Icons.Send, () => _ = ApplyAsync()),
                new ScreenCommand("Stop", Icons.Cross, () => _busy?.Cancel())),
            new("Tick",
                new ScreenCommand("Tick all", Icons.Check, () => Tick(true)),
                new ScreenCommand("Tick none", Icons.Clear, () => Tick(false))),
            new("Account",
                new ScreenCommand("Sign in to Google", Icons.Lock, () => _ = SignInAsync())),
        ];
        HandleCreated += (_, _) => LoadAccount();
    }

    public override bool SupportsRecords => true;
    public override bool HasChanges => _accountChanged;
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override string StatusText => _plan is null ? "Google Contacts" : $"{_plan.Rows.Count:N0} changes, {_plan.Rows.Count(r => r.Apply):N0} ticked{(HasChanges ? "  ·  unsaved account changes" : "")}";

    private void LoadAccount()
    {
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var row = GoogleAuth.Load(db);
            _account.Text = row.GoogleAccount ?? "";
            _clientId.Text = row.ClientId ?? "";
            _secret.Text = row.ClientSecret ?? "";
            _signedIn.Text = string.IsNullOrWhiteSpace(row.RefreshToken)
                ? "Not signed in yet: press Sign in to Google."
                : $"Signed in{(row.LastUpdated is { } on ? $" (token renewed {on:dd/MM/yyyy HH:mm})" : "")}. Contacts are only read until you press Apply.";
            _accountChanged = false;
            RaiseStatusChanged();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) { Program.ShowError("Could not read the Google account (table tblGoogleOAuth).", ex); }
    }

    /// <summary>Saves the account, client ID and secret. A changed client or account needs a new sign-in.</summary>
    public override bool SaveChanges()
    {
        if (!_accountChanged) return true;
        try
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var row = GoogleAuth.Load(db);
            var newClient = !string.Equals(row.ClientId?.Trim(), _clientId.Text.Trim(), StringComparison.Ordinal)
                            || !string.Equals(row.GoogleAccount?.Trim(), _account.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            row.GoogleAccount = _account.Text.Trim();
            row.ClientId = _clientId.Text.Trim();
            row.ClientSecret = _secret.Text.Trim();
            if (newClient) (row.RefreshToken, row.AccessToken, row.ExpiresAt) = (null, null, null);
            GoogleAuth.Save(db, row);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            Program.ShowError("Could not save the Google account.", ex);
            return false;
        }
        LoadAccount();
        return true;
    }

    public override void Reload() => _ = CheckAsync();

    private async Task SignInAsync()
    {
        if (!SaveChanges() || _busy is not null) return;
        _banner.Text = "Sign in to Google in the browser window that opened, then come back here…";
        using var busy = _busy = new CancellationTokenSource();
        try
        {
            await GoogleAuth.SignInAsync(Ctx.Config, Ctx.Session, busy.Token);
            _banner.Text = "Signed in to Google. Press Check.";
        }
        catch (Exception ex) when (ex is GoogleException or Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException or System.Net.HttpListenerException)
        {
            _banner.Text = "Not signed in: " + ex.Message;
        }
        finally { _busy = null; }
        LoadAccount();
    }

    private async Task<GooglePeople?> ConnectAsync(CancellationToken cancel)
    {
        var token = await GoogleAuth.AccessTokenAsync(Ctx.Config, Ctx.Session, cancel);
        if (token is not null) return new GooglePeople(token);
        _banner.Text = "Google needs you to sign in (again): press Sign in to Google.";
        return null;
    }

    /// <summary>Reads all the Google contacts and compares them with the students. Changes nothing.</summary>
    private async Task CheckAsync()
    {
        if (!SaveChanges() || _busy is not null) return;
        using var busy = _busy = new CancellationTokenSource();
        _banner.Text = "Reading the Google contacts…";
        try
        {
            if (await ConnectAsync(busy.Token) is not { } google) return;
            var contacts = await google.ContactsAsync(busy.Token);
            var groups = await google.GroupsAsync(busy.Token);
            _banner.Text = $"Comparing {contacts.Count:N0} contacts with the students…";
            var plan = await Task.Run(() =>
            {
                using var db = Ctx.Config.CreateContext(Ctx.Session);
                return GoogleSync.Plan(db, contacts, groups);
            }, busy.Token);
            if (IsDisposed) return;
            Show(plan, contacts.Count);
        }
        catch (Exception ex) when (ex is GoogleException or Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _banner.Text = "Could not check: " + ex.Message;
        }
        catch (OperationCanceledException) { _banner.Text = "Stopped."; }
        finally { _busy = null; }
    }

    private void Show(GoogleSyncPlan plan, int contacts)
    {
        _plan = plan;
        foreach (var r in plan.Rows) r.PropertyChanged += (_, _) => { _grid.Invalidate(); RaiseStatusChanged(); };
        _list = new FilterBindingList<GoogleSyncRow>(plan.Rows);
        _grid.Bind(_list);
        _onRoll.SetValue($"{plan.OnRoll:N0}");
        _add.SetValue($"{plan.Rows.Count(r => r.Action == GoogleAction.Add):N0}", Theme.Good);
        _update.SetValue($"{plan.Rows.Count(r => r.Action is GoogleAction.Update or GoogleAction.BackFromLeft):N0}", Theme.Warning);
        _left.SetValue($"{plan.Rows.Count(r => r.Action == GoogleAction.MoveToLeft):N0}");
        _same.SetValue($"{plan.UpToDate:N0}");
        var label = plan.StudentsLabel is { } l ? $"New contacts go in the label \"{l.Name}\"." : "New contacts go in My Contacts (no student label found).";
        _banner.Text = $"{contacts:N0} Google contacts read. {label}{(plan.Duplicates > 0 ? $" {plan.Duplicates:N0} contacts share a CC with another and are left alone." : "")}" +
                       (plan.Rows.Count(r => !r.Apply) is > 0 and var odd ? $" {odd:N0} contacts have a different student name, so may be someone else's: they are not ticked." : "") +
                       " Untick anything you do not want, then press Apply ticked.";
        RaiseStatusChanged();
    }

    private void Tick(bool on)
    {
        _grid.EndEdit();
        foreach (var r in _plan?.Rows ?? []) r.Apply = on;
    }

    /// <summary>Makes the ticked changes in Google, after asking. On a test database it says so first: the Google account is the real one.</summary>
    private async Task ApplyAsync()
    {
        _grid.EndEdit();
        if (_busy is not null) return;
        if (_plan is null) { Inform("Press Check first."); return; }
        var ticked = _plan.Rows.Count(r => r.Apply && r.Result.Length == 0);
        if (ticked == 0) { Inform("Nothing is ticked."); return; }
        var warning = MessageOut.MustTest(Ctx.Config)
            ? $"\n\nThis is the {Ctx.Config.Database} database, not the live one, but the Google account is the real one: its contacts will change to match this database."
            : "";
        if (KryptonMessageBox.Show(this, $"Make {ticked:N0} changes in the Google contacts of {_account.Text.Trim()}?{warning}\n\nContacts are added and changed, and moved between labels; none are deleted.",
                "Google Contacts", KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question, KryptonMessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var busy = _busy = new CancellationTokenSource();
        try
        {
            if (await ConnectAsync(busy.Token) is not { } google) return;
            var progress = new Progress<string>(text => _banner.Text = text);
            var (done, failed) = await GoogleSync.ApplyAsync(google, _plan, progress, busy.Token);
            _banner.Text = failed == 0 ? $"Done: {done:N0} changes made in Google. Press Check to confirm." : $"{done:N0} done and {failed:N0} failed (see Result). Press Check to try the rest again.";
        }
        catch (Exception ex) when (ex is GoogleException or Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            _banner.Text = "Stopped: " + ex.Message;
        }
        catch (OperationCanceledException) { _banner.Text = "Stopped. Press Check to see what is left."; }
        finally { _busy = null; }
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Google Contacts", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _busy?.Cancel();
        base.Dispose(disposing);
    }
}
