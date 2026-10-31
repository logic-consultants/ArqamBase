using System.ComponentModel;
using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>What a screen gets from the main window.</summary>
public sealed record ScreenContext(AppConfig Config, UserSession Session, UserSettings Settings, Action<string> OpenScreen, Action ApplyTheme,
    Action<string, object> OpenScreenAt);

/// <summary>
/// Base for every screen shown as a tab. The ribbon's Records and Sort &amp; filter buttons call these methods on the active screen.
/// </summary>
public class ScreenBase : KryptonPanel
{
    protected ScreenContext Ctx { get; }

    public event EventHandler? StatusChanged;

    public ScreenBase(ScreenContext ctx)
    {
        Ctx = ctx;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual string StatusText => "";
    public virtual bool SupportsRecords => false;
    public virtual bool HasChanges => false;

    public virtual void NewRecord() { }
    public virtual bool SaveChanges() => true;
    public virtual void DeleteRecord() { }
    public virtual void Reload() { }
    public virtual void SortAscending() { }
    public virtual void SortDescending() { }
    public virtual void ClearFilters() { }
    public virtual void FocusSearch() { }

    /// <summary>Shows a given record, e.g. a student's CC or a family ID, when another screen links here.</summary>
    public virtual void Navigate(object target) { }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancels.</summary>
    public bool ConfirmLeave()
    {
        CommitPendingEdits();
        if (!HasChanges)
            return true;
        var answer = KryptonMessageBox.Show("Save your changes?", Text, KryptonMessageBoxButtons.YesNoCancel, KryptonMessageBoxIcon.Question);
        return answer switch
        {
            DialogResult.Yes => SaveChanges(),
            DialogResult.No => true,
            _ => false
        };
    }

    protected void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs an action once the control has its real size (for splitter positions).</summary>
    protected static void WhenSized(Control control, Action action)
    {
        void Handler(object? s, EventArgs e)
        {
            if (control.Width < 200 || control.Height < 200) return;
            control.SizeChanged -= Handler;
            action();
        }
        control.SizeChanged += Handler;
    }

    /// <summary>Pushes values still being typed into their records, so HasChanges sees them.</summary>
    protected virtual void CommitPendingEdits() { }

    protected static KryptonLabel Heading(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, LabelStyle = LabelStyle.TitlePanel, Padding = new Padding(0, 0, 0, 6), AutoSize = true
    };
}
