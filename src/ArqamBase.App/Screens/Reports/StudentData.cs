using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>A student with the family's details and current tuition fee, as the student reports need them.</summary>
public sealed record StudentInfo(
    int Cc, int? DsNo, int? FamilyId, string Name, string Father, DateTime? BirthDate, DateTime? AdmissionDate,
    int? ClassId, int? SectionId, bool OnRoll, string Status, DateTime? LeavingDate, string? LeavingReason, string? Remarks,
    int? Tuition, string? Mobile, string? MotherMobile, string? PhoneHome, string? PhoneOffice, string? Address, string? Caste, string? Profession)
{
    /// <summary>All students (or those on roll) with their latest fee definition's tuition fee.</summary>
    public static List<StudentInfo> Read(ArqamDbContext db, bool onRollOnly)
    {
        var students = db.Students.AsNoTracking().Where(s => !onRollOnly || s.OnRoll).Include(s => s.Family).ToList();
        var tuition = db.FeeDefinitions.AsNoTracking().Where(d => d.StudentId != null)
            .Select(d => new { d.Id, StudentId = d.StudentId!.Value, d.TuitionFee }).ToList()
            .GroupBy(d => d.StudentId).ToDictionary(g => g.Key, g => g.MaxBy(d => d.Id)!.TuitionFee);
        return students.Select(s => new StudentInfo(
                s.Id, s.DsNo, s.FamilyId, s.Name ?? "", s.Family?.FatherName ?? "", s.BirthDate, s.AdmissionDate,
                s.ClassId, s.SectionId, s.OnRoll, s.StatusText, s.LeavingDate, s.LeavingReason, s.Remarks,
                tuition.TryGetValue(s.Id, out var fee) ? fee : null,
                s.Family?.Mobile, s.Family?.MotherMobile, s.Family?.PhoneHome, s.Family?.PhoneOffice, s.Family?.CurrentAddress,
                s.Family?.Caste, s.Family?.Profession))
            .ToList();
    }
}

/// <summary>One grid row of the student reports; each report shows the columns it needs.</summary>
public sealed class StudentRow
{
    public int? Cc { get; init; }
    public int? DsNo { get; init; }
    public int? FamilyId { get; init; }
    /// <summary>"123 · Father name", the heading of a family group.</summary>
    public string Family { get; init; } = "";
    public string Name { get; init; } = "";
    public string FatherName { get; init; } = "";
    public string School { get; init; } = "";
    public string Department { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string SectionName { get; init; } = "";
    public DateTime? BirthDate { get; init; }
    public DateTime? AdmissionDate { get; init; }
    public DateTime? LeavingDate { get; init; }
    public string Status { get; init; } = "";
    public string Month { get; init; } = "";
    public string Reason { get; init; } = "";
    public int? Fee { get; init; }
    public int Students { get; init; } = 1;
    public int? AverageFee { get; init; }
    public string? Mobile { get; init; }
    public string? MotherMobile { get; init; }
    public string? PhoneHome { get; init; }
    public string? PhoneOffice { get; init; }
    public string? Address { get; init; }
    public string? Caste { get; init; }
    public string? Profession { get; init; }
    public string? Remarks { get; init; }
    internal string ClassSort { get; init; } = "";
    internal DateTime? SortDate { get; init; }
}

public abstract partial class ReportView
{
    /// <summary>A row for one student, with the class and section names from the filter's lookup.</summary>
    protected static StudentRow StudentRowOf(StudentInfo s, ClassFilter classes) => new()
    {
        Cc = s.Cc, DsNo = s.DsNo, FamilyId = s.FamilyId, Family = $"{s.FamilyId} · {s.Father}", Name = s.Name, FatherName = s.Father,
        School = classes.SchoolName(s.ClassId), Department = classes.DepartmentName(s.ClassId),
        ClassName = classes.ClassName(s.ClassId), ClassSort = classes.ClassSort(s.ClassId), SectionName = classes.SectionName(s.SectionId),
        BirthDate = s.BirthDate, AdmissionDate = s.AdmissionDate, LeavingDate = s.LeavingDate, Status = s.Status,
        Reason = s.LeavingReason?.Trim() ?? "", Fee = s.Tuition, Mobile = s.Mobile, MotherMobile = s.MotherMobile,
        PhoneHome = s.PhoneHome, PhoneOffice = s.PhoneOffice, Address = s.Address?.Trim(), Caste = s.Caste, Profession = s.Profession,
        Remarks = s.Remarks?.Trim(),
    };

    /// <summary>Phone columns, shown as 0300-1234567.</summary>
    protected void AddPhones(params (string Property, string Header)[] columns)
    {
        foreach (var (p, h) in columns)
        {
            Grid.AddText(p, h, 115);
            Grid.FormatNumbers(p, Core.NumberFormat.Phone);
        }
    }

    /// <summary>An empty column to write in on the printout (marks, notes, signature).</summary>
    protected void AddBlank(string header, int width)
    {
        var col = new DataGridViewTextBoxColumn { Name = $"blank{Grid.Columns.Count}", HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
        Grid.Columns.Add(col);
    }
}
