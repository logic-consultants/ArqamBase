using System.ComponentModel;
using System.Text.RegularExpressions;
using ArqamBase.App.Printing;
using ArqamBase.App.Screens;
using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Core;

public enum MessageKind { StudentAttendance, StaffAttendance, FeeReminder, FeeVoucher, SalaryVoucher }

/// <summary>A type of message: its name, the fields it can use, and the wording it starts with.</summary>
public sealed record MessageType(MessageKind Kind, string Title, string Fields, string Subject, string Body)
{
    public static readonly IReadOnlyList<MessageType> All =
    [
        new(MessageKind.StudentAttendance, "Student attendance", "{Father} {Student} {Class} {Attendance} {Date}",
            "Attendance of {Student}: {Date}",
            "Dear Mr. *{Father}*\nYour child *{Student}* ({Class}) {Attendance} on {Date}.\nIf you have not informed the school, kindly send an application.\n\nRegards,\nSchool Administration"),
        new(MessageKind.StaffAttendance, "Staff attendance", "{Name} {Post} {Attendance} {Date} {TimeIn} {TimeOut}",
            "Your attendance: {Date}",
            "Dear *{Name}*\nOur record shows that you {Attendance} on {Date}.\nIf this is not right, kindly contact the office.\n\nRegards,\nSchool Administration"),
        new(MessageKind.FeeReminder, "Fee reminder", "{Father} {Student} {CC} {Class} {Months} {Periods} {Amount} {DueDate}",
            "School fee reminder: {Student} ({CC})",
            "Dear Mr. *{Father}*\nThis is a gentle reminder that your child *{Student}'s* school fee is still pending for *{Months} ({Periods})*, Rs *{Amount}* in all.\nKindly deposit the fee at your earliest convenience to avoid any inconvenience.\n\nRegards,\nSchool Administration"),
        new(MessageKind.FeeVoucher, "Fee voucher", "{Father} {Student} {CC} {Class} {Months} {Periods} {Amount} {DueDate}",
            "Fee voucher: {Student} ({CC})",
            "Dear Mr. *{Father}*\nThe fee voucher of *{Student}* for {Periods} is attached, Rs *{Amount}* in all, due on {DueDate}.\n\nRegards,\nSchool Administration"),
        new(MessageKind.SalaryVoucher, "Salary voucher", "{Name} {Post} {Month} {Amount}",
            "Salary voucher: {Month}",
            "Dear *{Name}*\nYour salary voucher for {Month} is attached. Amount paid: Rs *{Amount}*.\n\nRegards,\nSchool Administration"),
    ];

    public static MessageType Of(MessageKind kind) => All.First(t => t.Kind == kind);

    public string Key => Kind.ToString();
}

/// <summary>Filling a message's fields, and the number and address it goes to.</summary>
public static partial class MessageText
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Field();

    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Field().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>An email has no WhatsApp bold marks.</summary>
    public static string ForEmail(string text) => text.Replace("*", "");

    /// <summary>A WhatsApp number: 92 and the last 10 digits (as Access sent them), or null.</summary>
    public static string? WhatsAppNumber(string? number)
    {
        var digits = NumberFormat.Digits(number);
        return digits.Length >= 10 ? "92" + digits[^10..] : null;
    }

    /// <summary>Each message type's wording: the Admin's where it has been changed.</summary>
    public static Dictionary<MessageKind, (string Subject, string Body)> Wording(ArqamDbContext db)
    {
        var saved = db.MessageTemplates.AsNoTracking().ToDictionary(t => t.Key);
        return MessageType.All.ToDictionary(t => t.Kind, t => saved.TryGetValue(t.Key, out var s) ? (s.Subject, s.Body) : (t.Subject, t.Body));
    }
}

/// <summary>One person to send to, with the message filled in.</summary>
public sealed class MessageRow : INotifyPropertyChanged
{
    private bool _send;
    private string _result = "";

