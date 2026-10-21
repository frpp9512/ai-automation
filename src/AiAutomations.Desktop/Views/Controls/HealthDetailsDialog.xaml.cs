using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AiAutomations.Desktop.Views.Controls;

public sealed partial class HealthDetailsDialog : ContentDialog
{
    private readonly TriageAppService _triageAppService;

    public HealthDetailsDialog(TriageAppService triageAppService)
    {
        InitializeComponent();
        _triageAppService = triageAppService;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        HealthStatusDto? health;
        try
        {
            health = await _triageAppService.GetHealthAsync();
        }
        catch
        {
            health = null;
        }

        AiUsageSummaryDto? usage;
        try
        {
            usage = await _triageAppService.GetAiUsageSummaryAsync();
        }
        catch
        {
            usage = null;
        }

        RenderAiUsage(usage);

        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;

        SetDot(DbDot, health?.DatabaseOk);
        DbStatusText.Text = health is null ? "Unknown" : health.DatabaseOk ? "Connected" : "Not reachable";

        SetDot(SlackDot, health?.SlackConnected);
        SlackStatusText.Text = health is null ? "Unknown" : health.SlackConnected ? "Connected" : "Not connected";

        SetDot(AiDot, health?.AiProviderConfigured);
        AiStatusText.Text = health is null
            ? "Unknown"
            : $"{health.AiProvider} - {(health.AiProviderConfigured ? "configured" : "no API key set")}";

        SetDot(OpenProjectDot, health?.OpenProjectConnected);
        OpenProjectStatusText.Text = health is null
            ? "Unknown"
            : health.OpenProjectConnected ? "Last sync attempt succeeded" : "Not connected (or not configured yet)";

        PendingCountText.Text = health?.PendingMessageCount.ToString() ?? "-";

        var notConfigured = health is not null && (!health.SlackConnected || !health.AiProviderConfigured);
        if (notConfigured)
        {
            ConfigWarning.Message = $"Edit {DesktopConfiguration.SettingsFilePath} and restart the app.";
        }
        ConfigWarning.IsOpen = notConfigured;
    }

    private static void SetDot(Ellipse dot, bool? ok) => dot.Fill = new SolidColorBrush(ok switch
    {
        true => Colors.LimeGreen,
        false => Colors.IndianRed,
        null => Colors.Gray
    });

    private void RenderAiUsage(AiUsageSummaryDto? usage)
    {
        AiUsageByModelPanel.Children.Clear();

        if (usage is null)
        {
            AiUsageTilesGrid.Visibility = Visibility.Collapsed;
            AiUsageUnavailableText.Visibility = Visibility.Visible;
            AiUsageEmptyText.Visibility = Visibility.Collapsed;
            return;
        }

        if (usage.CallCount == 0)
        {
            AiUsageTilesGrid.Visibility = Visibility.Collapsed;
            AiUsageUnavailableText.Visibility = Visibility.Collapsed;
            AiUsageEmptyText.Visibility = Visibility.Visible;
            return;
        }

        AiUsageTilesGrid.Visibility = Visibility.Visible;
        AiUsageUnavailableText.Visibility = Visibility.Collapsed;
        AiUsageEmptyText.Visibility = Visibility.Collapsed;

        CallsCountText.Text = usage.CallCount.ToString("N0");
        TokensText.Text = $"{Abbreviate(usage.InputTokens)}/{Abbreviate(usage.OutputTokens)}";
        CostText.Text = usage.EstimatedCostUsd is { } cost ? $"${cost:0.0000}" : "-";

        var maxCalls = usage.ByModel.Count > 0 ? usage.ByModel.Max(m => m.CallCount) : 0;
        foreach (var model in usage.ByModel)
        {
            AiUsageByModelPanel.Children.Add(BuildModelRow(model, maxCalls));
        }
    }

    /// <summary>Compact "1.2k"-style formatting so a busy day's token counts don't force the
    /// stat tile to wrap or overflow its fixed width.</summary>
    private static string Abbreviate(long value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000.0:0.#}M",
        >= 1_000 => $"{value / 1_000.0:0.#}k",
        _ => value.ToString("N0")
    };

    private static FrameworkElement BuildModelRow(AiUsageByModelDto model, int maxCalls)
    {
        var fraction = maxCalls == 0 ? 0 : Math.Clamp((double)model.CallCount / maxCalls, 0.03, 1);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"{model.Provider} · {model.Model}",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 0);

        var statsText = model.EstimatedCostUsd is { } cost
            ? $"{model.CallCount} call(s) · {Abbreviate(model.InputTokens)}/{Abbreviate(model.OutputTokens)} · ~${cost:0.0000}"
            : $"{model.CallCount} call(s) · {Abbreviate(model.InputTokens)}/{Abbreviate(model.OutputTokens)}";
        var stats = new TextBlock
        {
            Text = statsText,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Gray)
        };
        Grid.SetColumn(stats, 1);

        header.Children.Add(label);
        header.Children.Add(stats);

        // Proportional two-segment bar (filled share + remaining track) via star-sized columns,
        // so it scales with the dialog's actual width instead of a guessed pixel value.
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 2, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });

        var fill = new Border { Background = new SolidColorBrush(Colors.DodgerBlue), CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(fill, 0);
        var track = new Border { Background = new SolidColorBrush(Colors.Gray) { Opacity = 0.2 }, CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(track, 1);
        bar.Children.Add(track);
        bar.Children.Add(fill);

        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(header);
        panel.Children.Add(bar);
        return panel;
    }
}
