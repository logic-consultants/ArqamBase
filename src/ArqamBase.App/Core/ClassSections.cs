using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>
/// The sections of each class. In the database sections are shared by all classes (their ClassID is 0), so a class's
/// sections are the ones its students are in, plus any section saved with that class.
/// </summary>
public static class ClassSections
{
    /// <summary>
    /// One entry per class and section, sorted by section name; a section used by several classes appears once for each.
    /// With <paramref name="onRollOnly"/>, only sections that have students on roll in that class.
    /// </summary>
    public static List<(int Id, int? ClassId, string Name)> Read(ArqamDbContext db, bool onRollOnly = false)
    {
        var sections = db.Sections.AsNoTracking().OrderBy(s => s.Name).ToList();
        var used = db.Students.AsNoTracking().Where(s => s.ClassId != null && s.SectionId != null && (!onRollOnly || s.OnRoll))
            .Select(s => new { ClassId = s.ClassId!.Value, SectionId = s.SectionId!.Value }).Distinct().AsEnumerable()
            .Select(u => (u.ClassId, u.SectionId));
        var pairs = used.Concat(sections.Where(s => s.ClassId > 0).Select(s => (ClassId: s.ClassId!.Value, SectionId: s.Id))).ToHashSet();
        return sections.SelectMany(s => pairs.Where(p => p.SectionId == s.Id).OrderBy(p => p.ClassId).Select(p => (s.Id, (int?)p.ClassId, s.Name ?? "")))
            .ToList();
    }
}
