using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>Shown for screens that belong to a later phase.</summary>
public sealed class ComingSoonScreen : ScreenBase
{
    public ComingSoonScreen(ScreenContext ctx, string title, int phase, string replaces) : base(ctx)
    {
        var text = new KryptonLabel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Values = { Text = $"This screen comes in phase {phase}.", ExtraText = string.IsNullOrEmpty(replaces) ? "" : $"\nUntil then, use Access: {replaces}" }
        };
        Controls.Add(text);
        Controls.Add(Heading(title));
    }
}
