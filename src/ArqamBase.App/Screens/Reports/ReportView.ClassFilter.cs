using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

public abstract partial class ReportView
{
    /// <summary>Schools, departments, classes and sections, read once with the report's data.</summary>
    protected sealed record ClassLookup(
        List<(int? Id, string Name)> Schools,
        Dictionary<int, (int? SchoolId, string Name)> Departments,
        List<(int Id, int? DepartmentId, string Name, string Sort)> Classes,
        List<(int Id, int? ClassId, string Name)> Sections)
    {
        /// <summary>The classes and sections a coordinator or teacher in charge may see; null for everyone else.</summary>
        public HashSet<(int ClassId, int SectionId)>? Allowed { get; init; }

        /// <summary>Only the user's departments or class and section, for a coordinator or teacher in charge.</summary>
        public ClassLookup For(Core.UserSession session)
        {
            if (!session.IsScoped) return this;
            var classes = Classes.ToDictionary(c => c.Id);
            var sections = Sections.Where(s => s.ClassId is int ci && classes.TryGetValue(ci, out var c) && session.Covers(c.DepartmentId, ci, s.Id)).ToList();
            var classIds = sections.Select(s => s.ClassId).ToHashSet();
            var schools = Classes.Where(c => classIds.Contains(c.Id)).Select(c => c.DepartmentId is int d ? Departments.GetValueOrDefault(d).SchoolId : null).ToHashSet();
            return new ClassLookup(Schools.Where(s => schools.Contains(s.Id)).ToList(), Departments, Classes.Where(c => classIds.Contains(c.Id)).ToList(), sections)
            {
                Allowed = sections.Select(s => (s.ClassId!.Value, s.Id)).ToHashSet(),
            };
        }

        public static ClassLookup Read(ArqamDbContext db) => new(
            db.Schools.AsNoTracking().OrderBy(s => s.Id).AsEnumerable().Select(s => ((int?)s.Id, s.Name ?? "")).ToList(),
            db.Departments.AsNoTracking().AsEnumerable().ToDictionary(d => d.Id, d => (d.SchoolId, d.Name ?? "")),
            db.Classes.AsNoTracking().OrderBy(c => c.SortName).ThenBy(c => c.Name).AsEnumerable()
                .Select(c => (c.Id, c.DepartmentId, c.Name ?? "", c.SortName ?? c.Name ?? "")).ToList(),
            Core.ClassSections.Read(db));
    }

    /// <summary>The cascading School, Class and Section filters of a report.</summary>
    protected sealed class ClassFilter
    {
        public ComboBox School { get; } = Combo(170);
        public ComboBox Class { get; } = Combo(130);
        public ComboBox Section { get; } = Combo(130);
        public event EventHandler? Changed;
        private ClassLookup _lookup = new([], [], [], []);
        private Dictionary<int, (int Id, int? DepartmentId, string Name, string Sort)> _classes = [];
        private Dictionary<int, string> _sections = [];
        private bool _filling;

        public ClassFilter()
        {
            School.SelectedIndexChanged += (_, _) => { if (!_filling) { FillClasses(); Changed?.Invoke(this, EventArgs.Empty); } };
            Class.SelectedIndexChanged += (_, _) => { if (!_filling) { FillSections(); Changed?.Invoke(this, EventArgs.Empty); } };
            Section.SelectedIndexChanged += (_, _) => { if (!_filling) Changed?.Invoke(this, EventArgs.Empty); };
        }

        public Control[] Controls => [School, Class, Section];
        public int? SchoolId => Selected<int?>(School);

        /// <summary>Shows the lookup in the boxes, keeping what was chosen.</summary>
        public void Set(ClassLookup lookup)
        {
            _lookup = lookup;
            _classes = lookup.Classes.ToDictionary(c => c.Id);
            _sections = lookup.Sections.DistinctBy(s => s.Id).ToDictionary(s => s.Id, s => s.Name);
            _filling = true;
            Fill(School, "All schools", lookup.Schools);
            _filling = false;
            FillClasses();
        }

        public bool Matches(int? classId, int? sectionId)
        {
            if (_lookup.Allowed is { } allowed && !(classId is int c0 && sectionId is int s0 && allowed.Contains((c0, s0)))) return false;
            if (Selected<int?>(Class) is int cls && classId != cls) return false;
            if (Selected<int?>(Section) is int section && sectionId != section) return false;
            return SchoolId is not int school || SchoolOf(classId) == school;
        }

        public int? SchoolOf(int? classId) =>
            classId is int id && _classes.TryGetValue(id, out var c) && c.DepartmentId is int d ? _lookup.Departments.GetValueOrDefault(d).SchoolId : null;

        public string SchoolName(int? classId) =>
            SchoolOf(classId) is int school ? _lookup.Schools.FirstOrDefault(s => s.Id == school).Name ?? "" : "";

        public string ClassName(int? classId) => classId is int id && _classes.TryGetValue(id, out var c) ? c.Name : "";
        public string ClassSort(int? classId) => classId is int id && _classes.TryGetValue(id, out var c) ? c.Sort : "";
        public string SectionName(int? sectionId) => sectionId is int id ? _sections.GetValueOrDefault(id, "") : "";

        public string DepartmentName(int? classId) =>
            classId is int id && _classes.TryGetValue(id, out var c) && c.DepartmentId is int d && _lookup.Departments.TryGetValue(d, out var dn) ? dn.Name : "";

        /// <summary>The chosen school, class and section for the printout's filter line.</summary>
        public IEnumerable<string> TextParts()
        {
            foreach (var box in Controls.Cast<ComboBox>())
                if (Selected<int?>(box) is not null) yield return SelectedText(box);
        }

        private void FillClasses()
        {
            _filling = true;
            var school = SchoolId;
            Fill(Class, "All classes", _lookup.Classes
                .Where(c => school is null || c.DepartmentId is int d && _lookup.Departments.GetValueOrDefault(d).SchoolId == school)
                .Select(c => ((int?)c.Id, $"Class: {c.Name}")));
            _filling = false;
            FillSections();
        }

        private void FillSections()
        {
            _filling = true;
            var cls = Selected<int?>(Class);
            Fill(Section, "All sections", _lookup.Sections.Where(s => cls is null || s.ClassId == cls).DistinctBy(s => s.Id).Select(s => ((int?)s.Id, $"Section: {s.Name}")));
            _filling = false;
        }
    }
}
