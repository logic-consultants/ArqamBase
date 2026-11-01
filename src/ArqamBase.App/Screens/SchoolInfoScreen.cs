using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Shell;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// The school's own details (table CampusInfo): name, address, phones, the logo, and the letterhead header and footer
/// pictures printed on certificates. Only an Admin can change them.
/// </summary>
public sealed class SchoolInfoScreen : ScreenBase
{
    private const int MaxPictureKB = 3072;

    private readonly BindingSource _bs = new();
    private readonly bool _canEdit;
    private readonly PictureSlot _logo, _header, _footer;
    private readonly ScreenCommandGroup[] _commands;
    private ArqamDbContext? _db;
    private Campus? _campus;

    public SchoolInfoScreen(ScreenContext ctx) : base(ctx)
    {
        _canEdit = ctx.Session.CanEdit(AppArea.Setup);

        var fields = new FieldPanel(4);
        fields.Add("Campus name", Inputs.Text(_bs, nameof(Campus.Name)), 2);
        fields.Add("Address", Inputs.Text(_bs, nameof(Campus.Address)), 2);
        fields.Add("Phone 1", Inputs.Text(_bs, nameof(Campus.Phone1)));
        fields.Add("Phone 2", Inputs.Text(_bs, nameof(Campus.Phone2)));
        fields.Add("Phone 3", Inputs.Text(_bs, nameof(Campus.Phone3)));
        fields.Enabled = _canEdit;
        var details = new Card("Details");
        details.AddTop(fields);

        _logo = new PictureSlot("Logo", "Printed on ID cards, and on certificates when there is no letterhead header picture.",
            () => _campus?.Logo, v => SetPicture(c => c.Logo = v), _canEdit);
        _header = new PictureSlot("Letterhead header", "A PNG picture stretched across the top of certificates, in place of the logo, school name and address.",
            () => _campus?.LetterheadHeader, v => SetPicture(c => c.LetterheadHeader = v), _canEdit);
        _footer = new PictureSlot("Letterhead footer", "A PNG picture stretched across the bottom of certificates.",
            () => _campus?.LetterheadFooter, v => SetPicture(c => c.LetterheadFooter = v), _canEdit);

        var hint = new KryptonLabel
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8),
            Text = _canEdit
                ? "Used on printouts. The school name on certificates comes from each student's class (Setup lists › Schools). Save with the ribbon's Save button."
                : "Only an Admin can change these details."
        };

        var content = new StackPanel();
        content.AddTop(details, _logo, _header, _footer);
        var scroll = new KryptonPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 8, 0) };
        scroll.Controls.Add(content);

        Controls.Add(scroll);
        Controls.Add(hint);
        Controls.Add(Heading("School information"));

        _commands = [new("Print", new ScreenCommand("Sample certificate", Icons.Print, PreviewSample))];
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override bool HasChanges => _db?.ChangeTracker.HasChanges() == true;
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override string StatusText => _campus is null ? "" : $"School information{(HasChanges ? "  ·  unsaved changes" : "")}";

    public override void Reload()
    {
        _db?.Dispose();
        _db = Ctx.Config.CreateContext(Ctx.Session);
        try
        {
            _campus = _db.Campuses.OrderBy(c => c.Id).FirstOrDefault();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)
        {
            Program.ShowError("Could not read the school information. Has sql\\campus_info_install.sql been run on this database?", ex);
            _campus = null;
        }
        if (_campus is null && _canEdit)
        {
            _campus = new Campus();
            _db.Campuses.Add(_campus); // no row yet: Save creates it
        }
        _bs.DataSource = _campus is null ? typeof(Campus) : _campus;
        _bs.ResetBindings(false);
        foreach (var slot in new[] { _logo, _header, _footer }) slot.ShowPicture();
        RaiseStatusChanged();
    }

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        if (_db is null || !_canEdit) return true;
        try
        {
            _db.SaveChanges();
            RaiseStatusChanged();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            KryptonMessageBox.Show(this, "Someone else changed the school information while you were editing. Your changes were not saved; it will now reload.",
                "School information", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);
            Reload();
            return false;
        }
        catch (DbUpdateException ex)
        {
            Program.ShowError("Could not save the school information.", ex);
            return false;
        }
    }

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate(); // pushes the value of the box being typed in
        _bs.EndEdit();
    }

    private void SetPicture(Action<Campus> set)
    {
        if (_campus is null) return;
        set(_campus);
        _db?.ChangeTracker.DetectChanges();
        RaiseStatusChanged();
    }

    /// <summary>A bonafide certificate for a made-up student, with the details as they are now (saved or not).</summary>
    private void PreviewSample()
    {
        CommitPendingEdits();
        var sample = new Printing.StudentPrint(1234, 567, "SAMPLE STUDENT", true, "SAMPLE FATHER", new DateTime(2015, 3, 15), new DateTime(2020, 4, 1),
            "Five", "A", Printing.PrintKit.DefaultSchoolName, null, null, null, "", 1000, null, null);
        var school = Printing.SchoolPrint.From(_campus);
        PrintPreviewForm.Show(this, () => Printing.Certificates.Bonafide(sample, school), "Sample certificate", "Sample certificate");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A box with one picture: a preview, and Choose picture… / Remove buttons.</summary>
    private sealed class PictureSlot : Card
    {
        private readonly Func<byte[]?> _get;
        private readonly Action<byte[]?> _set;
        private readonly PictureBox _preview = new() { Dock = DockStyle.Top, Height = 120, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        private readonly KryptonLabel _info = new() { AutoSize = true };
        private readonly KryptonButton _choose = new() { Text = "Choose picture…" }, _remove = new() { Text = "Remove" };

        public PictureSlot(string title, string hint, Func<byte[]?> get, Action<byte[]?> set, bool canEdit) : base(title)
        {
            _get = get;
            _set = set;
            var hintLabel = new KryptonLabel { AutoSize = true, Text = hint, Padding = new Padding(0, 0, 0, 6) };
            var bar = new FlowLayoutPanel { Height = 40, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            bar.Controls.AddRange([_choose, _remove, _info]);
            _choose.Click += (_, _) => Choose();
            _remove.Click += (_, _) => { _set(null); ShowPicture(); };
            _choose.Visible = _remove.Visible = canEdit;
            AddTop(hintLabel, _preview, bar);
            SizeButtons();
            Theme.Changed += OnThemeChanged;
        }

        /// <summary>Shows the current picture.</summary>
        public void ShowPicture()
        {
            var old = _preview.Image;
            var bytes = _get();
            _preview.Image = null;
            old?.Dispose();
            if (bytes is { Length: > 0 })
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    using var image = Image.FromStream(stream);
                    _preview.Image = new Bitmap(image);
                    _info.Text = $"{image.Width} × {image.Height} pixels · {bytes.Length / 1024:N0} KB";
                }
                catch (ArgumentException) { _info.Text = "The saved picture cannot be read."; }
            }
            else _info.Text = "No picture.";
            _remove.Enabled = bytes is { Length: > 0 };
        }

        private void Choose()
        {
            using var dialog = new OpenFileDialog { Filter = "Pictures (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "Choose a picture" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(dialog.FileName);
                using var stream = new MemoryStream(bytes);
                using var _ = Image.FromStream(stream); // checks it is a picture
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Program.ShowError("Could not use this file as a picture.", ex);
                return;
            }
            if (bytes.Length > MaxPictureKB * 1024)
            {
                KryptonMessageBox.Show(this, $"This picture is {bytes.Length / 1024 / 1024.0:N1} MB. Please use one under {MaxPictureKB / 1024} MB.",
                    "School information", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
                return;
            }
            _set(bytes);
            ShowPicture();
        }

        // Sized from the text so they fit at every font size (Krypton's AutoSize buttons came out too wide).
        private void SizeButtons()
        {
            foreach (var b in new[] { _choose, _remove })
                b.Size = new Size(TextRenderer.MeasureText(b.Text, Theme.BaseFont).Width + 24, Theme.RowHeight + 2);
            _info.Margin = new Padding(8, 4, 0, 0);
            ((Control)_choose.Parent!).Height = Theme.RowHeight + 14;
        }

        private void OnThemeChanged(object? sender, EventArgs e) => SizeButtons();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Theme.Changed -= OnThemeChanged;
                _preview.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
