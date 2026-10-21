using Microsoft.Extensions.Configuration;

namespace AiAutomations.Desktop.Core;

/// <summary>
/// Loads the desktop app's local settings file - same section shape as the server's
/// appsettings.json (Slack:*/Ai:*/Anthropic:*/OpenAiCompatible:*/Claude:*) so the two
/// stay configured the same way, just from a hand-edited file instead of Docker env vars.
/// </summary>
public static class DesktopConfiguration
{
    public const string AppFolderName = "AiAutomations";
    public const string SettingsFileName = "settings.json";

    private const string Template = """
        {
          "ConnectionStrings": {
            "Default": "Data Source=aiautomations.db"
          },
          "Slack": {
            "BotToken": "",
            "AppLevelToken": "",
            "UserToken": "",
            "SelfUserId": ""
          },
          "Ai": {
            "Provider": "OpenAiCompatible"
          },
          "Anthropic": {
            "ApiKey": "",
            "Model": "claude-sonnet-5-5",
            "MaxTokens": 1024
          },
          "OpenAiCompatible": {
            "ApiKey": "",
            "BaseUrl": "https://openrouter.ai/api/v1",
            "Model": "openrouter/free",
            "MaxTokens": 2048
          },
          "Claude": {
            "AutoLaunch": false
          },
          "OpenProject": {
            "BaseUrl": "",
            "ApiToken": "",
            "SyncIntervalMinutes": 15,
            "StatusFilter": "all",
            "RepositoryMatchConfidenceThreshold": 80
          }
        }
        """;

    /// <summary>
    /// The app's local storage folder - also where the desktop SQLite DB lives. For a packaged
    /// (MSIX) run this is Windows.Storage.ApplicationData's documented per-app folder, the
    /// standard place a packaged desktop app is meant to keep its local data (no manifest
    /// capability needed, unlike disabling the platform's AppData virtualization). Falls back
    /// to a plain %LocalAppData%\AiAutomations folder when there's no package identity, e.g.
    /// for unpackaged dev/test tooling.
    /// </summary>
    public static string AppDataDirectory { get; } = ResolveAppDataDirectory();

    private static string ResolveAppDataDirectory()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
        }
    }

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, SettingsFileName);

    /// <summary>
    /// Creates a blank settings file on first run (so there's something to hand-edit) and
    /// builds configuration from it, resolving the SQLite connection string's relative
    /// path against AppDataDirectory rather than the process's working directory.
    /// </summary>
    public static IConfiguration Load()
    {
        Directory.CreateDirectory(AppDataDirectory);

        if (!File.Exists(SettingsFilePath))
        {
            File.WriteAllText(SettingsFilePath, Template);
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(SettingsFilePath, optional: false, reloadOnChange: false)
            .Build();

        return ResolveConnectionString(configuration);
    }

    private static IConfiguration ResolveConnectionString(IConfiguration configuration)
    {
        var dbPath = Path.Combine(AppDataDirectory, "aiautomations.db");
        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={dbPath}"
        };

        return new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(overrides)
            .Build();
    }
}
