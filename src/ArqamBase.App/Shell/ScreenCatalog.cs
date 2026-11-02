using ArqamBase.App.Core;
using ArqamBase.App.Screens;

namespace ArqamBase.App.Shell;

/// <summary>One entry in the navigation pane and ribbon.</summary>
public sealed record ScreenInfo(string Key, string Title, string Group, char Glyph, int Phase, string Replaces = "",
    Func<ScreenContext, ScreenBase>? Create = null, bool AdminOnly = false);

public static class ScreenCatalog
{
    public static readonly IReadOnlyList<ScreenInfo> All =
    [
        new("dashboard", "Dashboard", "Home", Icons.Home, 1, Create: c => new DashboardScreen(c)),

        new("students", "Students", "Students", Icons.Person, 2, "frmStudents", c => new StudentsScreen(c)),
        new("family", "Family", "Students", Icons.People, 2, "frmFamilyDetails", c => new FamilyScreen(c)),
        new("classes", "Class information", "Students", Icons.School, 6, "frmClassInformation"),
        new("promotion", "Promotion", "Students", Icons.Up, 6, "frmClassPromote"),

        new("ledger", "Fee ledger", "Fees", Icons.Ledger, 3, "Ledger", c => new FeeLedgerScreen(c)),
        new("periodicfee", "Periodic fee update", "Fees", Icons.Refresh, 3, "Feeupdate", c => new PeriodicFeeScreen(c)),
        new("feedef", "Fee definition", "Fees", Icons.Money, 3, "frmFeeDefinition, frmClassFeeIncrease", c => new FeeDefinitionScreen(c)),
        new("challans", "Print challans", "Fees", Icons.Print, 3, "frmPrintChallanClasewise", c => new PrintChallansScreen(c)),

        new("attendance", "Mark attendance", "Attendance", Icons.Check, 6, "frmClassAttendance"),
        new("holidays", "Holidays", "Attendance", Icons.Calendar, 1, Create: c => new SetupListsScreen(c, "Holidays")),

        new("staff", "Staff", "Staff & salaries", Icons.IdCard, 6, "frmStaff"),
        new("salary", "Salary sheet", "Staff & salaries", Icons.Money, 7, "Excel salary sheets"),
        new("advances", "Employee advances", "Staff & salaries", Icons.Receipt, 7),

        new("vouchers", "Vouchers", "Accounts", Icons.Receipt, 8, "ACC_Toba accounting database"),
        new("coa", "Chart of accounts", "Accounts", Icons.Book, 8),
        new("accledger", "Account ledger", "Accounts", Icons.Ledger, 8),

        new("messages", "WhatsApp, SMS & email", "Messages", Icons.Chat, 5, "frmUnpaidWhatsApp, frmWhatsAppsend"),

        new("reports", "Reports centre", "Reports", Icons.Chart, 4, "the Access reports"),

        new("setuplists", "Setup lists", "Setup", Icons.Grid, 1, Create: c => new SetupListsScreen(c)),
        new("schoolinfo", "School information", "Setup", Icons.School, 2, "CampusInfo", c => new SchoolInfoScreen(c)),
        new("settings", "Settings", "Setup", Icons.Settings, 1, Create: c => new SettingsScreen(c)),
    ];

    public static ScreenInfo Get(string key) => All.First(s => s.Key == key);

    public static ScreenBase CreateScreen(ScreenInfo info, ScreenContext ctx) =>
        info.Create?.Invoke(ctx) ?? new ComingSoonScreen(ctx, info.Title, info.Phase, info.Replaces);
}
