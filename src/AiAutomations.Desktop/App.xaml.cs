using System.Diagnostics;
using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.Services;
using AiAutomations.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace AiAutomations.Desktop;

public partial class App : Application
{
    private const string SingleInstanceKey = "AiAutomationsDesktopMain";

    public static IHost Host { get; private set; } = null!;

    public static ToastNotificationService? ToastNotificationService { get; private set; }

    public static MainWindow? MainWindow { get; private set; }

    private MainWindow? _mainWindow;
    private TrayIconService? _trayIconService;
    private ToastNotificationService? _toastNotificationService;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            Process.GetCurrentProcess().Kill();
            return;
        }

        var configuration = DesktopConfiguration.Load();
        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddConfiguration(configuration);
        hostBuilder.Services.AddDesktopHost(configuration);
        Host = hostBuilder.Build();

        using (var scope = Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            await DatabaseSchemaUpgrader.ApplyAsync(db);
            await DefaultRepositorySeed.EnsureSeededAsync(db);
        }

        await Host.StartAsync();

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        _trayIconService = new TrayIconService();
        _trayIconService.OpenRequested += () => _mainWindow.ShowAndActivate();
        _trayIconService.ExitRequested += async () => await ExitAsync();

        // ToastNotificationService's constructor subscribes NotificationInvoked - that MUST
        // happen before Register() is called ("Must register event handlers before calling
        // Register()"), and Register() must in turn happen before GetActivatedEventArgs() so a
        // cold launch from clicking a notification is correctly reported as one.
        _toastNotificationService = new ToastNotificationService(
            Host.Services.GetRequiredService<DesktopRealtimeNotifier>(),
            Host.Services.GetRequiredService<TriageAppService>(),
            _mainWindow.DispatcherQueue);
        _toastNotificationService.Activated += actionId => _mainWindow.ShowActionDetail(actionId);
        _toastNotificationService.WorkItemActivated += workItemId => _mainWindow.ShowWorkItemDetail(workItemId);
        _toastNotificationService.HealthActivated += () => _mainWindow.ShowHealthDetail();
        ToastNotificationService = _toastNotificationService;

        _trayIconService.NavigateRequested += section =>
        {
            _mainWindow.ShowAndActivate();
            _mainWindow.NavigateToSection(section);
        };
        _trayIconService.HealthRequested += () =>
        {
            _mainWindow.ShowAndActivate();
            _mainWindow.ShowHealthDetail();
        };

        AppNotificationManager.Default.Register();
        var launchArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

        if (launchArgs.Kind == ExtendedActivationKind.AppNotification && launchArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
        {
            if (notificationArgs.Arguments.TryGetValue("actionId", out var rawAction) && Guid.TryParse(rawAction, out var actionId))
            {
                _mainWindow.ShowActionDetail(actionId);
            }
            else if (notificationArgs.Arguments.TryGetValue("workItemId", out var rawWorkItem) && Guid.TryParse(rawWorkItem, out var workItemId))
            {
                _mainWindow.ShowWorkItemDetail(workItemId);
            }
            else if (notificationArgs.Arguments.ContainsKey("openHealth"))
            {
                _mainWindow.Activate();
                _mainWindow.ShowHealthDetail();
            }
            else
            {
                _mainWindow.Activate();
            }
        }
        else
        {
            _mainWindow.Activate();
        }
    }

    private async Task ExitAsync()
    {
        _toastNotificationService?.Dispose();
        _trayIconService?.Dispose();
        AppNotificationManager.Default.Unregister();
        await Host.StopAsync();
        _mainWindow?.RequestRealClose();
        _mainWindow?.Close();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
