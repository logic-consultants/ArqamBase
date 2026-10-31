using System.ComponentModel;
using ArqamBase.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32.SafeHandles;

namespace ArqamBase.App.Core;

public enum SignInProblem { None, SqlLoginMissing, SqlLoginRejected, ServerUnreachable, WrongPassword, Locked, SwitchedOff, Cancelled }

public sealed record SignInResult(UserSession? Session, SignInProblem Problem, string Message)
{
    public static SignInResult Fail(SignInProblem p, string m) => new(null, p, m);
}

/// <summary>
/// Two-step sign-in: connect to SQL Server with the user's Windows account on the server,
/// then check the app user name and password in AppUsers.
/// </summary>
public static class SignIn
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    /// <param name="confirmFirstAdmin">Asked when no app users exist yet; returning true creates this user as Admin.</param>
    public static SignInResult Run(AppConfig config, string userName, string password, SqlLogin? sqlLogin, Func<bool> confirmFirstAdmin)
    {
        userName = userName.Trim();
        if (userName.Length == 0 || password.Length == 0)
            return SignInResult.Fail(SignInProblem.WrongPassword, "Enter your user name and password.");

        SafeAccessTokenHandle? token = null;
        var sqlName = "SQL Server login";
        if (config.UsesWindowsLogin)
        {
            if (sqlLogin is null || sqlLogin.UserName.Trim().Length == 0)
                return SignInResult.Fail(SignInProblem.SqlLoginMissing, $"Enter your Windows user name and password for {config.ServerMachine}.");
            sqlName = sqlLogin.FullName(config.ServerMachine);
            try { token = sqlLogin.CreateToken(config.ServerMachine); }
            catch (Win32Exception ex) { return SignInResult.Fail(SignInProblem.SqlLoginRejected, ex.Message); }
        }

        var ok = false;
        try
        {
            using var db = ArqamDbContext.Create(config.ConnectionString, userName, token);
            try
            {
                db.Database.OpenConnection();
            }
            catch (SqlException ex) when (ex.Number is 18456 or 18452)
            {
                return SignInResult.Fail(SignInProblem.SqlLoginRejected, $"SQL Server did not accept {sqlName}. Check the Windows user name and password, and that this account has a login on {config.Server}.");
            }
            catch (SqlException ex)
            {
                return SignInResult.Fail(SignInProblem.ServerUnreachable, $"Cannot reach {config.Database} on {config.Server}.\n{ex.Message}");
            }

            var createdAdmin = false;
            if (!db.AppUsers.Any())
            {
                if (PasswordHasher.Weakness(password) is { } weak)
                    return SignInResult.Fail(SignInProblem.WrongPassword, $"No users exist yet, so this sign-in creates the first Admin. Choose a stronger password: {weak}");
                if (!confirmFirstAdmin())
                    return SignInResult.Fail(SignInProblem.Cancelled, "");
                var admin = new AppUser { UserName = userName, FullName = userName, RoleName = nameof(AppRole.Admin) };
                admin.SetPassword(password, mustChange: false);
                db.AppUsers.Add(admin);
                db.SaveChanges();
                createdAdmin = true;
            }

            var user = db.AppUsers.FirstOrDefault(u => u.UserName == userName);
            if (user is null)
                return SignInResult.Fail(SignInProblem.WrongPassword, "The user name or password is wrong.");
            if (user.IsLocked)
                return SignInResult.Fail(SignInProblem.Locked, $"This account is locked after {MaxAttempts} wrong passwords. Try again after {user.LockedUntil:HH:mm}, or ask an Admin to reset it.");
            if (!user.CheckPassword(password))
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= MaxAttempts)
                    user.LockedUntil = DateTime.Now.Add(LockTime);
                db.SaveChanges();
                return SignInResult.Fail(SignInProblem.WrongPassword, user.HasPassword
                    ? "The user name or password is wrong."
                    : "No password is set for this user yet. Ask an Admin to set one.");
            }
            if (!user.IsActive)
                return SignInResult.Fail(SignInProblem.SwitchedOff, "This user is switched off. Ask an Admin.");

            user.FailedAttempts = 0;
            user.LockedUntil = null;
            user.LastSignIn = DateTime.Now;
            db.SaveChanges();

            ok = true;
            return new SignInResult(new UserSession
            {
                UserName = user.UserName,
                DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName,
                Role = user.Role,
                SqlLoginName = config.UsesWindowsLogin ? sqlName : new SqlConnectionStringBuilder(config.ConnectionString).UserID,
                WindowsToken = token,
                CreatedFirstAdmin = createdAdmin,
                MustChangePassword = user.MustChangePassword
            }, SignInProblem.None, "");
        }
        finally
        {
            if (!ok) token?.Dispose();
        }
    }

    /// <summary>Changes the signed-in user's own password.</summary>
    public static string? ChangePassword(AppConfig config, UserSession session, string? currentPassword, string newPassword)
    {
        if (PasswordHasher.Weakness(newPassword) is { } weak)
            return weak;
        using var db = config.CreateContext(session);
        var user = db.AppUsers.First(u => u.UserName == session.UserName);
        if (currentPassword is not null && !user.CheckPassword(currentPassword))
            return "Your current password is wrong.";
        if (user.CheckPassword(newPassword))
            return "Choose a password different from the current one.";
        user.SetPassword(newPassword, mustChange: false);
        db.SaveChanges();
        session.MustChangePassword = false;
        return null;
    }
}
