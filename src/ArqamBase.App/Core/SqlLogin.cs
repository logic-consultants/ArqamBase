using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ArqamBase.App.Core;

/// <summary>The Windows account on the database server that a user connects to SQL Server with.</summary>
public sealed record SqlLogin(string UserName, string Password)
{
    /// <summary>
    /// Gets a token that uses these credentials for network connections only (like "runas /netonly").
    /// The password is checked by SQL Server when the first connection opens.
    /// </summary>
    public SafeAccessTokenHandle CreateToken(string defaultDomain)
    {
        var parts = UserName.Split('\\', 2);
        var (domain, user) = parts.Length == 2 ? (parts[0], parts[1]) : (defaultDomain, parts[0]);
        if (!LogonUser(user, domain, Password, LogonNewCredentials, ProviderWinNT50, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return token;
    }

    /// <summary>"feeclerk1" becomes "DAS-SVR\feeclerk1".</summary>
    public string FullName(string defaultDomain) => UserName.Contains('\\') ? UserName : $"{defaultDomain}\\{UserName}";

    private const int LogonNewCredentials = 9;
    private const int ProviderWinNT50 = 3;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out SafeAccessTokenHandle token);
}

/// <summary>
/// Remembers a user's SQL Server login on this computer, encrypted by Windows (DPAPI) for the current Windows user.
/// Nothing is stored in the database.
/// </summary>
public static class SqlLoginStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArqamBase.SqlLogin.v1");

    private static string PathFor(string appUser)
    {
        var safe = string.Concat(appUser.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArqamBase", $"sqllogin-{safe}.bin");
    }

    public static SqlLogin? Load(string appUser)
    {
        var path = PathFor(appUser);
        if (!File.Exists(path)) return null;
        try
        {
            var json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SqlLogin>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public static void Save(string appUser, SqlLogin login)
    {
        var path = PathFor(appUser);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(login), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Forget(string appUser)
    {
        var path = PathFor(appUser);
        if (File.Exists(path)) File.Delete(path);
    }
}
