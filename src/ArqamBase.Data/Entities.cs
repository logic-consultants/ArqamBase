namespace ArqamBase.Data;

/// <summary>Rows that are never physically deleted: isDeleted is set instead.</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? CreatedOn { get; set; }
    DateTime? DeletedOn { get; set; }
}

/// <summary>A school (table Branches). Address holds the school's name.</summary>
public class School : ISoftDelete
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
}

public class Department : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? SchoolId { get; set; }
    public string? AccountNumber { get; set; }
    public string? CampusDivision { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class SchoolClass : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? DepartmentId { get; set; }
    /// <summary>Sort key such as "04 One" (column ClassFltr).</summary>
    public string? SortName { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class Section : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public int? ClassId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class ConversationType : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class AttendanceStatus
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool CountsAsPresent { get; set; }
    public byte SortOrder { get; set; }
}

/// <summary>The attendance of one class and section on one date (table tblAttendanceRegister).</summary>
public class AttendanceRegister
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
    public string TakenBy { get; set; } = "";
    public DateTime TakenOn { get; set; }
    /// <summary>Locked registers can be changed only by an Admin.</summary>
    public bool IsLocked { get; set; }
    public string? Remarks { get; set; }
    public List<StudentAttendance> Marks { get; set; } = [];
}

/// <summary>One student's mark in a register: P, A, L or T (table tblStudentAttendance).</summary>
public class StudentAttendance
{
    public int Id { get; set; }
    public int RegisterId { get; set; }
    public int StudentId { get; set; }
    public string Status { get; set; } = "P";
    public string? Remarks { get; set; }
}

public class CalendarDay
{
    public DateTime Date { get; set; } = DateTime.Today;
    public bool IsHoliday { get; set; } = true;
    public string? Reason { get; set; }

    public string DayName => Date.ToString("dddd");
}

public enum AppRole { Admin, FeeClerk, Admissions, ReadOnly, Coordinator, TeacherInCharge }

public class AppUser
{
    public string UserName { get; set; } = "";
    public string? FullName { get; set; }
    public string RoleName { get; set; } = nameof(AppRole.ReadOnly);
    public byte[]? PasswordHash { get; set; }
    public byte[]? PasswordSalt { get; set; }
    public int? PasswordIterations { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastSignIn { get; set; }

    public AppRole Role => Enum.TryParse<AppRole>(RoleName, out var r) ? r : AppRole.ReadOnly;
    public bool HasPassword => PasswordHash is not null;
    public bool IsLocked => LockedUntil > DateTime.Now;
    public string PasswordState => !HasPassword ? "Not set" : MustChangePassword ? "Temporary" : "Set";

    public void SetPassword(string password, bool mustChange)
    {
        (PasswordHash, PasswordSalt, PasswordIterations) = PasswordHasher.Hash(password);
        MustChangePassword = mustChange;
        FailedAttempts = 0;
        LockedUntil = null;
    }

    public bool CheckPassword(string password) =>
        PasswordHash is not null && PasswordSalt is not null && PasswordIterations is int n
        && PasswordHasher.Verify(password, PasswordHash, PasswordSalt, n);
}
