using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>Start page: today's attendance, the screens ready to use, and what comes next.</summary>
public sealed class DashboardScreen : ScreenBase
{
    public DashboardScreen(ScreenContext ctx) : base(ctx)
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        var year = DateTime.Today.Month >= 7 ? DateTime.Today.Year : DateTime.Today.Year - 1;
        var screens = ScreenCatalog.For(ctx.Session).ToList();

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

        flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.TitlePanel, Text = $"{greeting}, {ctx.Session.DisplayName}" });
        flow.Controls.Add(new KryptonLabel { AutoSize = true, Text = $"{DateTime.Today:dddd, d MMMM yyyy}  ·  Academic year {year}-{(year + 1) % 100:00}  ·  Role: {UserSession.RoleLabel(ctx.Session.Role)}" });
        flow.Controls.Add(Spacer(16));

        if (ctx.Session.CanView(AppArea.Attendance))
        {
            flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.BoldControl, Text = "Attendance today" });
            flow.Controls.Add(AttendanceToday());
            flow.Controls.Add(Spacer(12));
        }

        flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.BoldControl, Text = "Ready now" });
        flow.Controls.Add(Tiles(screens.Where(s => s.Create is not null && s.Key != "dashboard")));
        flow.Controls.Add(Spacer(12));

        var next = screens.Where(s => s.Create is null).OrderBy(s => s.Phase).GroupBy(s => s.Phase).ToList();
        if (next.Count > 0)
            flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.BoldControl, Text = "Coming next (keep using Access for these)" });
        foreach (var phase in next)
            flow.Controls.Add(new KryptonLabel { AutoSize = true, Text = $"Phase {phase.Key}:  " + string.Join(", ", phase.Select(s => s.Title.Replace("&", "&&"))) });

        Controls.Add(flow);
    }

    /// <summary>
    /// Classes taken today (of those the user looks after), students present and absent, and staff present.
    /// Shown as "-" when the attendance tables cannot be read.
    /// </summary>
    private Control AttendanceToday()
    {
        var classes = new StatCard("Classes taken");
        var present = new StatCard("Students present");
        var absent = new StatCard("Absent or on leave");
        var staff = new StatCard("Staff present");
        var row = new EqualColumns { Dock = DockStyle.None, Width = 900, Margin = new Padding(0, 6, 0, 0) };
        row.Controls.AddRange([classes, present, absent]);
        var showStaff = Ctx.Session.CanView(AppArea.Staff);
        if (showStaff) row.Controls.Add(staff);
        try
        {
            var today = DateTime.Today;
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            var department = db.Classes.AsNoTracking().Select(c => new { c.Id, c.DepartmentId }).ToDictionary(c => c.Id, c => c.DepartmentId);
            var sections = db.Students.AsNoTracking().Where(s => s.OnRoll && s.ClassId != null && s.SectionId != null)
                .Select(s => new { ClassId = s.ClassId!.Value, SectionId = s.SectionId!.Value }).Distinct().ToList()
                .Where(s => Ctx.Session.Covers(department.GetValueOrDefault(s.ClassId), s.ClassId, s.SectionId)).ToList();
            var marks = (from m in db.StudentAttendance.AsNoTracking()
                         join r in db.AttendanceRegisters.AsNoTracking() on m.RegisterId equals r.Id
                         where r.Date == today
                         select new { r.ClassId, r.SectionId, m.Status }).ToList()
                .Where(m => Ctx.Session.Covers(department.GetValueOrDefault(m.ClassId), m.ClassId, m.SectionId)).ToList();
            var taken = marks.Select(m => (m.ClassId, m.SectionId)).Distinct().Count();
            classes.SetValue($"{taken} of {sections.Count}", taken < sections.Count ? Theme.Warning : Theme.Good);
            var here = marks.Count(m => m.Status is "P" or "T");
            present.SetValue(marks.Count == 0 ? "-" : $"{here:N0}  ({100.0 * here / marks.Count:N0}%)", Theme.Good);
            var away = marks.Count(m => m.Status is "A" or "L");
            absent.SetValue(marks.Count == 0 ? "-" : $"{away:N0}", away > 0 ? Theme.Error : null);
            if (showStaff)
            {
                var staffMarks = db.StaffAttendance.AsNoTracking().Where(a => a.Date == today).Select(a => a.Status).ToList();
                var onStaff = db.Staff.AsNoTracking().Count(s => s.LeftOn == null);
                staff.SetValue(staffMarks.Count == 0 ? "Not taken" : $"{staffMarks.Count(s => s is "P" or "T")} of {onStaff}");
            }
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            foreach (var card in new[] { classes, present, absent, staff }) card.SetValue("-");
        }
        return row;
    }

    private FlowLayoutPanel Tiles(IEnumerable<ScreenInfo> screens)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Color.Transparent, MaximumSize = new Size(1100, 0) };
        foreach (var s in screens)
        {
            var b = new KryptonButton
            {
                Width = 200, Height = 64, Margin = new Padding(0, 6, 12, 6),
                Values = { Text = s.Title, Image = Icons.Large(s.Glyph) },
            };
            b.Click += (_, _) => Ctx.OpenScreen(s.Key);
            panel.Controls.Add(b);
        }
        return panel;
    }

    private static Control Spacer(int h) => new Panel { Height = h, Width = 10, BackColor = Color.Transparent };
}