    public bool Send { get => _send; set { if (_send != value) { _send = value; Changed(nameof(Send)); } } }
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Class or post.</summary>
    public string Detail { get; init; } = "";
    /// <summary>The attendance mark or amount.</summary>
    public string About { get; init; } = "";
    public string? Number { get; init; }
    public string? Email { get; init; }
    public string LastSent { get; set; } = "";
    public string Result { get => _result; set { _result = value; Changed(nameof(Result)); } }

    public string NumberText => Number is null ? "" : $"+{Number}";
    public MessageKind Kind { get; init; }
    public string RefKey { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
    public string? FileName { get; init; }
    /// <summary>The voucher sent with the message, if any.</summary>
    public Func<IDocument>? Document { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>Who each type of message goes to, and what it says.</summary>
public static class MessageData
{
    /// <param name="date">The day, for attendance.</param>
    /// <param name="month">The salary sheet, as yyyyMM.</param>
    /// <param name="everyone">For attendance, everyone marked and not only those absent, on leave or late.</param>
    public static List<MessageRow> Load(MessageKind kind, ArqamDbContext db, AppConfig config, DateTime date, string? month, bool everyone)
    {
        var wording = MessageText.Wording(db)[kind];
        var rows = kind switch
        {
            MessageKind.StudentAttendance => StudentAttendance(db, wording, date, everyone),
            MessageKind.StaffAttendance => StaffAttendance(db, wording, date, everyone),
            MessageKind.FeeReminder or MessageKind.FeeVoucher => Fees(db, config, kind, wording),
            _ => SalaryVouchers(db, wording, month),
        };
        var sent = rows.Count == 0 ? [] : LastSent(db, kind);
        foreach (var r in rows)
        {
            r.LastSent = sent.TryGetValue((r.RefKey, r.Id), out var on) ? on.ToString("dd/MM/yyyy HH:mm") : "";
            r.Send = r.LastSent.Length == 0 && (r.Number is not null || r.Email is not null);
        }
        return rows;
    }

    /// <summary>When each message of this type last really went out; test and failed messages do not count.</summary>
    private static Dictionary<(string, int), DateTime> LastSent(ArqamDbContext db, MessageKind kind)
    {
        var key = kind.ToString();
        return db.MessageLogs.AsNoTracking().Where(l => l.Kind == key && l.Status == "Sent")
            .GroupBy(l => new { l.RefKey, l.PersonId }).Select(g => new { g.Key.RefKey, g.Key.PersonId, On = g.Max(l => l.SentOn) })
            .ToDictionary(x => (x.RefKey, x.PersonId), x => x.On);
    }

    private static string Clean(string? text) => text?.Trim() ?? "";

    private static List<MessageRow> StudentAttendance(ArqamDbContext db, (string Subject, string Body) wording, DateTime date, bool everyone)
    {
        var day = date.Date;
        var marks = (from m in db.StudentAttendance.AsNoTracking()
                     join r in db.AttendanceRegisters.AsNoTracking() on m.RegisterId equals r.Id
                     where r.Date == day && (everyone || m.Status != "P")
                     join s in db.Students.AsNoTracking().Include(s => s.Family) on m.StudentId equals s.Id
                     join c in db.Classes.AsNoTracking() on r.ClassId equals c.Id
                     join sec in db.Sections.AsNoTracking() on r.SectionId equals sec.Id
                     select new { Student = s, m.Status, ClassName = c.Name, SectionName = sec.Name }).ToList();
        return marks.OrderBy(x => x.ClassName).ThenBy(x => x.SectionName).ThenBy(x => x.Student.Name).Select(x =>
        {
            var status = x.Status.Trim().ToUpperInvariant();
            var values = new Dictionary<string, string>
            {
                ["Father"] = Clean(x.Student.Family?.FatherName), ["Student"] = Clean(x.Student.Name), ["Class"] = $"{Clean(x.ClassName)} {Clean(x.SectionName)}".Trim(),
                ["Date"] = day.ToString("dddd dd/MM/yyyy"),
                ["Attendance"] = status switch { "A" => "is absent from school", "L" => "is on leave", "T" => "came late to school", _ => "is present at school" },
            };
            return Row(MessageKind.StudentAttendance, wording, values, x.Student.Id, values["Student"], values["Class"], StatusName(status), day.ToString("yyyy-MM-dd"),
                Pick(x.Student.WhatsApp, x.Student.Family?.Mobile), x.Student.Email);
        }).ToList();
    }

    private static string StatusName(string status) => status switch { "A" => "Absent", "L" => "Leave", "T" => "Late", _ => "Present" };

    private static string? Pick(string? first, string? second) => string.IsNullOrWhiteSpace(first) ? second : first;

    private static List<MessageRow> StaffAttendance(ArqamDbContext db, (string Subject, string Body) wording, DateTime date, bool everyone)
    {
        var day = date.Date;
        var schedule = StaffTimes.For(db.StaffSchedules.AsNoTracking().Where(s => s.From <= day && s.To >= day).ToList(), day);
        var posts = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => Clean(t.Name));
        var marks = (from a in db.StaffAttendance.AsNoTracking()
                     where a.Date == day
                     join s in db.Staff.AsNoTracking() on a.StaffId equals s.Id
                     select new { a, s }).ToList();
        return marks.Select(x =>
        {
            var status = x.a.Status.Trim().ToUpperInvariant();
            var early = status is "P" or "T" ? StaffTimes.EarlyBy(schedule, x.a.TimeOut) : 0;
            var timeIn = x.a.TimeIn is { } i ? StaffTimes.Text(i) : "";
            var timeOut = x.a.TimeOut is { } o ? StaffTimes.Text(o) : "";
            var parts = new List<string>();
            if (status == "A") parts.Add("were absent");
            else if (status == "L") parts.Add("were on leave");
            else if (status == "T") parts.Add(timeIn.Length > 0 ? $"came late at {timeIn}" : "came late");
            if (early > 0) parts.Add($"left early at {timeOut}");
            if (parts.Count == 0 && !everyone) return null;
            if (parts.Count == 0) parts.Add(timeIn.Length > 0 ? $"were present from {timeIn} to {timeOut}" : "were present");
            var post = x.s.TypeId is int t && posts.TryGetValue(t, out var p) ? p : Clean(x.s.Designation);
            var values = new Dictionary<string, string>
            {
                ["Name"] = Clean(x.s.Name), ["Post"] = post, ["Attendance"] = string.Join(" and ", parts), ["Date"] = day.ToString("dddd dd/MM/yyyy"), ["TimeIn"] = timeIn, ["TimeOut"] = timeOut,
            };
            return Row(MessageKind.StaffAttendance, wording, values, x.s.Id, values["Name"], post, StatusName(status) + (early > 0 ? ", left early" : ""), day.ToString("yyyy-MM-dd"), x.s.CellNo, x.s.Email);
        }).OfType<MessageRow>().OrderBy(r => r.Name).ToList();
    }

    /// <summary>On-roll students with unpaid challans. The reference is the periods owed, so a new challan can be sent again.</summary>
    private static List<MessageRow> Fees(ArqamDbContext db, AppConfig config, MessageKind kind, (string Subject, string Body) wording)
    {
        var ids = (from c in db.FeeChallans.AsNoTracking()
                   where !c.Paid && c.StudentId != null
                   join s in db.Students.AsNoTracking() on c.StudentId equals s.Id
                   where s.OnRoll
                   select s.Id).Distinct().ToList();
        var challans = FeeChallanData.Load(db, ids);
        var contacts = db.Students.AsNoTracking().Include(s => s.Family).Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.WhatsApp, s.Email, Mobile = s.Family!.Mobile }).ToDictionary(s => s.Id);
        var school = voucherSchool(db);
        return challans.OrderBy(c => c.ClassName).ThenBy(c => c.SectionName).ThenBy(c => c.Name).Select(c =>
        {
            var contact = contacts[c.Cc];
            var months = c.Lines.Count;
            var values = new Dictionary<string, string>
            {
                ["Father"] = Clean(c.FatherName), ["Student"] = Clean(c.Name), ["CC"] = $"{c.Cc}", ["Class"] = $"{Clean(c.ClassName)} {Clean(c.SectionName)}".Trim(),
                ["Months"] = $"{months} {(months == 1 ? "month" : "months")}", ["Periods"] = c.Periods, ["Amount"] = c.Total.ToString("N0"),
                ["DueDate"] = c.DueDate is { } d ? d.ToString("dd/MM/yyyy") : "",
            };
            var voucher = kind == MessageKind.FeeVoucher;
            return Row(kind, wording, values, c.Cc, values["Student"], values["Class"], $"Rs {values["Amount"]}", c.Periods[..Math.Min(c.Periods.Length, 50)],
                Pick(contact.WhatsApp, contact.Mobile), contact.Email,
                voucher ? $"Fee voucher {c.Cc}.pdf" : null, voucher ? () => Challans.Create([c], school, config.ChallanBank, config.ChallanLateFee) : null);
        }).ToList();
    }

