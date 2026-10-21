using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// Global health indicator - lives in MainWindow's title bar (not any one page) so it's visible
/// no matter which section is open. Polls GetHealthAsync periodically, plus refreshes promptly
/// on relevant realtime events, and raises a toast whenever a status actually flips (not on
/// every poll - only on a change) so a Slack drop or missing AI key doesn't go unnoticed just
/// because no page happens to be open to show it.
/// </summary>
public sealed partial class HealthStripControl : UserControl
{
    private readonly TriageAppService _triageAppService;
    private readonly DesktopRealtimeNotifier _notifier;
    private readonly DispatcherQueueTimer _timer;

    private HealthStatusDto? _lastHealth;

    public HealthStripControl()
    {
        InitializeComponent();

        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        _notifier = App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>();

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(15);
        _timer.IsRepeating = true;
        _timer.Tick += async (_, _) => await RefreshAsync();

        _notifier.MessageReceived += OnActivity;
        _notifier.DecisionReady += OnActivity;
        _notifier.ActionStatusChanged += OnActivity;

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _notifier.MessageReceived -= OnActivity;
            _notifier.DecisionReady -= OnActivity;
            _notifier.ActionStatusChanged -= OnActivity;
        };
    }

    private void OnActivity<T>(T _) => DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private async Task RefreshAsync()
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

        NotifyIfChanged(_lastHealth, health);
        _lastHealth = health;

        SetDot(DbDot, health?.DatabaseOk);
        SetDot(SlackDot, health?.SlackConnected);
        SetDot(AiDot, health?.AiProviderConfigured);
        AiLabel.Text = health is null ? "AI" : $"AI ({health.AiProvider})";

        var pending = health?.PendingMessageCount ?? 0;
        PendingPanel.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingText.Text = pending == 1 ? "Processing 1 message" : $"Processing {pending} messages";
    }

    /// <summary>Only toasts on an actual flip, comparing against the previous poll - never on
    /// the first read (no prior value to compare against) and never repeatedly for a status
    /// that's just staying bad.</summary>
    private static void NotifyIfChanged(HealthStatusDto? previous, HealthStatusDto? current)
    {
        if (previous is null || current is null || App.ToastNotificationService is not { } toasts)
        {
            return;
        }

        if (previous.SlackConnected != current.SlackConnected)
        {
            toasts.ShowHealthChangedToast("Slack connection changed",
                current.SlackConnected ? "Slack is connected again." : "Slack disconnected.");
        }

        if (previous.DatabaseOk != current.DatabaseOk)
        {
            toasts.ShowHealthChangedToast("Database status changed",
                current.DatabaseOk ? "Database is reachable again." : "Database is not reachable.");
        }

        if (previous.AiProviderConfigured != current.AiProviderConfigured)
        {
            toasts.ShowHealthChangedToast("AI provider status changed",
                current.AiProviderConfigured ? $"{current.AiProvider} is configured." : $"{current.AiProvider} is missing an API key.");
        }
    }

    private static void SetDot(Ellipse dot, bool? ok) => dot.Fill = new SolidColorBrush(ok switch
    {
        true => Colors.LimeGreen,
        false => Colors.IndianRed,
        null => Colors.Gray
    });

    private async void OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new HealthDetailsDialog(_triageAppService) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
