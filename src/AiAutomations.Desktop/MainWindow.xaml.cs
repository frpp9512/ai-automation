using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.Services;
using AiAutomations.Desktop.Views;
using AiAutomations.Desktop.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly TriageAppService _triageAppService;
    private bool _reallyClose;

    // Drives the History nav item's unread badge - reset to "now" every time History is opened,
    // so the badge only ever counts decisions that landed since the last visit.
    private DateTimeOffset _historyLastSeenUtc = DateTimeOffset.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AI Automations";
        AppWindow.Closing += OnAppWindowClosing;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Modern integrated title bar instead of the default system one - draws the app's own
        // icon/title and lets our content occupy the space the system chrome used to take.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var initialTheme = ThemeManager.Load();
        ThemeManager.Apply(this, initialTheme);
        ApplyTitleBarButtonColors(initialTheme);
        UpdateThemeSwitcherIcon(initialTheme);

        ContentFrame.Navigate(typeof(DashboardPage));
        Nav.SelectedItem = Nav.MenuItems[0];

        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        var notifier = App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>();
        notifier.ActionStatusChanged += _ => DispatcherQueue.TryEnqueue(async () => await RefreshBadgesAsync());
        notifier.WorkItemChanged += _ => DispatcherQueue.TryEnqueue(async () => await RefreshBadgesAsync());
        notifier.DecisionReady += _ => DispatcherQueue.TryEnqueue(async () => await RefreshBadgesAsync());

        Activated += async (_, _) => await RefreshBadgesAsync();
    }

    private async Task RefreshBadgesAsync()
    {
        try
        {
            var pendingCount = (await _triageAppService.GetPendingActionsAsync()).Count;
            SetBadge(InboxBadge, pendingCount);

            var activeTaskCount = await _triageAppService.GetActiveWorkItemCountAsync();
            SetBadge(TasksBadge, activeTaskCount);

            var unreadHistoryCount = await _triageAppService.GetDecisionCountSinceAsync(_historyLastSeenUtc);
            SetBadge(HistoryBadge, unreadHistoryCount);
        }
        catch
        {
            // Best-effort - badges just stay at their last known value.
        }
    }

    private static void SetBadge(InfoBadge badge, int count)
    {
        badge.Value = count;
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Call before Close() to let the window actually close instead of hiding to tray.</summary>
    public void RequestRealClose() => _reallyClose = true;

    public void ShowAndActivate()
    {
        AppWindow.Show();

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>Deep-link entry point from a clicked toast notification - brings the window
    /// forward, switches to Inbox, and opens that specific action's detail dialog.</summary>
    public void ShowActionDetail(Guid actionId)
    {
        ShowAndActivate();
        NavigateTo("Inbox");

        if (ContentFrame.Content is InboxPage inboxPage)
        {
            inboxPage.ShowDetailFor(actionId);
        }
    }

    /// <summary>Deep-link entry point from a "task updated" toast - brings the window forward,
    /// switches to Tasks, and opens that specific Work Item's detail dialog.</summary>
    public void ShowWorkItemDetail(Guid workItemId)
    {
        ShowAndActivate();
        NavigateTo("Tasks");

        if (ContentFrame.Content is TasksPage tasksPage)
        {
            tasksPage.ShowDetailFor(workItemId);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem { Tag: string tag })
        {
            NavigateTo(tag);
        }
    }

    /// <summary>Public entry point for the tray menu's direct section links.</summary>
    public void NavigateToSection(string tag) => NavigateTo(tag);

    /// <summary>Applies and persists a theme choice - called from the Settings page's selector
    /// and from the title bar's own quick switcher.</summary>
    public void SetTheme(ElementTheme theme)
    {
        ThemeManager.Save(theme);
        ThemeManager.Apply(this, theme);
        ApplyTitleBarButtonColors(theme);
        UpdateThemeSwitcherIcon(theme);
    }

    /// <summary>Flips between Light and Dark based on whatever's currently resolved (including
    /// from Auto) - this toggle never sets Auto itself, which stays a Settings-page-only choice.</summary>
    private void OnThemeToggleClick(object sender, RoutedEventArgs e)
    {
        var currentlyDark = ((Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark) == ElementTheme.Dark;
        SetTheme(currentlyDark ? ElementTheme.Light : ElementTheme.Dark);
    }

    /// <summary>Sun for Light, crescent moon for Dark/Auto (Auto's glyph tracks whatever it
    /// currently resolves to, via ActualTheme, rather than a separate neutral icon).</summary>
    private void UpdateThemeSwitcherIcon(ElementTheme theme)
    {
        var resolvedTheme = theme == ElementTheme.Default
            ? (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark
            : theme;

        ThemeSwitcherIcon.Glyph = resolvedTheme == ElementTheme.Light ? "" : "";
    }

    /// <summary>
    /// The native title bar caption buttons (minimize/maximize/close) are OS chrome, not part of
    /// the XAML visual tree - ThemeResource bindings never reach them, so their colors have to be
    /// re-applied by hand on startup and every time the theme changes, or they stay stuck at
    /// whatever they were set to when the window was created (e.g. white glyphs invisible against
    /// a light background after switching away from dark).
    /// </summary>
    private void ApplyTitleBarButtonColors(ElementTheme theme)
    {
        if (AppWindow.TitleBar is not { } titleBar)
        {
            return;
        }

        var resolvedTheme = theme == ElementTheme.Default
            ? (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Dark
            : theme;
        var isDark = resolvedTheme == ElementTheme.Dark;

        var foreground = isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        var hoverBackground = isDark ? Windows.UI.Color.FromArgb(25, 255, 255, 255) : Windows.UI.Color.FromArgb(25, 0, 0, 0);
        var pressedBackground = isDark ? Windows.UI.Color.FromArgb(50, 255, 255, 255) : Windows.UI.Color.FromArgb(50, 0, 0, 0);

        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    /// <summary>Opens the health details dialog over whatever's currently showing - used by the
    /// tray menu's "Health..." item and by clicking a health-change toast.</summary>
    public async void ShowHealthDetail()
    {
        var dialog = new HealthDetailsDialog(_triageAppService) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();
    }

    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "Inbox" => typeof(InboxPage),
            "Tasks" => typeof(TasksPage),
            "History" => typeof(HistoryPage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => typeof(DashboardPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        if (tag == "History")
        {
            // The badge only ever counts what's landed since the last visit - opening the page
            // counts as having seen everything currently in it.
            _historyLastSeenUtc = DateTimeOffset.UtcNow;
            SetBadge(HistoryBadge, 0);
        }

        // Keep the sidebar's visual selection in sync even when navigation was triggered
        // programmatically (a notification deep-link) rather than by clicking a nav item.
        foreach (var menuItem in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (menuItem.Tag as string == tag && !ReferenceEquals(Nav.SelectedItem, menuItem))
            {
                Nav.SelectedItem = menuItem;
                break;
            }
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_reallyClose)
        {
            return;
        }

        // Clicking the window's own X just hides it to the tray - only the tray menu's
        // Exit (or RequestRealClose + Close) actually shuts the app down. Window.Closed
        // fires too late to cancel (the window is already gone by then); AppWindow.Closing
        // is the event that can actually be canceled.
        args.Cancel = true;
        AppWindow.Hide();
    }
}