    private static SchoolPrint voucherSchool(ArqamDbContext db) => SchoolPrint.From(db.Campuses.AsNoTracking().OrderBy(c => c.Id).FirstOrDefault());

    private static List<MessageRow> SalaryVouchers(ArqamDbContext db, (string Subject, string Body) wording, string? period)
    {
        var head = db.SalaryHeads.AsNoTracking().Include(h => h.Lines).FirstOrDefault(h => h.Period == period);
        if (head is null) return [];
        var month = DateTime.ParseExact(head.Period, "yyyyMM", null).ToString("MMMM yyyy");
        var ids = head.Lines.Select(l => l.StaffId).ToList();
        var staff = db.Staff.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionary(s => s.Id);
        var posts = db.StaffTypes.AsNoTracking().ToDictionary(t => t.Id, t => Clean(t.Name));
        var schools = db.Schools.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);
        var school = voucherSchool(db);
        return head.Lines.Where(l => staff.ContainsKey(l.StaffId)).Select(l =>
        {
            var s = staff[l.StaffId];
            var post = s.TypeId is int t && posts.TryGetValue(t, out var p) ? p : Clean(s.Designation);
            var schoolName = s.BranchId is int b && schools.TryGetValue(b, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : PrintKit.DefaultSchoolName;
            var voucher = new SalaryVoucher(s.Id, Clean(s.Name), post, SalarySheetScreen.CampusName(schoolName), month, l.BasicSalary ?? 0, l.Days ?? 0, l.AbsentLeave ?? 0,
                l.Incentive ?? 0, l.Deduction ?? 0, l.Scf ?? 0, l.Security ?? 0, l.AdvanceDeducted ?? 0, l.AdvanceBalance ?? 0, l.AmountPaid ?? 0, Clean(l.PayMethod), l.RemainingSecurity);
            var values = new Dictionary<string, string> { ["Name"] = voucher.Name, ["Post"] = post, ["Month"] = month, ["Amount"] = voucher.Paid.ToString("N0") };
            return Row(MessageKind.SalaryVoucher, wording, values, s.Id, voucher.Name, post, $"Rs {values["Amount"]}", head.Period, s.CellNo, s.Email,
                $"Salary voucher {s.Id} {month}.pdf", () => Printing.SalaryVouchers.Create(voucher, schoolName, school));
        }).OrderBy(r => r.Name).ToList();
    }

    private static MessageRow Row(MessageKind kind, (string Subject, string Body) wording, Dictionary<string, string> values, int id, string name, string detail, string about, string refKey,
        string? number, string? email, string? fileName = null, Func<IDocument>? document = null) => new()
    {
        Kind = kind, Id = id, Name = name, Detail = detail, About = about, RefKey = refKey, Number = MessageText.WhatsAppNumber(number),
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(), Subject = MessageText.Fill(wording.Subject, values), Body = MessageText.Fill(wording.Body, values),
        FileName = fileName, Document = document,
    };
}
