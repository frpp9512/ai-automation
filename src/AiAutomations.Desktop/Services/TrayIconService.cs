using System.Drawing;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Services;

/// <summary>
/// System tray icon created in code (no XAML host needed) - left-click or the "Open" menu
/// item brings the main window back; "Exit" is the only way to actually shut the app down,
/// since MainWindow's own close button just hides to tray.
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;

    public event Action? OpenRequested;

    /// <summary>Fired with a MainWindow nav section tag ("Inbox"/"Tasks"/"History"/"Settings")
    /// when the user picks it directly from the tray menu.</summary>
    public event Action<string>? NavigateRequested;

    public event Action? HealthRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        // H.NotifyIcon's default ContextMenuMode (PopupMenu) renders this as a native Win32
        // popup menu and wires clicks to each item's Command, NOT its Click event - Click
        // handlers are silently never invoked.
        var openItem = new MenuFlyoutItem { Text = "Open", Command = new RelayCommand(() => OpenRequested?.Invoke()) };
        var dashboardItem = new MenuFlyoutItem { Text = "Dashboard", Command = new RelayCommand(() => NavigateRequested?.Invoke("Dashboard")) };
        var inboxItem = new MenuFlyoutItem { Text = "Inbox", Command = new RelayCommand(() => NavigateRequested?.Invoke("Inbox")) };
        var tasksItem = new MenuFlyoutItem { Text = "Tasks", Command = new RelayCommand(() => NavigateRequested?.Invoke("Tasks")) };
        var historyItem = new MenuFlyoutItem { Text = "History", Command = new RelayCommand(() => NavigateRequested?.Invoke("History")) };
        var settingsItem = new MenuFlyoutItem { Text = "Settings", Command = new RelayCommand(() => NavigateRequested?.Invoke("Settings")) };
        var aboutItem = new MenuFlyoutItem { Text = "About", Command = new RelayCommand(() => NavigateRequested?.Invoke("About")) };
        var healthItem = new MenuFlyoutItem { Text = "Health...", Command = new RelayCommand(() => HealthRequested?.Invoke()) };
        var exitItem = new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(() => ExitRequested?.Invoke()) };

        var menu = new MenuFlyout();
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(dashboardItem);
        menu.Items.Add(inboxItem);
        menu.Items.Add(tasksItem);
        menu.Items.Add(historyItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(healthItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);

        _taskbarIcon = new TaskbarIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = "AI Automations",
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(() => OpenRequested?.Invoke()),
            DoubleClickCommand = new RelayCommand(() => OpenRequested?.Invoke())
        };
        // enablesEfficiencyMode defaults to true, which runs the whole process in a
        // throttled "hidden/background" power mode - wrong for an app whose tray icon
        // exists specifically so the window can be brought back on demand.
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Dispose() => _taskbarIcon.Dispose();

    private static Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
