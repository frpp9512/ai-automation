using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.Views.Controls;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AiAutomations.Desktop.Views;

/// <summary>
/// The app's landing page - an overview of everything the other pages track in detail (pending
/// approvals, live processing, recent history, task board, AI usage), so opening the app answers
/// "what's going on" before diving into any one section. The Inbox itself is unchanged; this
/// doesn't duplicate its action cards, only a count and a link to it.
/// </summary>
public sealed partial class DashboardPage : Page
{
    private readonly TriageAppService _triageAppService;
    private readonly DesktopRealtimeNotifier _notifier;
    private readonly DispatcherQueueTimer _refreshTimer;

    public DashboardPage()
    {
        InitializeComponent();

        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        _notifier = App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>();

        _notifier.MessageReceived += OnActivity;
        _notifier.DecisionReady += OnActivity;
        _notifier.ActionStatusChanged += OnActivity;
        _notifier.WorkItemChanged += OnActivity;

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(15);
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _refreshTimer.Start();
        };
        Unloaded += (_, _) =>
        {
            _refreshTimer.Stop();
            _notifier.MessageReceived -= OnActivity;
            _notifier.DecisionReady -= OnActivity;
            _notifier.ActionStatusChanged -= OnActivity;
            _notifier.WorkItemChanged -= OnActivity;
        };
    }

    private void OnActivity<T>(T _) => DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private void OnOpenInbox(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateToSection("Inbox");

    private async Task RefreshAsync()
    {
        var pendingTask = _triageAppService.GetPendingActionsAsync();
        var healthTask = _triageAppService.GetHealthAsync();
        var newHistoryTask = _triageAppService.GetDecisionCountSinceAsync(DateTimeOffset.UtcNow.AddHours(-24));
        var activeTasksTask = _triageAppService.GetActiveWorkItemCountAsync();
        var usageTask = _triageAppService.GetAiUsageSummaryAsync();
        var decisionsTask = _triageAppService.GetRecentDecisionsAsync(8);
        var messagesTask = _triageAppService.GetRecentMessagesAsync(200);
        var workItemsTask = _triageAppService.GetWorkItemsAsync();

        await Task.WhenAll(pendingTask, healthTask, newHistoryTask, activeTasksTask, usageTask, decisionsTask, messagesTask, workItemsTask);

        var pendingCount = (await pendingTask).Count;
        PendingCountText.Text = pendingCount.ToString("N0");
        PendingCta.Visibility = pendingCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingCtaTitle.Text = pendingCount == 1 ? "1 item needs your review" : $"{pendingCount} items need your review";

        var health = await healthTask;
        ProcessingCountText.Text = health.PendingMessageCount.ToString("N0");
        SetDot(DbDot, health.DatabaseOk);
        DbStatusText.Text = health.DatabaseOk ? "Connected" : "Not reachable";
        SetDot(SlackDot, health.SlackConnected);
        SlackStatusText.Text = health.SlackConnected ? "Connected" : "Not connected";
        SetDot(AiDot, health.AiProviderConfigured);
        AiStatusText.Text = $"{health.AiProvider} - {(health.AiProviderConfigured ? "configured" : "no API key set")}";

        SetDot(OpenProjectDot, health.OpenProjectConnected);
        OpenProjectStatusText.Text = health.OpenProjectConnected
            ? "Last sync attempt succeeded"
            : "Not connected (or not configured yet)";

        NewHistoryCountText.Text = (await newHistoryTask).ToString("N0");
        ActiveTasksCountText.Text = (await activeTasksTask).ToString("N0");

        var usage = await usageTask;
        AiCallsCountText.Text = usage.CallCount.ToString("N0");

        RenderRecentActivity(await decisionsTask, await messagesTask);
        RenderTasksByStatus(await workItemsTask);
        RenderAiUsageByModel(usage);
    }

    private void RenderRecentActivity(IReadOnlyList<MessageDecisionDto> decisions, IReadOnlyList<SlackMessageDto> messages)
    {
        RecentActivityPanel.Children.Clear();

        if (decisions.Count == 0)
        {
            RecentActivityEmptyText.Visibility = Visibility.Visible;
            return;
        }

        RecentActivityEmptyText.Visibility = Visibility.Collapsed;
        var messagesById = messages.ToDictionary(m => m.Id);

        foreach (var decision in decisions.OrderByDescending(d => d.CreatedAtUtc))
        {
            var message = messagesById.GetValueOrDefault(decision.MessageId);
            RecentActivityPanel.Children.Add(BuildActivityRow(decision, message));
        }
    }

    private static FrameworkElement BuildActivityRow(MessageDecisionDto decision, SlackMessageDto? message)
    {
        var grid = new Grid { Padding = new Thickness(0, 8, 0, 8), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var when = new TextBlock
        {
            Text = decision.CreatedAtUtc.ToLocalTime().ToString("g"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(when, 0);

        var intent = new Border
        {
            Background = new SolidColorBrush(IntentColor(decision.Intent)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = decision.Intent.ToString(), FontSize = 10, Foreground = new SolidColorBrush(Colors.Black) }
        };
        Grid.SetColumn(intent, 1);

        var summary = new TextBlock
        {
            Text = message is null ? decision.Summary : $"{message.DisplayName}: {decision.Summary}",
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(summary, 2);

        grid.Children.Add(when);
        grid.Children.Add(intent);
        grid.Children.Add(summary);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.15 },
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Windows.UI.Color IntentColor(MessageIntent intent) => intent switch
    {
        MessageIntent.FyiOnly => Colors.LightGray,
        MessageIntent.ReplyNeeded => Colors.LightSkyBlue,
        MessageIntent.TaskNeeded => Colors.Khaki,
        MessageIntent.ImplementationNeeded or MessageIntent.PlanningNeeded => Colors.LightSalmon,
        _ => Colors.LightGray
    };

    private void RenderTasksByStatus(IReadOnlyList<WorkItemDto> items)
    {
        TasksByStatusPanel.Children.Clear();

        var counts = WorkItemCardControl.Columns.ToDictionary(s => s, s => items.Count(i => i.Status == s));
        var max = counts.Values.DefaultIfEmpty(0).Max();

        foreach (var status in WorkItemCardControl.Columns)
        {
            TasksByStatusPanel.Children.Add(BuildBarRow(WorkItemCardControl.ColumnTitle(status), counts[status], max));
        }
    }

    private void RenderAiUsageByModel(AiUsageSummaryDto usage)
    {
        AiUsageByModelPanel.Children.Clear();

        if (usage.ByModel.Count == 0)
        {
            AiUsageEmptyText.Visibility = Visibility.Visible;
            return;
        }

        AiUsageEmptyText.Visibility = Visibility.Collapsed;
        var max = usage.ByModel.Max(m => m.CallCount);

        foreach (var model in usage.ByModel)
        {
            AiUsageByModelPanel.Children.Add(BuildBarRow($"{model.Provider} · {model.Model}", model.CallCount, max));
        }
    }

    /// <summary>Shared "label — count" row with a proportional two-segment bar (filled share +
    /// remaining track) via star-sized Grid columns, so it scales with the card's actual width
    /// instead of a guessed pixel value.</summary>
    private static FrameworkElement BuildBarRow(string label, int count, int max)
    {
        var fraction = max == 0 ? 0 : Math.Clamp((double)count / max, count == 0 ? 0 : 0.03, 1);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock { Text = label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(labelText, 0);

        var countText = new TextBlock { Text = count.ToString("N0"), FontSize = 12, Foreground = new SolidColorBrush(Colors.Gray) };
        Grid.SetColumn(countText, 1);

        header.Children.Add(labelText);
        header.Children.Add(countText);

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

    private static void SetDot(Ellipse dot, bool ok) => dot.Fill = new SolidColorBrush(ok ? Colors.LimeGreen : Colors.IndianRed);
}
