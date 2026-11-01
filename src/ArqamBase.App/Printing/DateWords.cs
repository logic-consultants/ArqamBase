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

    /// <summary>Whole numbers in words, in crores, lakhs and thousands (e.g. "One Lakh Twenty Five Thousand").</summary>
    public static string Number(int n)
    {
        if (n == 0) return "Zero";
        if (n < 0) return "Minus " + Number(-n);
        var parts = new List<string>();
        foreach (var (size, name) in new[] { (10_000_000, "Crore"), (100_000, "Lakh"), (1000, "Thousand"), (100, "Hundred") })
            if (n >= size) { parts.Add($"{BelowHundred(n / size)} {name}"); n %= size; }
        if (n > 0) parts.Add(BelowHundred(n));
        return string.Join(" ", parts);
    }

    private static string BelowHundred(int n) =>
        n >= 100 ? Number(n) : n >= 20 ? Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "") : Ones[n];
}
