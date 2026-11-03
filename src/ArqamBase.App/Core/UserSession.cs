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

    /// <summary>The departments (coordinator) or classes and sections (teacher in charge) this user looks after.</summary>
    public IReadOnlyList<UserScope> Scopes { get; init; } = [];

    public static string RoleLabel(AppRole role) => role switch
    {
        AppRole.FeeClerk => "Fee clerk",
        AppRole.ReadOnly => "Read-only",
        AppRole.TeacherInCharge => "Teacher in charge",
        _ => role.ToString()
    };

    /// <summary>Area-level edit rights. Coordinators and teachers in charge mark attendance only where <see cref="Covers"/> allows.</summary>
    public bool CanEdit(AppArea area) => Role switch
    {
        AppRole.Admin => true,
        AppRole.FeeClerk => area == AppArea.Fees,
        AppRole.Admissions => area is AppArea.Students or AppArea.Attendance,
        AppRole.Coordinator or AppRole.TeacherInCharge => area == AppArea.Attendance,
        _ => false
    };

    /// <summary>
    /// Which areas the user sees. Everyone else may view everything; a coordinator sees students, attendance and their reports,
    /// a teacher in charge only attendance.
    /// </summary>
    public bool CanView(AppArea area) => Role switch
    {
        AppRole.Coordinator => area is AppArea.Students or AppArea.Attendance or AppArea.Reports,
        AppRole.TeacherInCharge => area is AppArea.Attendance or AppArea.Reports,
        _ => true
    };

    /// <summary>Whether attendance is limited to the user's departments or classes.</summary>
    public bool IsScoped => Role is AppRole.Coordinator or AppRole.TeacherInCharge;

    /// <summary>Whether the user may see and mark this class and section (in this department); everyone does unless scoped.</summary>
    public bool Covers(int? departmentId, int classId, int? sectionId) =>
        !IsScoped || Scopes.Any(s => (s.DepartmentId is int d && d == departmentId)
                                     || (s.ClassId == classId && (sectionId is null || s.SectionId == sectionId)));

    /// <summary>
    /// How far back the user may mark or change a day's attendance: a teacher in charge only today; others any day
    /// (an Admin can lock a day).
    /// </summary>
    public bool CanChangeDay(DateTime date) => Role != AppRole.TeacherInCharge || date.Date == DateTime.Today;

    public void Dispose() => WindowsToken?.Dispose();
}

public enum AppArea { Setup, Students, Fees, Attendance, Staff, Accounts, Messages, Reports }
