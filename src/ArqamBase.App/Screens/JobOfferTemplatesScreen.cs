using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using ArqamBase.App.Printing;
using ArqamBase.Data;
using Krypton.Toolkit;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens;

/// <summary>
/// The wording of the Job Offer Letter for each post, changed by an Admin. A post with no wording of its own uses the
/// general letter, and until that is changed, the school's sample. Letters are printed from the Staff screen.
/// </summary>
public sealed class JobOfferTemplatesScreen : ScreenBase
{
    public sealed class Row
    {
        public int? PostId { get; init; }
        public required string Post { get; init; }
        /// <summary>The saved row, if this post has its own wording.</summary>
        public JobOfferTemplate? Saved { get; set; }
        /// <summary>What is on show: the saved row, or a copy of the wording in use, not saved until it is changed.</summary>
        public required JobOfferTemplate Text { get; set; }
        public string Starting { get; set; } = "";
        public string Uses => Saved is not null ? "Own letter" : PostId is null ? "School's sample" : "General letter";
        public bool Changed => JobOffersText.Key(Text) != Starting;
    }

    private readonly AccessGrid _posts = new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, RowHeadersVisible = false, ShowFilters = false,
    };
    private readonly BindingSource _bs = new() { DataSource = typeof(JobOfferTemplate) };
    private readonly KryptonPanel _details = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 8, 0) };
    private readonly Card _card = new("Wording");
    private readonly IReadOnlyList<ScreenCommandGroup> _commands;
    private FilterBindingList<Row>? _list;
    private ArqamDbContext? _db;
    private Row? _current;
    private bool _loading;

    public JobOfferTemplatesScreen(ScreenContext ctx) : base(ctx)
    {
        _posts.AddText(nameof(Row.Post), "Post", 170, fill: true);
        _posts.AddText(nameof(Row.Uses), "Uses", 110);
        _posts.SelectionChanged += (_, _) => { if (!_loading) ShowCurrent(); };

        var fields = new FieldPanel(2);
        fields.Add("School heading", Inputs.Text(_bs, nameof(JobOfferTemplate.Heading)));
        fields.Add("Campus", Inputs.Text(_bs, nameof(JobOfferTemplate.CampusLine)));
        fields.Add("Quotation", Inputs.Text(_bs, nameof(JobOfferTemplate.Quote)), 2);
        fields.Add("Letter title", Inputs.Text(_bs, nameof(JobOfferTemplate.Title)));
        fields.Add("Signed by", Inputs.Text(_bs, nameof(JobOfferTemplate.Signatory)));
        fields.Add("Opening line", Inputs.Text(_bs, nameof(JobOfferTemplate.Intro)), 2);
        var terms = Inputs.Text(_bs, nameof(JobOfferTemplate.Terms), multiline: true);
        terms.Height = 430;
        terms.ScrollBars = ScrollBars.Vertical;
        terms.AcceptsReturn = true;
        fields.Add("Terms and conditions (each line prints as its own paragraph)", terms, 2);
        var acceptance = Inputs.Text(_bs, nameof(JobOfferTemplate.Acceptance), multiline: true);
        acceptance.Height = 70;
        fields.Add("Acceptance", acceptance, 2);
        _card.AddTop(fields, new KryptonLabel { Text = $"These fields fill in by themselves: {JobOffers.FieldList}", AutoSize = true, StateCommon = { ShortText = { Color1 = Theme.Muted } } });
        var content = new StackPanel();
        content.AddTop(_card);
        _details.Controls.Add(content);

        var split = new KryptonSplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_posts);
        split.Panel2.Controls.Add(_details);
        WhenSized(split, () => split.SplitterDistance = Math.Clamp(split.Width / 4, 280, 380));
        Controls.Add(split);

        _commands = [new("Letter", new ScreenCommand("Preview", Icons.Print, Preview), new ScreenCommand("Staff", Icons.IdCard, () => Ctx.OpenScreen("staff")))];
        HandleCreated += (_, _) => Reload();
    }

    public override bool SupportsRecords => true;
    public override string NewText => "New letter";
    public override IReadOnlyList<ScreenCommandGroup> Commands => _commands;
    public override bool HasChanges => !_loading && _list?.Any(r => r.Changed) == true;
    public override string StatusText => _list is null ? "Loading…" : $"Job offer letters: {_list.Count(r => r.Saved is not null)} with their own wording{(HasChanges ? "  ·  unsaved changes" : "")}";

    protected override void CommitPendingEdits()
    {
        FindForm()?.Validate();
        _bs.EndEdit();
    }

    public override void Reload()
    {
        var keep = _current;
        _current = null;
        _db?.Dispose();
        var db = _db = Ctx.Config.CreateContext(Ctx.Session);
        _loading = true;
        try
        {
            var posts = db.StaffTypes.AsNoTracking().OrderBy(t => t.Name).ToList();
            var saved = db.JobOfferTemplates.ToList();
            var general = saved.FirstOrDefault(t => t.StaffTypeId is null);
            var rows = new List<Row> { MakeRow(null, "General (any post)", general, JobOffers.Sample()) };
            rows.AddRange(posts.Select(p => MakeRow(p.Id, p.Name?.Trim() ?? "", saved.FirstOrDefault(t => t.StaffTypeId == p.Id), general ?? JobOffers.Sample())));
            _list = new FilterBindingList<Row>(rows);
            _posts.Bind(_list);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            Program.ShowError("Could not load the letters. Has sql/job_offer_install.sql been run on this database?", ex);
        }
        finally
        {
            _loading = false;
        }
        if (keep is not null)
            foreach (DataGridViewRow row in _posts.Rows)
                if (row.DataBoundItem is Row r && r.PostId == keep.PostId) _posts.CurrentCell = row.Cells[0];
        ShowCurrent();
    }

    private static Row MakeRow(int? postId, string post, JobOfferTemplate? saved, JobOfferTemplate inUse)
    {
        var text = saved ?? JobOffersText.Copy(inUse, postId);
        return new Row { PostId = postId, Post = post, Saved = saved, Text = text, Starting = JobOffersText.Key(text) };
    }

    private void ShowCurrent()
    {
        if (_current is not null) CommitPendingEdits();
        _current = _posts.CurrentRow?.DataBoundItem as Row;
        _bs.DataSource = _current?.Text ?? (object)typeof(JobOfferTemplate);
        _details.Visible = _current is not null;
        RaiseStatusChanged();
    }

    public override void NewRecord() =>
        Inform("Each post already has a letter. Choose the post on the left and change its wording.");

    public override bool SaveChanges()
    {
        CommitPendingEdits();
        if (_db is null || _list is null) return true;
        var changed = _list.Where(r => r.Changed).ToList();
        if (changed.Count == 0) return true;
        if (changed.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Text.Terms)) is { } empty) { Inform($"The letter for {empty.Post} has no terms."); return false; }
        foreach (var row in changed)
        {
            row.Text.UpdatedBy = Ctx.Session.UserName;
            row.Text.UpdatedOn = DateTime.Now;
            if (row.Saved is null) _db.JobOfferTemplates.Add(row.Text);
        }
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not save.", ex); return false; }
        Reload();
        return true;
    }

    /// <summary>Removes a post's own wording, so it goes back to the general letter.</summary>
    public override void DeleteRecord()
    {
        if (_current is null || _db is null) return;
        if (_current.Saved is null) { Inform($"{_current.Post} has no wording of its own."); return; }
        var back = _current.PostId is null ? "the school's sample" : "the general letter";
        if (KryptonMessageBox.Show(this, $"Remove the wording for {_current.Post}? It goes back to {back}.", "Job offer letters",
                KryptonMessageBoxButtons.YesNo, KryptonMessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _db.JobOfferTemplates.Remove(_current.Saved);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException ex) { Program.ShowError("Could not remove the wording.", ex); }
        Reload();
    }

    /// <summary>The letter with this wording and nothing filled in.</summary>
    private void Preview()
    {
        CommitPendingEdits();
        if (_current is null) return;
        var (text, school) = (_current.Text, SchoolDetails());
        Shell.PrintPreviewForm.Show(this, () => JobOffers.Create(text, JobOfferDetails.Blank, school), $"Job offer letter · {_current.Post}", $"Job offer letter {_current.Post}");
    }

    private void Inform(string message) =>
        KryptonMessageBox.Show(this, message, "Job offer letters", KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Comparing and copying the wording of a letter.</summary>
public static class JobOffersText
{
    public static string Key(JobOfferTemplate t) => string.Join('\u0001', t.Heading, t.CampusLine, t.Quote, t.Title, t.Intro, t.Terms, t.Signatory, t.Acceptance);

    public static JobOfferTemplate Copy(JobOfferTemplate from, int? postId) => new()
    {
        StaffTypeId = postId, Heading = from.Heading, CampusLine = from.CampusLine, Quote = from.Quote, Title = from.Title,
        Intro = from.Intro, Terms = from.Terms, Signatory = from.Signatory, Acceptance = from.Acceptance,
    };
}
