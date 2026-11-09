using System.Globalization;
using ArqamBase.Data;

namespace ArqamBase.App.Core;

/// <summary>Reading typed times, and working out late and early leaving from the staff schedule.</summary>
public static class StaffTimes
{
    /// <summary>The schedule covering a date, if any.</summary>
    public static StaffSchedule? For(IEnumerable<StaffSchedule> schedules, DateTime date) =>
        schedules.Where(s => s.From.Date <= date.Date && s.To.Date >= date.Date).MaxBy(s => s.From);

    /// <summary>Accepts 08:05, 8:05, 8.05, 805, 0805, 8 and 15. Empty text is a valid "no time".</summary>
    public static bool TryParse(string? text, out TimeSpan? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var t = text.Trim().Replace('.', ':');
        if (!t.Contains(':'))
            t = t.Length <= 2 ? $"{t}:00" : t.PadLeft(4, '0').Insert(2, ":");
        if (!TimeSpan.TryParseExact(t, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var v) || v >= TimeSpan.FromDays(1)) return false;
        time = v;
        return true;
    }

    /// <summary>The times offered in drop-downs: every 5 minutes from 06:00 to 20:00.</summary>
    public static IReadOnlyList<string> Choices { get; } =
        Enumerable.Range(0, 169).Select(i => Text(TimeSpan.FromMinutes(360 + i * 5))).ToList();

    public static string Text(TimeSpan time) => time.ToString(@"hh\:mm");

    /// <summary>Nobody leaves before they arrive, so a time out of 3 or 3:00 before check-in means the afternoon.</summary>
    public static TimeSpan Afternoon(TimeSpan timeOut, StaffSchedule? schedule) =>
        schedule is not null && timeOut < schedule.CheckIn && timeOut.TotalHours < 12 ? timeOut + TimeSpan.FromHours(12) : timeOut;

    /// <summary>Minutes after check-in, or 0 when within the minutes allowed.</summary>
    public static int LateBy(StaffSchedule? schedule, TimeSpan? timeIn)
    {
        if (schedule is null || timeIn is not { } t) return 0;
        var minutes = (int)(t - schedule.CheckIn).TotalMinutes;
        return minutes > schedule.LateMinutes ? minutes : 0;
    }

    /// <summary>Minutes before check-out, or 0 when within the minutes allowed.</summary>
    public static int EarlyBy(StaffSchedule? schedule, TimeSpan? timeOut)
    {
        if (schedule is null || timeOut is not { } t) return 0;
        var minutes = (int)(schedule.CheckOut - t).TotalMinutes;
        return minutes > schedule.EarlyMinutes ? minutes : 0;
    }
}
