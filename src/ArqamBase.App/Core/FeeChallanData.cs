using System.Globalization;
using ArqamBase.App.Printing;
using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>Fee periods and what fee challan forms print, shared by the fee screens.</summary>
public static class FeeChallanData
{
    /// <summary>A month as challans store it, e.g. "NOV/26".</summary>
    public static string Period(DateTime month) => month.ToString("MMM/yy", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// A period's letters and digits only, for comparing: stored periods are written in several ways (NOV/26, NOV-26, APR26).
    /// </summary>
    public static string PeriodKey(string? period) => period is null ? "" : new string(period.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>The fee challan forms of these students: each one's unpaid challans added together. Students with nothing unpaid are left out.</summary>
    public static List<ChallanPrint> Load(ArqamDbContext db, IReadOnlyCollection<int> studentIds)
    {
        var ids = studentIds.Select(i => (int?)i).ToList();
        var unpaid = db.FeeChallans.AsNoTracking().Where(c => !c.Paid && ids.Contains(c.StudentId))
            .OrderBy(c => c.IssueDate).ThenBy(c => c.Id).ToList()
            .GroupBy(c => c.StudentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        if (unpaid.Count == 0) return [];
        var keys = unpaid.Keys.ToList();
        var students = (from s in db.Students.AsNoTracking().Include(s => s.Family)
                        where keys.Contains(s.Id)
                        join c in db.Classes on s.ClassId equals c.Id into cs
                        from c in cs.DefaultIfEmpty()
                        join sec in db.Sections on s.SectionId equals sec.Id into ss
                        from sec in ss.DefaultIfEmpty()
                        join d in db.Departments on c.DepartmentId equals d.Id into ds
                        from d in ds.DefaultIfEmpty()
                        join sc in db.Schools on d.SchoolId equals sc.Id into scs
                        from sc in scs.DefaultIfEmpty()
                        select new { Student = s, ClassName = c.Name, SectionName = sec.Name, d.AccountNumber, d.CampusDivision, School = sc.Name }).ToList();
        var order = studentIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return students
            .OrderBy(x => order.GetValueOrDefault(x.Student.Id))
            .Select(x => new ChallanPrint(x.Student.Id, x.Student.Name ?? "", x.Student.FamilyId, x.Student.Family?.FatherName ?? "",
                x.ClassName ?? "", x.SectionName ?? "", x.School is { Length: > 0 } school ? school.Trim() : PrintKit.DefaultSchoolName, x.AccountNumber, x.CampusDivision,
                unpaid[x.Student.Id].Select(c => new ChallanLine(c.Period ?? "", c.TuitionFee ?? 0, c.ExamsFee ?? 0, c.RegistrationFee ?? 0,
                    c.AdmissionFee ?? 0, c.ProspectusFee ?? 0, c.Fine ?? 0, c.Other ?? 0, c.DueDate)).ToList(),
                unpaid[x.Student.Id].LastOrDefault(c => !string.IsNullOrWhiteSpace(c.Remarks))?.Remarks?.Trim()))
            .ToList();
    }
}
