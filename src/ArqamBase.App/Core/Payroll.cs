using ArqamBase.Data;

namespace ArqamBase.App.Core;

/// <summary>The salary sheet's rules, as worked in the Excel salary workbook.</summary>
public static class Payroll
{
    /// <summary>Days of absence or leave in a month that are not deducted.</summary>
    public const int FreeLeaves = 1;

    public static readonly IReadOnlyList<(byte Id, string Name)> Sheets =
        [(1, "Teaching Staff (Girls)"), (2, "Teaching Staff (Boys)"), (3, "Non Teaching Staff"), (4, "Directors")];

    public static readonly string[] PayMethods = ["BAHL", "CASH"];

    public static string SheetName(byte? id) => Sheets.FirstOrDefault(s => s.Id == id).Name ?? "No sheet chosen";

    /// <summary>D.D: a day's pay for each day absent or on leave after the free one.</summary>
    public static decimal AbsenceDeduction(decimal salary, int days, int absent) =>
        days <= 0 ? 0 : Math.Round(salary / days * Math.Max(absent - FreeLeaves, 0), 2);

    public static decimal Paid(decimal salary, decimal incentive, decimal deduction, decimal advance, decimal scf, decimal security) =>
        Math.Round(salary + incentive - deduction - advance - scf - security, 2);

    /// <summary>
    /// This month's security deduction: the deposit equals the first salary, so the last deduction is what remains and then it stops.
    /// With no first salary on record the monthly amount stands.
    /// </summary>
    public static decimal SecurityDue(decimal? firstSalary, decimal monthly, decimal depositedBefore) =>
        firstSalary > 0 ? Math.Clamp(firstSalary.Value - depositedBefore, 0, monthly) : monthly;

    /// <summary>The Rem. sec text: what is still to be deposited, or Clear; none when there is no first salary on record.</summary>
    public static string? SecurityLeft(decimal? firstSalary, decimal deposited) =>
        firstSalary > 0 ? (firstSalary.Value > deposited ? $"{firstSalary.Value - deposited:0.##}" : "Clear") : null;

    /// <summary>The key of a month, e.g. 202610.</summary>
    public static string PeriodKey(DateTime month) => month.ToString("yyyyMM");

    public static DateTime PeriodMonth(string period) =>
        new(int.Parse(period[..4]), int.Parse(period[4..6]), 1);

    /// <summary>A month as the sheets show it, e.g. OCT-2026.</summary>
    public static string PeriodText(DateTime month) => month.ToString("MMM-yyyy").ToUpperInvariant();

    /// <summary>What is left of each advance (oldest first), after the amounts already deducted.</summary>
    public static List<(StaffAdvance Advance, decimal Left)> AdvancesLeft(IEnumerable<StaffAdvance> advances, decimal deducted)
    {
        var result = new List<(StaffAdvance, decimal)>();
        foreach (var a in advances.OrderBy(a => a.Date).ThenBy(a => a.Id))
        {
            var taken = Math.Min(a.Amount, deducted);
            deducted -= taken;
            result.Add((a, a.Amount - taken));
        }
        return result;
    }

    /// <summary>This month's instalments of the advances still owed and due to start by this month.</summary>
    public static decimal AdvanceDue(IEnumerable<StaffAdvance> advances, decimal deducted, string period) =>
        AdvancesLeft(advances, deducted).Where(x => x.Left > 0 && string.CompareOrdinal(x.Advance.FirstPeriod, period) <= 0)
            .Sum(x => Math.Min(x.Advance.Installment, x.Left));
}
