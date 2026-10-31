namespace ArqamBase.Data;

/// <summary>A student (table Students). Id is the CC (computer code).</summary>
public class Student : ISoftDelete
{
    public int Id { get; set; }
    public int? DsNo { get; set; }
    public int? FamilyId { get; set; }
    public Family? Family { get; set; }
    public string? Name { get; set; }
    public string? NameUrdu { get; set; }
    public string? Sex { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? FormB { get; set; }
    public int? BloodGroupId { get; set; }
    public DateTime? AdmissionDate { get; set; }
    public string? AdmissionClass { get; set; }
    public int? ClassId { get; set; }
    public int? SectionId { get; set; }
    /// <summary>"Currently present in school" (column Status).</summary>
    public bool OnRoll { get; set; } = true;
    /// <summary>Left, Transfer or empty (column Left).</summary>
    public string? LeavingType { get; set; }
    public DateTime? LeavingDate { get; set; }
    public string? LeavingReason { get; set; }
    public string? PromotionDate { get; set; }
    public double? PreviousClass { get; set; }
    public bool Flag { get; set; }
    public bool Snap { get; set; }
    public string? PicturePath { get; set; }
    public decimal? Fee { get; set; }
    public string? Email { get; set; }
    public string? WhatsApp { get; set; }
    public string? Remarks { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }

    public List<Conversation> Conversations { get; set; } = [];

    // Shown in grids; not stored.
    public string? FatherName => Family?.FatherName;
    public string StatusText => OnRoll ? "On roll" : string.IsNullOrWhiteSpace(LeavingType) ? "Not on roll" : LeavingType!;
}

/// <summary>A family (table Family). Id is the family ID (FormID).</summary>
public class Family : ISoftDelete
{
    public int Id { get; set; }
    public string? FatherName { get; set; }
    public string? FatherNameUrdu { get; set; }
    public string? Cnic { get; set; }
    public string? Caste { get; set; }
    public string? FatherEducation { get; set; }
    public string? Profession { get; set; }
    public string? BusinessName { get; set; }
    public string? Mobile { get; set; }
    public string? PhoneHome { get; set; }
    public string? PhoneOffice { get; set; }
    public string? MotherName { get; set; }
    public string? MotherMobile { get; set; }
    public string? MotherEducation { get; set; }
    public string? CurrentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? EmergencyName { get; set; }
    public string? EmergencyNo { get; set; }
    public string? Nationality { get; set; } = "Pakistani";
    public bool Flag { get; set; }
    public string? Notes { get; set; }
    public string? SpecialInstructions { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }

    public List<Student> Students { get; set; } = [];

    // Shown in grids; not stored.
    public int ChildrenOnRoll => Students.Count(s => s.OnRoll);
}

/// <summary>A note of a conversation with a student's family (table tblConvo).</summary>
public class Conversation : ISoftDelete
{
    public int Id { get; set; }
    public int? StudentId { get; set; }
    public DateTime? Date { get; set; } = DateTime.Today;
    public DateTime? Time { get; set; }
    public int? TypeId { get; set; }
    public string? Transcript { get; set; }
    public bool? IsIssue { get; set; }
    public bool? IsResolved { get; set; }
    public string? Resolution { get; set; }
    public string? EnteredBy { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? CreatedOn { get; set; }
    public DateTime? DeletedOn { get; set; }
    public byte[]? RowVersion { get; set; }
}

public class BloodGroup
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

/// <summary>A picture or document of a student (table tblStudentDocuments). The file itself is in the shared attachments folder.</summary>
public class StudentDocument : ISoftDelete
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    /// <summary>ProfilePicture, FormB, … or Doc1, Doc2… (see <see cref="DocumentTypes"/>).</summary>
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
    public string TypeText => DocumentTypes.Label(DocType);
    public string FileName => System.IO.Path.GetFileName(FilePath);
}

/// <summary>The kinds of student documents. Anything else is saved as Doc1, Doc2…</summary>
public static class DocumentTypes
{
    public const string ProfilePicture = "ProfilePicture";
    public const string Other = "Doc";
    public const int MaxPerStudent = 10;

    public static readonly IReadOnlyList<(string Type, string Label)> All =
    [
        (ProfilePicture, "Profile picture"),
        ("FormB", "Form-B"),
        ("BirthCertificate", "Birth certificate"),
        ("FatherCNIC", "Father's CNIC"),
        ("MotherCNIC", "Mother's CNIC"),
        ("LeavingCertificate", "Leaving certificate"),
        ("ResultCard", "Result card"),
        (Other, "Other document"),
    ];

    public static string Label(string type) =>
        All.FirstOrDefault(t => t.Type == type).Label
        ?? (type.StartsWith(Other) ? $"Other document {type[Other.Length..]}" : type);
}
