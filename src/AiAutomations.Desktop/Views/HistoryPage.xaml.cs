using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AiAutomations.Desktop.Views;

public sealed partial class HistoryPage : Page
{
    private readonly TriageAppService _triageAppService;
    private readonly DesktopRealtimeNotifier _notifier;

    public HistoryPage()
    {
        InitializeComponent();
        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        _notifier = App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>();

        // Previously History only loaded once on Loaded - a decision made while this tab was
        // open never showed up until you navigated away and back.
        _notifier.DecisionReady += OnDecisionReady;

        Loaded += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => _notifier.DecisionReady -= OnDecisionReady;
    }

    private void OnDecisionReady(MessageDecisionDto _) => DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private async Task RefreshAsync()
    {
        var decisionsTask = _triageAppService.GetRecentDecisionsAsync(200);
        var messagesTask = _triageAppService.GetRecentMessagesAsync(200);
        await Task.WhenAll(decisionsTask, messagesTask);

        var messagesById = (await messagesTask).ToDictionary(m => m.Id);
        var rows = (await decisionsTask)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => (Decision: d, Message: messagesById.GetValueOrDefault(d.MessageId)))
            .ToList();

        RowsPanel.Children.Clear();

        if (rows.Count == 0)
        {
            RowsPanel.Children.Add(new TextBlock { Text = "Nothing triaged yet.", Foreground = new SolidColorBrush(Colors.Gray) });
            return;
        }

        foreach (var (decision, message) in rows)
        {
            RowsPanel.Children.Add(BuildRow(decision, message));
        }
    }

    private static FrameworkElement BuildRow(MessageDecisionDto decision, SlackMessageDto? message)
    {
        var grid = new Grid { Padding = new Thickness(0, 10, 0, 10), ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var when = new TextBlock
        {
            Text = decision.CreatedAtUtc.ToLocalTime().ToString("g"),
            Foreground = new SolidColorBrush(Colors.Gray),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(when, 0);

        var from = new TextBlock
        {
            Text = message is null ? string.Empty : $"{(message.IsDirectMessage ? "DM" : "mention")} {message.DisplayName}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(from, 1);

        var intent = new Border
        {
            Background = new SolidColorBrush(IntentColor(decision.Intent)),
            // A radius far larger than the border's own half-height/width (e.g. the common
            // CSS "999px pill" trick) isn't clamped the way it would be in CSS - WinUI3's
            // rounded-rect geometry overshoots and self-intersects into a pointed lens shape.
            // 10 is just above half this badge's ~19px rendered height, which is all a pill needs.
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = decision.Intent.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.Black) }
        };
        Grid.SetColumn(intent, 2);

        var summary = new TextBlock
        {
            Text = decision.Summary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(summary, 3);

        grid.Children.Add(when);
        grid.Children.Add(from);
        grid.Children.Add(intent);
        grid.Children.Add(summary);

        // Always present a link button when the item originated from a Slack message - absent
        // only for the rare row where no message record survived (shouldn't normally happen,
        // since every decision is derived from one).
        if (!string.IsNullOrWhiteSpace(message?.Permalink))
        {
            var viewInSlackButton = new HyperlinkButton
            {
                Content = "View in Slack",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            viewInSlackButton.Click += async (_, _) =>
            {
                if (Uri.TryCreate(message.Permalink, UriKind.Absolute, out var uri))
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
            };
            Grid.SetColumn(viewInSlackButton, 4);
            grid.Children.Add(viewInSlackButton);
        }

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.2 },
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };

        return border;
    }

    private static Windows.UI.Color IntentColor(MessageIntent intent) => intent switch
    {
        MessageIntent.FyiOnly => Colors.LightGray,
        MessageIntent.ReplyNeeded => Colors.LightSkyBlue,
        MessageIntent.TaskNeeded => Colors.Khaki,
        MessageIntent.ImplementationNeeded or MessageIntent.PlanningNeeded => Colors.LightSalmon,
        _ => Colors.LightGray
    };
}
