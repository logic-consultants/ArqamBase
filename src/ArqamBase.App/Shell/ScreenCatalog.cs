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

        new("attendance", "Mark attendance", "Attendance", Icons.Check, 6, "frmClassAttendance", c => new AttendanceScreen(c)),
        new("staffattendance", "Staff attendance", "Attendance", Icons.IdCard, 6, Create: c => new StaffAttendanceScreen(c)),
        new("staffschedule", "Staff schedule", "Attendance", Icons.Clock, 6, Create: c => new StaffScheduleScreen(c), AdminOnly: true),
        new("holidays", "Holidays", "Attendance", Icons.Calendar, 1, Create: c => new SetupListsScreen(c, "Holidays")),

        new("staff", "Staff", "Staff & salaries", Icons.IdCard, 6, "frmStaff", c => new StaffScreen(c)),
        new("salary", "Salary sheet", "Staff & salaries", Icons.Money, 7, "Excel salary sheets", c => new SalarySheetScreen(c), AdminOnly: true),
        new("advances", "Employee advances", "Staff & salaries", Icons.Receipt, 7, "Advance Request Form", c => new AdvancesScreen(c), AdminOnly: true),
        new("joboffers", "Job offer letters", "Staff & salaries", Icons.Document, 7, "Job offer letter (Word)", c => new JobOfferTemplatesScreen(c), AdminOnly: true),

        new("vouchers", "Vouchers", "Accounts", Icons.Receipt, 8, "ACC_Toba accounting database"),
        new("coa", "Chart of accounts", "Accounts", Icons.Book, 8),
        new("accledger", "Account ledger", "Accounts", Icons.Ledger, 8),

        new("messages", "WhatsApp & email", "Messages", Icons.Chat, 5, "frmUnpaidWhatsApp, frmWhatsAppsend", c => new MessagesScreen(c), AdminOnly: true),
        new("messagetemplates", "Message templates", "Messages", Icons.Document, 5, Create: c => new MessageTemplatesScreen(c), AdminOnly: true),
        new("messagesettings", "Message settings", "Messages", Icons.Settings, 5, Create: c => new MessageSettingsScreen(c), AdminOnly: true),

        new("reports", "Reports centre", "Reports", Icons.Chart, 4, "the Access reports", c => new ReportsScreen(c)),

        new("setuplists", "Setup lists", "Setup", Icons.Grid, 1, Create: c => new SetupListsScreen(c)),
        new("schoolinfo", "School information", "Setup", Icons.School, 2, "CampusInfo", c => new SchoolInfoScreen(c)),
        new("settings", "Settings", "Setup", Icons.Settings, 1, Create: c => new SettingsScreen(c)),
    ];

    public static ScreenInfo Get(string key) => All.First(s => s.Key == key);

    /// <summary>The screens this user sees in the navigation pane, ribbon and dashboard.</summary>
    public static IEnumerable<ScreenInfo> For(UserSession session) => All.Where(s => (!s.AdminOnly || session.IsAdmin) && (Area(s) is not { } area || session.CanView(area)));

    /// <summary>The area a screen belongs to; none for the dashboard and Settings, which everyone has.</summary>
    private static AppArea? Area(ScreenInfo s) => s.Key switch
    {
        "dashboard" or "settings" => null,
        "holidays" => AppArea.Setup,
        "staffattendance" or "staffschedule" => AppArea.Staff,
        _ => s.Group switch
        {
            "Students" => AppArea.Students,
            "Fees" => AppArea.Fees,
            "Attendance" => AppArea.Attendance,
            "Staff & salaries" => AppArea.Staff,
            "Accounts" => AppArea.Accounts,
            "Messages" => AppArea.Messages,
            "Reports" => AppArea.Reports,
            _ => AppArea.Setup,
        },
    };

    public static ScreenBase CreateScreen(ScreenInfo info, ScreenContext ctx) =>
        info.Create?.Invoke(ctx) ?? new ComingSoonScreen(ctx, info.Title, info.Phase, info.Replaces);
}
