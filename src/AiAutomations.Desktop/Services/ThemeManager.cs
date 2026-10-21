using Microsoft.UI.Xaml;
using Windows.Storage;

namespace AiAutomations.Desktop.Services;

/// <summary>
/// Light/Dark/Auto theme preference - stored in ApplicationData.LocalSettings (a small per-user
/// preference, unlike the Slack/AI secrets which live in the hand-edited settings.json) and
/// applied by setting RequestedTheme on the window's root element, which cascades to every page
/// and dialog attached to that XamlRoot.
/// </summary>
public static class ThemeManager
{
    private const string SettingsKey = "AppTheme";

    public static ElementTheme Load()
    {
        var raw = ApplicationData.Current.LocalSettings.Values[SettingsKey] as string;
        return raw switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    public static void Save(ElementTheme theme)
    {
        ApplicationData.Current.LocalSettings.Values[SettingsKey] = theme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Auto"
        };
    }

    public static void Apply(Window window, ElementTheme theme)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }
}
