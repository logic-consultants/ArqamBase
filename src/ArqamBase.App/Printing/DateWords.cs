namespace ArqamBase.App.Printing;

/// <summary>Dates written out for certificates, e.g. "Fifteenth March, Two Thousand Eighteen" (as DateInWords2 in Access).</summary>
public static class DateWords
{
    private static readonly string[] Days =
    [
        "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth",
        "Eleventh", "Twelfth", "Thirteenth", "Fourteenth", "Fifteenth", "Sixteenth", "Seventeenth", "Eighteenth", "Nineteenth", "Twentieth",
        "Twenty First", "Twenty Second", "Twenty Third", "Twenty Fourth", "Twenty Fifth", "Twenty Sixth", "Twenty Seventh", "Twenty Eighth",
        "Twenty Ninth", "Thirtieth", "Thirty First"
    ];

    private static readonly string[] Ones =
        ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
         "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Of(DateTime date) => $"{Days[date.Day - 1]} {date:MMMM}, {Number(date.Year)}";

    /// <summary>Whole numbers below 10,000 in words.</summary>
    public static string Number(int n)
    {
        if (n == 0) return "Zero";
        var parts = new List<string>();
        if (n >= 1000) { parts.Add($"{Ones[n / 1000]} Thousand"); n %= 1000; }
        if (n >= 100) { parts.Add($"{Ones[n / 100]} Hundred"); n %= 100; }
        if (n >= 20) { parts.Add(Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "")); n = 0; }
        if (n > 0) parts.Add(Ones[n]);
        return string.Join(" ", parts);
    }
}
