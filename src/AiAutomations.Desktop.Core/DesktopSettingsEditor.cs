using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiAutomations.Desktop.Core;

/// <summary>
/// Read/write access to the actual values in the hand-edited settings.json - distinct from
/// DesktopConfiguration.Load(), which builds the read-only IConfiguration the host was composed
/// from at launch. Lets the Settings page show and update Slack/AI connection values without the
/// user opening the file directly; a change here only takes effect after an app restart, since
/// the Slack gateway and AI engine are built once from that configuration snapshot at startup.
/// </summary>
public static class DesktopSettingsEditor
{
    public static DesktopSettingsValues Load()
    {
        var root = ReadRoot();

        return new DesktopSettingsValues(
            SlackBotToken: Get(root, "Slack", "BotToken"),
            SlackAppLevelToken: Get(root, "Slack", "AppLevelToken"),
            SlackUserToken: Get(root, "Slack", "UserToken"),
            SlackSelfUserId: Get(root, "Slack", "SelfUserId"),
            AiProvider: Get(root, "Ai", "Provider") is { Length: > 0 } provider ? provider : "OpenAiCompatible",
            AnthropicApiKey: Get(root, "Anthropic", "ApiKey"),
            AnthropicModel: Get(root, "Anthropic", "Model") is { Length: > 0 } am ? am : "claude-sonnet-5-5",
            OpenAiCompatibleApiKey: Get(root, "OpenAiCompatible", "ApiKey"),
            OpenAiCompatibleBaseUrl: Get(root, "OpenAiCompatible", "BaseUrl") is { Length: > 0 } bu ? bu : "https://openrouter.ai/api/v1",
            OpenAiCompatibleModel: Get(root, "OpenAiCompatible", "Model") is { Length: > 0 } om ? om : "openrouter/free");
    }

    public static void Save(DesktopSettingsValues values)
    {
        Directory.CreateDirectory(DesktopConfiguration.AppDataDirectory);
        var root = ReadRoot();

        Set(root, "Slack", "BotToken", values.SlackBotToken);
        Set(root, "Slack", "AppLevelToken", values.SlackAppLevelToken);
        Set(root, "Slack", "UserToken", values.SlackUserToken);
        Set(root, "Slack", "SelfUserId", values.SlackSelfUserId);
        Set(root, "Ai", "Provider", values.AiProvider);
        Set(root, "Anthropic", "ApiKey", values.AnthropicApiKey);
        Set(root, "Anthropic", "Model", values.AnthropicModel);
        Set(root, "OpenAiCompatible", "ApiKey", values.OpenAiCompatibleApiKey);
        Set(root, "OpenAiCompatible", "BaseUrl", values.OpenAiCompatibleBaseUrl);
        Set(root, "OpenAiCompatible", "Model", values.OpenAiCompatibleModel);

        // Fields this editor doesn't expose - leave as-is if present, default a brand-new file.
        EnsureDefault(root, "ConnectionStrings", "Default", "Data Source=aiautomations.db");
        EnsureDefault(root, "Anthropic", "MaxTokens", 1024);
        EnsureDefault(root, "OpenAiCompatible", "MaxTokens", 2048);
        EnsureDefault(root, "Claude", "AutoLaunch", false);

        File.WriteAllText(DesktopConfiguration.SettingsFilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Raw settings.json contents, for exporting to a file outside the app's packaged
    /// storage - same rationale as the database backup: an uninstall wipes LocalState entirely.</summary>
    public static string ExportRawJson() =>
        File.Exists(DesktopConfiguration.SettingsFilePath)
            ? File.ReadAllText(DesktopConfiguration.SettingsFilePath)
            : throw new InvalidOperationException("No settings file exists yet.");

    /// <summary>Replaces the whole settings.json with a previously exported copy. Validates it's
    /// well-formed JSON first so a corrupt/unrelated file can't brick the live settings; still
    /// only takes effect after the app is restarted, like any other change through this editor.</summary>
    public static void ImportRawJson(string json)
    {
        using var _ = JsonDocument.Parse(json);

        Directory.CreateDirectory(DesktopConfiguration.AppDataDirectory);
        File.WriteAllText(DesktopConfiguration.SettingsFilePath, json);
    }

    private static JsonObject ReadRoot()
    {
        if (!File.Exists(DesktopConfiguration.SettingsFilePath))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(File.ReadAllText(DesktopConfiguration.SettingsFilePath)) as JsonObject ?? new JsonObject();
    }

    private static string? Get(JsonObject root, string section, string key) =>
        (root[section] as JsonObject)?[key]?.GetValue<string>();

    private static void Set(JsonObject root, string section, string key, string? value)
    {
        if (root[section] is not JsonObject sectionNode)
        {
            sectionNode = new JsonObject();
            root[section] = sectionNode;
        }

        sectionNode[key] = value ?? string.Empty;
    }

    private static void EnsureDefault(JsonObject root, string section, string key, object defaultValue)
    {
        if (root[section] is not JsonObject sectionNode)
        {
            sectionNode = new JsonObject();
            root[section] = sectionNode;
        }

        sectionNode[key] ??= JsonValue.Create(defaultValue);
    }
}

public record DesktopSettingsValues(
    string? SlackBotToken,
    string? SlackAppLevelToken,
    string? SlackUserToken,
    string? SlackSelfUserId,
    string AiProvider,
    string? AnthropicApiKey,
    string AnthropicModel,
    string? OpenAiCompatibleApiKey,
    string OpenAiCompatibleBaseUrl,
    string OpenAiCompatibleModel);
