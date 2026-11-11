using System.Text.Json;
using ArqamBase.Data;
using Microsoft.Data.SqlClient;

namespace ArqamBase.App.Core;

/// <summary>Machine settings from appsettings.json. ARQAMBASE_CONNECTION overrides the connection string.</summary>
public sealed class AppConfig
{
    public string ConnectionString { get; init; } = "";
    /// <summary>Shared folder for student pictures and documents. ARQAMBASE_ATTACHMENTS overrides it.</summary>
    public string StudentAttachmentsFolder { get; init; } = @"\\das-svr\db_share\arqambase\StudentsAttachments";
    /// <summary>Shared folder for staff pictures and documents. ARQAMBASE_STAFF_ATTACHMENTS overrides it.</summary>
    public string StaffAttachmentsFolder { get; init; } = @"\\das-svr\db_share\arqambase\StaffAttachments";
    /// <summary>
    /// From this date Online Deposit Boys receipts take their numbers from the boys series (with Cash Receipt Boys);
    /// before it they used the girls numbers. Set it to the day the fee counter moves from Access to this app.
    /// </summary>
    public DateTime OnlineBoysOwnSeriesFrom { get; init; } = new(2026, 11, 2);
    /// <summary>Printed on fee challans as "Credited at: …".</summary>
    public string ChallanBank { get; init; } = "Bank Al-Habib Limited (Chamara Mandi)";
    /// <summary>Added to the total for "After due date" on fee challans.</summary>
    public int ChallanLateFee { get; init; } = 100;

    public string Server => new SqlConnectionStringBuilder(ConnectionString).DataSource;
    public string Database => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    /// <summary>The server's computer name, used as the default domain for its local Windows accounts.</summary>
    public string ServerMachine => Server.Split('\\', ',')[0].Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);

    /// <summary>False when a test override connects with a SQL Server login instead of Windows credentials.</summary>
    public bool UsesWindowsLogin => new SqlConnectionStringBuilder(ConnectionString).IntegratedSecurity;

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = File.Exists(path)
            ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig()
            : new AppConfig();

        var overrideConn = Environment.GetEnvironmentVariable("ARQAMBASE_CONNECTION");
        var overrideFolder = Environment.GetEnvironmentVariable("ARQAMBASE_ATTACHMENTS");
        var overrideStaffFolder = Environment.GetEnvironmentVariable("ARQAMBASE_STAFF_ATTACHMENTS");
        return new AppConfig
        {
            ConnectionString = string.IsNullOrWhiteSpace(overrideConn) ? config.ConnectionString : overrideConn,
            StudentAttachmentsFolder = string.IsNullOrWhiteSpace(overrideFolder) ? config.StudentAttachmentsFolder : overrideFolder,
            StaffAttachmentsFolder = string.IsNullOrWhiteSpace(overrideStaffFolder) ? config.StaffAttachmentsFolder : overrideStaffFolder,
            OnlineBoysOwnSeriesFrom = config.OnlineBoysOwnSeriesFrom,
            ChallanBank = config.ChallanBank,
            ChallanLateFee = config.ChallanLateFee,
        };
    }

    public ArqamDbContext CreateContext(UserSession session) => ArqamDbContext.Create(ConnectionString, session.UserName, session.WindowsToken);
}

/// <summary>The numbers are kept from the earlier themes: Light became Silver, Dark became Black, Office 2007 Blue became Classic 2007 Blue.</summary>
public enum ThemeChoice { FollowWindows = 0, Silver = 1, Black = 2, Classic2007Blue = 3, Blue = 4, Green = 5 }

/// <summary>Per-user preferences, stored in %LocalAppData%\ArqamBase\settings.json.</summary>
public sealed class UserSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.Blue;
    public bool NavigationPaneOpen { get; set; } = true;
    public int NavigationPaneWidth { get; set; } = 240;
    public string? LastUserName { get; set; }
    public string FontName { get; set; } = Core.Theme.DefaultFontName;
    public int FontSize { get; set; } = Core.Theme.DefaultFontSize;
    /// <summary>Font of the school name on printouts.</summary>
    public string SchoolNameFont { get; set; } = AppFonts.SchoolNameDefault;
    /// <summary>Text font of the printouts; null keeps each printout's own font (Arial or Times New Roman).</summary>
    public string? PrintFont { get; set; }
    /// <summary>The fee ledger's last-used collection type (Mode), which also picks the receipt number series.</summary>
    public int? FeeCollectionTypeId { get; set; }
    /// <summary>Paper for fee receipts on this computer: A4 (laser printer) or an 80 mm thermal roll.</summary>
    public Printing.ReceiptPaper ReceiptPaper { get; set; } = Printing.ReceiptPaper.A4;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArqamBase", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings()
                : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
