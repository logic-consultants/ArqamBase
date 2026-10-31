using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>Start page. Live figures (fees, attendance) are added as their modules are built.</summary>
public sealed class DashboardScreen : ScreenBase
{
    public DashboardScreen(ScreenContext ctx) : base(ctx)
    {
        var hour = DateTime.Now.Hour;
        var greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        var year = DateTime.Today.Month >= 7 ? DateTime.Today.Year : DateTime.Today.Year - 1;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

        flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.TitlePanel, Text = $"{greeting}, {ctx.Session.DisplayName}" });
        flow.Controls.Add(new KryptonLabel { AutoSize = true, Text = $"{DateTime.Today:dddd, d MMMM yyyy}  ·  Academic year {year}-{(year + 1) % 100:00}  ·  Role: {UserSession.RoleLabel(ctx.Session.Role)}" });
        flow.Controls.Add(Spacer(16));

        flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.BoldControl, Text = "Ready now" });
        flow.Controls.Add(Tiles(ScreenCatalog.All.Where(s => s.Create is not null && s.Key != "dashboard")));
        flow.Controls.Add(Spacer(12));

        flow.Controls.Add(new KryptonLabel { AutoSize = true, LabelStyle = LabelStyle.BoldControl, Text = "Coming next (keep using Access for these)" });
        var next = ScreenCatalog.All.Where(s => s.Create is null).OrderBy(s => s.Phase).GroupBy(s => s.Phase);
        foreach (var phase in next)
            flow.Controls.Add(new KryptonLabel { AutoSize = true, Text = $"Phase {phase.Key}:  " + string.Join(", ", phase.Select(s => s.Title.Replace("&", "&&"))) });

        Controls.Add(flow);
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
