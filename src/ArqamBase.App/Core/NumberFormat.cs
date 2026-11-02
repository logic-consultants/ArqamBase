namespace ArqamBase.App.Core;

/// <summary>
/// Shows phone, CNIC and Form-B numbers in a fixed layout, and turns what is typed back into plain digits for the database.
/// Numbers are never changed or corrected here (that is done by hand): short numbers fill the layout as far as they go,
/// and numbers too long for it, or text that is not a number, are shown as they are.
/// </summary>
public static class NumberFormat
{
    /// <summary>Phone numbers as (0300) 123-4321; a short number shows as far as it goes, e.g. (0462) 000-623.</summary>
    public static string Phone(string? value) => Layout(value, "(####) ###-####");

    /// <summary>CNIC and Form-B numbers as 33100-1001001-7.</summary>
    public static string Cnic(string? value) => Layout(value, "#####-#######-#");

    /// <summary>Digits in a CNIC or Form-B number.</summary>
    public const int FormBLength = 13;

    /// <summary>
    /// What is saved: the digits only, or null when the box is empty.
    /// Text that is not a number (e.g. "N/A" or a name) is kept as typed rather than lost.
    /// </summary>
    public static string? Plain(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (!IsNumber(text)) return text;
        var digits = Digits(text);
        return digits.Length > 0 ? digits : null;
    }

    public static string Digits(string? value) => value is null ? "" : new string(value.Where(char.IsDigit).ToArray());

    private static bool IsNumber(string text) => text.All(c => char.IsDigit(c) || c is '(' or ')' or '-' or ' ' or '+' or '.');

    /// <summary>Puts the digits into the pattern's # places in order, stopping when the digits run out.</summary>
    private static string Layout(string? value, string pattern)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || !IsNumber(text)) return value ?? "";
        var digits = Digits(text);
        if (digits.Length == 0 || digits.Length > pattern.Count(c => c == '#')) return value!;

        var result = new System.Text.StringBuilder();
        var next = 0;
        foreach (var c in pattern)
        {
            if (next == digits.Length) break;
            if (c == '#') result.Append(digits[next++]);
            else result.Append(c);
        }
        var shown = result.ToString();
        return shown.Contains('(') && !shown.Contains(')') ? shown + ")" : shown; // "(0)" rather than "(0"
    }
}
