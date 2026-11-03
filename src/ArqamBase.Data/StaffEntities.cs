namespace ArqamBase.Data;

/// <summary>A member of staff (table Staff). Only what attendance needs; the Staff screen comes in phase 6.</summary>
public class StaffMember : ISoftDelete
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? FatherName { get; set; }
    public int? TypeId { get; set; }
    public string? Designation { get; set; }
    public string? CellNo { get; set; }
    public DateTime? DateHired { get; set; }
    /// <summary>The date they left (column SLDate); empty while they work here.</summary>
    public DateTime? LeftOn { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
}

/// <summary>A post, e.g. Teacher (Academic) or Clerk (Non Academic) (table StaffTypes).</summary>
public class StaffType : ISoftDelete
{
    public int Id { get; set; }
    /// <summary>Academic or Non Academic (column Type).</summary>
    public string? Kind { get; set; }
    /// <summary>The post (column Detail).</summary>
    public string? Name { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
}

/// <summary>One staff member's attendance on one date (table tblStaffAttendance).</summary>
public class StaffAttendance
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int StaffId { get; set; }
    public string Status { get; set; } = "P";
    public TimeSpan? TimeIn { get; set; }
    public TimeSpan? TimeOut { get; set; }
    public string? Remarks { get; set; }
    public string TakenBy { get; set; } = "";
    public DateTime TakenOn { get; set; }
}

/// <summary>
/// What a user looks after (table tblUserScope): a department for a coordinator, or a class and section for a teacher in charge.
/// </summary>
public class UserScope
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public int? ClassId { get; set; }
    public int? SectionId { get; set; }
}
