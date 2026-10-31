using System.Text.Json;
using ArqamBase.Data;
using Microsoft.Data.SqlClient;

namespace ArqamBase.App.Core;

/// <summary>Machine settings from appsettings.json. ARQAMBASE_CONNECTION overrides the connection string.</summary>
public sealed class AppConfig
{
    public string ConnectionString { get; init; } = "";
    /// <summary>Shared folder for student pictures and documents. ARQAMBASE_ATTACHMENTS overrides it.</summary>
    public string StudentAttachmentsFolder { get; init; } = @"\\das-svr\arqambase\StudentsAttachments";

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
        return new AppConfig
        {
            ConnectionString = string.IsNullOrWhiteSpace(overrideConn) ? config.ConnectionString : overrideConn,
            StudentAttachmentsFolder = string.IsNullOrWhiteSpace(overrideFolder) ? config.StudentAttachmentsFolder : overrideFolder,
        };
    }

    public ArqamDbContext CreateContext(UserSession session) => ArqamDbContext.Create(ConnectionString, session.UserName, session.WindowsToken);
}

public enum ThemeChoice { FollowWindows, Light, Dark, Office2007Blue }

/// <summary>Per-user preferences, stored in %LocalAppData%\ArqamBase\settings.json.</summary>
public sealed class UserSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.FollowWindows;
    public bool NavigationPaneOpen { get; set; } = true;
    public int NavigationPaneWidth { get; set; } = 240;
    public string? LastUserName { get; set; }

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
