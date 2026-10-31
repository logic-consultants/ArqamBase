using ArqamBase.Data;
using Microsoft.Win32.SafeHandles;

namespace ArqamBase.App.Core;

/// <summary>The signed-in app user, their role, and the Windows account used for SQL Server.</summary>
public sealed class UserSession : IDisposable
{
    public required string UserName { get; init; }
    public required string DisplayName { get; init; }
    public required AppRole Role { get; init; }
    /// <summary>The SQL Server login, e.g. DAS-SVR\feeclerk1.</summary>
    public required string SqlLoginName { get; init; }
    public SafeAccessTokenHandle? WindowsToken { get; init; }
    public bool CreatedFirstAdmin { get; init; }
    public bool MustChangePassword { get; set; }

    public bool IsAdmin => Role == AppRole.Admin;

    public static string RoleLabel(AppRole role) => role switch
    {
        AppRole.FeeClerk => "Fee clerk",
        AppRole.ReadOnly => "Read-only",
        _ => role.ToString()
    };

    /// <summary>Area-level edit rights; read access is open to every signed-in user.</summary>
    public bool CanEdit(AppArea area) => Role switch
    {
        AppRole.Admin => true,
        AppRole.FeeClerk => area == AppArea.Fees,
        AppRole.Admissions => area == AppArea.Students,
        _ => false
    };

    public void Dispose() => WindowsToken?.Dispose();
}

public enum AppArea { Setup, Students, Fees, Attendance, Staff, Accounts, Messages, Reports }
