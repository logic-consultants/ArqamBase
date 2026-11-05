namespace ArqamBase.Data;

/// <summary>A member of staff (table Staff).</summary>
public class StaffMember : ISoftDelete
{
    public int Id { get; set; }
    /// <summary>Mr., Miss or Mrs. (column Salute).</summary>
    public string? Salute { get; set; }
    public string? Name { get; set; }
    public string? NameUrdu { get; set; }
    /// <summary>Father or spouse.</summary>
    public string? FatherName { get; set; }
    public int? TypeId { get; set; }
    public string? Designation { get; set; }
    public string? Address { get; set; }
    public string? HomePhone { get; set; }
    public string? CellNo { get; set; }
    public DateTime? BirthDate { get; set; }
    public DateTime? DateHired { get; set; }
    /// <summary>Last degree (column Education).</summary>
    public string? Education { get; set; }
    public int? PrevExperience { get; set; }
    public string? Subjects { get; set; }
    public string? MaritalStatus { get; set; }
    /// <summary>The salary at which they were hired (column Salary).</summary>
    public decimal? Salary { get; set; }
    public int? CurrentSalary { get; set; }
    public decimal? LastIncrement { get; set; }
    /// <summary>Monthly security deduction.</summary>
    public decimal? SecDeduction { get; set; }
    /// <summary>Monthly SCF deduction (the G.P column of the salary sheet).</summary>
    public decimal? Scf { get; set; }
    public int? BranchId { get; set; }
    /// <summary>1 Teaching (Girls), 2 Teaching (Boys), 3 Non Teaching, 4 Directors.</summary>
    public byte? SalarySheet { get; set; }
    /// <summary>BAHL or CASH.</summary>
    public string? PayMethod { get; set; }
    public string? BankAccount { get; set; }
    public string? Caste { get; set; }
    public string? Religion { get; set; }
    public string? Cnic { get; set; }
    public string? EmergencyNo { get; set; }
    public decimal? StPerk { get; set; }
    public decimal? FuelAllowance { get; set; }
    /// <summary>The date they left (column SLDate); empty while they work here.</summary>
    public DateTime? LeftOn { get; set; }
    public string? LeftReason { get; set; }
    /// <summary>"Working currently".</summary>
    public bool? Flag { get; set; }
    public int? TotalTestMarks { get; set; }
    public int? ObtainedTestMarks { get; set; }
    public string? InterviewRemarks { get; set; }
    public string? Reference { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }

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

/// <summary>A month's salary sheet (table tblSalariesHead). Period is yyyyMM.</summary>
public class SalaryHead
{
    public int Id { get; set; }
    public string Period { get; set; } = "";
    public DateTime? SalaryDate { get; set; }
    public bool IsFinal { get; set; }
    public string? BankChequeNo { get; set; }
    public string? CashChequeNo { get; set; }
    public string? PreparedBy { get; set; }
    public DateTime? PreparedOn { get; set; }
    public List<SalaryLine> Lines { get; set; } = [];
}

/// <summary>One person on a month's sheet (table tblSalaries). Amounts are kept as they were, whatever the Staff record says later.</summary>
public class SalaryLine
{
    public int Id { get; set; }
    public int HeadId { get; set; }
    public int StaffId { get; set; }
    public DateTime? SalaryDate { get; set; }
    public byte? Sheet { get; set; }
    public decimal? BasicSalary { get; set; }
    public int? Days { get; set; }
    public int? AbsentLeave { get; set; }
    public decimal? Incentive { get; set; }
    /// <summary>D.D: the deduction for days absent.</summary>
    public decimal? Deduction { get; set; }
    public decimal? Scf { get; set; }
    public decimal? Security { get; set; }
    public decimal? AmountPaid { get; set; }
    /// <summary>Advance owed before this month's deduction.</summary>
    public decimal? Advance { get; set; }
    public decimal? AdvanceDeducted { get; set; }
    public decimal? AdvanceBalance { get; set; }
    /// <summary>BAHL or CASH (column Bank).</summary>
    public string? PayMethod { get; set; }
    public string? BankAccount { get; set; }
    public string? Caste { get; set; }
    public string? Religion { get; set; }
    public string? Cnic { get; set; }
    public string? EmergencyNo { get; set; }
    public decimal? StPerk { get; set; }
    public decimal? FuelAllowance { get; set; }
    public string? RemainingSecurity { get; set; }
    public string? Verify { get; set; }
}

/// <summary>An advance to a member of staff, from the Advance Request Form (table tblStaffAdvance).</summary>
public class StaffAdvance : ISoftDelete
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int StaffId { get; set; }
    public decimal Amount { get; set; }
    public int Installments { get; set; } = 1;
    /// <summary>yyyyMM of the first salary it is deducted from.</summary>
    public string FirstPeriod { get; set; } = "";
    public string? Witness1 { get; set; }
    public string? Witness2 { get; set; }
    public string? Witness3 { get; set; }
    public string? PaidThrough { get; set; }
    public string? RecommendedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Remarks { get; set; }
    public string CreatedBy { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }

    /// <summary>The equal monthly payment, in whole rupees; the last one takes what is left.</summary>
    public decimal Installment => Installments <= 0 ? Amount : Math.Ceiling(Amount / Installments);
}

/// <summary>A picture or document of a staff member (table tblStaffDocuments). The file itself is in the shared staff attachments folder.</summary>
public class StaffDocument : ISoftDelete
{
    public int Id { get; set; }
    public int StaffId { get; set; }
    /// <summary>ProfilePicture, CNIC, … or Doc1, Doc2… (see <see cref="StaffDocumentTypes"/>).</summary>
    public string DocType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public int? FileSizeKB { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public string? DeletedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }

    // Shown in grids; not stored.
    public string TypeText => StaffDocumentTypes.Label(DocType);
    public string FileName => System.IO.Path.GetFileName(FilePath);
}

/// <summary>The kinds of staff documents. Anything else is saved as Doc1, Doc2…</summary>
public static class StaffDocumentTypes
{
    public const string ProfilePicture = DocumentTypes.ProfilePicture;
    public const string Other = DocumentTypes.Other;
    /// <summary>Documents per person, not counting the profile picture.</summary>
    public const int MaxDocuments = 10;

    public static readonly IReadOnlyList<(string Type, string Label)> All =
    [
        (ProfilePicture, "Profile picture"),
        ("CNIC", "CNIC"),
        ("CV", "CV"),
        ("Degree", "Degree"),
        ("ExperienceLetter", "Experience letter"),
        ("AppointmentLetter", "Appointment letter"),
        (Other, "Other document"),
    ];

    public static string Label(string type) =>
        All.FirstOrDefault(t => t.Type == type).Label
        ?? (type.StartsWith(Other) ? $"Other document {type[Other.Length..]}" : type);
}

/// <summary>The wording of a Job Offer Letter (table tblJobOfferTemplates): for one post, or the general letter when StaffTypeId is empty.</summary>
public class JobOfferTemplate
{
    public int Id { get; set; }
    public int? StaffTypeId { get; set; }
    public string Heading { get; set; } = "";
    public string CampusLine { get; set; } = "";
    public string Quote { get; set; } = "";
    public string Title { get; set; } = "";
    public string Intro { get; set; } = "";
    /// <summary>Each line prints as its own paragraph.</summary>
    public string Terms { get; set; } = "";
    public string Signatory { get; set; } = "";
    public string Acceptance { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
}
