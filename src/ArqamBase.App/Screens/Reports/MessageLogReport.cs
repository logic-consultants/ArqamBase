using ArqamBase.App.Controls;
using ArqamBase.App.Core;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Screens.Reports;

/// <summary>Everything sent from the Messages screen between two dates, with the reason for any failure.</summary>
public sealed class MessageLogReport : ReportView
{
    public sealed class Row
    {
        public DateTime SentOn { get; init; }
        public string SentBy { get; init; } = "";
        public string Message { get; init; } = "";
        public string About { get; init; } = "";
        public string Name { get; init; } = "";
        public string Channel { get; init; } = "";
        public string SentTo { get; init; } = "";
        public string Status { get; init; } = "";
        public string Detail { get; init; } = "";
        public string Body { get; init; } = "";
    }

    private readonly Krypton.Toolkit.KryptonDateTimePicker _from = DatePicker(), _to = DatePicker();
    private FilterBindingList<Row>? _list;

    public MessageLogReport(ScreenContext ctx) : base(ctx)
    {
        _from.Value = DateTime.Today.AddDays(-30);
        _to.Value = DateTime.Today;
        Filters.Controls.AddRange([_from, _to]);
        AddShowButton();
        Grid.AddText(nameof(Row.SentOn), "Sent on", 115, format: "dd/MM/yyyy HH:mm");
        Grid.AddText(nameof(Row.Message), "Message", 120);
        Grid.AddText(nameof(Row.About), "About", 110);
        Grid.AddText(nameof(Row.Name), "Name", 160, fill: true);
        Grid.AddText(nameof(Row.Channel), "By", 70);
        Grid.AddText(nameof(Row.SentTo), "Sent to", 140);
        Grid.AddText(nameof(Row.Status), "Status", 60);
        Grid.AddText(nameof(Row.Detail), "Detail", 180);
        Grid.AddText(nameof(Row.SentBy), "Sent by", 90);
    }

    public override string Title => "Message log";
    public override string Replaces => "the Access WhatsApp log";
    public override string StatusText => $"{_list?.Count ?? 0:N0} messages";

    protected override string FilterText => $"{_from.Value:dd/MM/yyyy} to {_to.Value:dd/MM/yyyy}";
    protected override IReadOnlyList<(string Name, string Property)> PrintGroups => [];

    protected override IReadOnlyList<(string Label, string Value)> Summary
    {
        get
        {
            var rows = ShownRows<Row>().ToList();
            return rows.Count == 0 ? [] : [("Sent", $"{rows.Count(r => r.Status == "Sent"):N0}"), ("Test", $"{rows.Count(r => r.Status == "Test"):N0}"), ("Failed", $"{rows.Count(r => r.Status == "Failed"):N0}")];
        }
    }

    protected override async Task LoadAsync()
    {
        var (from, to) = (_from.Value.Date, _to.Value.Date.AddDays(1));
        var titles = MessageType.All.ToDictionary(t => t.Key, t => t.Title);
        var rows = await Task.Run(() =>
        {
            using var db = Ctx.Config.CreateContext(Ctx.Session);
            return db.MessageLogs.AsNoTracking().Where(l => l.SentOn >= from && l.SentOn < to).OrderByDescending(l => l.SentOn).ToList().Select(l => new Row
            {
                SentOn = l.SentOn, SentBy = l.SentBy, Message = titles.GetValueOrDefault(l.Kind, l.Kind), About = l.RefKey, Name = l.PersonName, Channel = l.Channel,
                SentTo = l.SentTo, Status = l.Status, Detail = l.Detail ?? "", Body = l.Body,
            }).ToList();
        });
        if (IsDisposed) return;
        _list = new FilterBindingList<Row>(rows);
        Grid.Bind(_list);
        RaiseStatusChanged();
    }
}
