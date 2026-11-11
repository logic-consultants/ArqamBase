using System.Globalization;

namespace ArqamBase.App.Controls;

/// <summary>How a column is filtered: text (as shown), a number, or a date.</summary>
public enum FilterKind { Text, Number, Date }

/// <summary>The condition of a column filter. Text: Contains, StartsWith, Exact. Numbers: Equal, Between, Greater, Less. Dates: Equal, Between, Before, After.</summary>
public enum FilterOp { Contains, StartsWith, Exact, Equal, Between, Greater, Less, Before, After }

/// <summary>
/// One column's filter. Text compares with what the grid shows, ignoring case. Numbers and dates compare values
/// (dates by day, so the time is ignored); Between includes both ends. A blank value never matches a number or date.
/// </summary>
public sealed record ColumnFilter(FilterOp Op, string Text = "", decimal? Number1 = null, decimal? Number2 = null, DateTime? Date1 = null, DateTime? Date2 = null)
{
    public bool Matches(object? value)
    {
        switch (Op)
        {
            case FilterOp.Contains or FilterOp.StartsWith or FilterOp.Exact:
                var text = (value as string ?? value?.ToString() ?? "").Trim();
                return Op switch
                {
                    FilterOp.Contains => text.Contains(Text, StringComparison.CurrentCultureIgnoreCase),
                    FilterOp.StartsWith => text.StartsWith(Text, StringComparison.CurrentCultureIgnoreCase),
                    _ => string.Equals(text, Text, StringComparison.CurrentCultureIgnoreCase),
                };
            case FilterOp.Before or FilterOp.After:
            case FilterOp.Equal or FilterOp.Between when Date1 is not null:
                if (value is not DateTime d) return false;
                var day = d.Date;
                return Op switch
                {
                    FilterOp.Equal => day == Date1!.Value.Date,
                    FilterOp.Between => day >= Date1!.Value.Date && day <= (Date2 ?? Date1).Value.Date,
                    FilterOp.Before => day < Date1!.Value.Date,
                    _ => day > Date1!.Value.Date,
                };
            default:
                if (Number(value) is not { } n || Number1 is not { } a) return false;
                return Op switch
                {
                    FilterOp.Equal => n == a,
                    FilterOp.Between => n >= Math.Min(a, Number2 ?? a) && n <= Math.Max(a, Number2 ?? a),
                    FilterOp.Greater => n > a,
                    _ => n < a,
                };
        }
    }

    private static decimal? Number(object? value)
    {
        try { return value is null or string ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException) { return null; }
    }

    /// <summary>A number typed by the user: "12,800", "1500.5". Null when it is not a number.</summary>
    public static decimal? ParseNumber(string text) =>
        decimal.TryParse(text.Trim().Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A date typed as dd/MM/yyyy (also d/M/yy and dd-MM-yyyy). Null when it is not a date.</summary>
    public static DateTime? ParseDate(string text) =>
        DateTime.TryParseExact(text.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "d/M/yy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
}
