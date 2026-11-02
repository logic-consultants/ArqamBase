using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Shell;

/// <summary>
/// Adds a missing or new challan for one student (replaces frmNewFee). The amounts start from the student's fee definition.
/// </summary>
public sealed class NewFeeForm : KryptonForm
{
    private readonly AppConfig _config;
    private readonly UserSession _session;
    private readonly Student _student;
    private readonly HashSet<string> _periods;
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly KryptonDateTimePicker _issue = Picker();
    private readonly KryptonDateTimePicker _due = Picker();
    private readonly KryptonNumericUpDown _tuition = Amount(), _exams = Amount(), _registration = Amount(), _admission = Amount(),
        _prospectus = Amount(), _fine = Amount(), _other = Amount();
    private readonly KryptonTextBox _remarks = new();
    private readonly KryptonLabel _total = new() { AutoSize = true, StateCommon = { ShortText = { Font = Theme.Scaled(1.1f, FontStyle.Bold) } } };
    private readonly KryptonLabel _warning = new() { AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Warning } } };

    /// <summary>The challan added (null until saved).</summary>
    public FeeChallan? Added { get; private set; }

    private static KryptonDateTimePicker Picker() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy" };
    private static KryptonNumericUpDown Amount() => new() { Maximum = 1_000_000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };

    public NewFeeForm(AppConfig config, UserSession session, Student student, FeeDefinition? definition, IEnumerable<string> existingPeriods, string className)
    {
        _config = config;
        _session = session;
        _student = student;
        _periods = existingPeriods.Select(FeeChallanData.PeriodKey).ToHashSet();
        Text = $"Add fee · {student.Id} {student.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => Core.Theme.FitButtons(this); // buttons as wide as their text at any font size
        MinimizeBox = MaximizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 480);

        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var m = month.AddMonths(-6); m <= month.AddMonths(6); m = m.AddMonths(1)) _period.Items.Add(FeeChallanData.Period(m));
        _period.Text = FeeChallanData.Period(month);
        _issue.Value = month;
        _due.Value = month.AddDays(9);
        _period.TextChanged += (_, _) => PeriodChanged();
        _tuition.Value = definition?.TuitionFee ?? 0;
        _exams.Value = definition?.ExamsFee ?? 0;
        _registration.Value = definition?.RegistrationFee ?? 0;
        _prospectus.Value = definition?.ProspectusFee ?? 0;
        _other.Value = definition?.Other ?? 0;
        foreach (var n in new[] { _tuition, _exams, _registration, _admission, _prospectus, _fine, _other }) n.ValueChanged += (_, _) => ShowTotal();

        var fields = new FieldPanel(3) { Padding = new Padding(16, 12, 16, 0) };
        fields.Add("", new KryptonLabel
        {
            AutoSize = true,
            Text = $"{student.Name} · CC {student.Id}{(className.Length > 0 ? " · " + className : "")}" +
                   (definition is null ? "\nNo fee definition: type the amounts." : "\nAmounts are from the fee definition; change them if needed."),
        }, 3);
        fields.Add("Period (e.g. NOV/26)", _period);
        fields.Add("Issue date", _issue);
        fields.Add("Due date", _due);
        fields.Add("Tuition", _tuition);
        fields.Add("Exams / Annual Fund", _exams);
        fields.Add("Registration", _registration);
        fields.Add("Admission", _admission);
        fields.Add("Prospectus", _prospectus);
        fields.Add("Other / summer tasks", _other);
        fields.Add("Fine", _fine);
        fields.Add("Total", _total, 2);
        fields.Add("Remarks", _remarks, 3);
        fields.Add("", _warning, 3);

        var ok = new KryptonButton { Text = "Add fee", Width = 110, Height = Theme.RowHeight + 6 };
        var cancel = new KryptonButton { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 110, Height = Theme.RowHeight + 6 };
        ok.Click += (_, _) => Save();
        Theme.MainButton(ok);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.RowHeight + 24, Padding = new Padding(12, 8, 12, 8), BackColor = Color.Transparent };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;
        var host = new KryptonPanel { Dock = DockStyle.Fill };
        host.Controls.Add(fields);
        host.Controls.Add(buttons);
        Controls.Add(host);
        ShowTotal();
        PeriodChanged();
    }

    private int Sum => (int)(_tuition.Value + _exams.Value + _registration.Value + _admission.Value + _prospectus.Value + _fine.Value + _other.Value);

    private void ShowTotal() => _total.Text = $"Rs {Sum:N0}";

    /// <summary>A period typed as MMM/YY sets the issue date to the 1st and the due date to the 10th of that month.</summary>
    private void PeriodChanged()
    {
        var text = _period.Text.Trim().ToUpperInvariant();
        if (DateTime.TryParseExact(text, "MMM/yy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var month))
        {
            _issue.Value = month;
            _due.Value = month.AddDays(9);
        }
        _warning.Text = _periods.Contains(FeeChallanData.PeriodKey(text)) ? $"{student()} already has a challan for {text}. Add it only if a second one is really needed." : "";
        string student() => _student.Name ?? $"CC {_student.Id}";
    }

    private void Save()
    {
        var period = _period.Text.Trim().ToUpperInvariant();
        if (period.Length == 0) { Say("Type the period, e.g. NOV/26."); _period.Focus(); return; }
        if (Sum == 0) { Say("Type at least one amount."); _tuition.Focus(); return; }
        if (_due.Value.Date < _issue.Value.Date) { Say("The due date is before the issue date."); _due.Focus(); return; }
        if (_periods.Contains(FeeChallanData.PeriodKey(period)) && KryptonMessageBox.Show(this, $"There is already a challan for {period}. Add another one?", Text,
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Question) != DialogResult.Yes) return;
        var challan = new FeeChallan
        {
            StudentId = _student.Id, Period = period, IssueDate = _issue.Value.Date, DueDate = _due.Value.Date,
            TuitionFee = (int)_tuition.Value, ExamsFee = (int)_exams.Value, RegistrationFee = (int)_registration.Value,
            AdmissionFee = (int)_admission.Value, ProspectusFee = (int)_prospectus.Value, Fine = (int)_fine.Value, Other = (int)_other.Value,
            ReceiptNo = 0, ReceivedAmount = 0, DrCr = "DR", Remarks = _remarks.Text.Trim() is { Length: > 0 } r ? r : null,
            EditedBy = _session.UserName, DateEdited = DateTime.Now,
        };
        try
        {
            using var db = _config.CreateContext(_session);
            db.FeeChallans.Add(challan);
            db.SaveChanges();
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("Could not add the fee.", ex);
            return;
        }
        Added = challan;
        DialogResult = DialogResult.OK;
    }

    private void Say(string message) => KryptonMessageBox.Show(this, message, Text, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
}
