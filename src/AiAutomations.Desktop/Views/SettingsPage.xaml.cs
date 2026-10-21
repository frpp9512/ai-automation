using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.Services;
using AiAutomations.Desktop.Views.Controls;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AiAutomations.Desktop.Views;

public sealed partial class SettingsPage : Page
{
    private readonly TriageAppService _triageAppService;
    private bool _initializingThemePicker;

    public SettingsPage()
    {
        InitializeComponent();
        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();

        _initializingThemePicker = true;
        var currentTheme = ThemeManager.Load();
        foreach (var item in ThemePicker.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == currentTheme.ToString())
            {
                ThemePicker.SelectedItem = item;
                break;
            }
        }
        _initializingThemePicker = false;

        LoadConnectionsFields();
        LoadOpenProjectFields();

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            await RefreshOpenProjectStatusAsync();
        };
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingThemePicker || ThemePicker.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var theme = Enum.Parse<ElementTheme>(tag);
        App.MainWindow?.SetTheme(theme);
    }

    private void LoadConnectionsFields()
    {
        var values = DesktopSettingsEditor.Load();
        SlackBotTokenBox.Password = values.SlackBotToken ?? string.Empty;
        SlackAppLevelTokenBox.Password = values.SlackAppLevelToken ?? string.Empty;
        SlackUserTokenBox.Password = values.SlackUserToken ?? string.Empty;
        SlackSelfUserIdBox.Text = values.SlackSelfUserId ?? string.Empty;

        foreach (var item in AiProviderBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == values.AiProvider)
            {
                AiProviderBox.SelectedItem = item;
                break;
            }
        }

        AnthropicApiKeyBox.Password = values.AnthropicApiKey ?? string.Empty;
        AnthropicModelBox.Text = values.AnthropicModel;
        OpenAiApiKeyBox.Password = values.OpenAiCompatibleApiKey ?? string.Empty;
        OpenAiBaseUrlBox.Text = values.OpenAiCompatibleBaseUrl;
        OpenAiModelBox.Text = values.OpenAiCompatibleModel;
    }

    private void OnSaveConnections(object sender, RoutedEventArgs e)
    {
        var provider = (AiProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "OpenAiCompatible";

        // Loads the current values first (rather than constructing a brand-new record) so saving
        // this card never clobbers the OpenProject fields the other card owns.
        var current = DesktopSettingsEditor.Load();
        DesktopSettingsEditor.Save(current with
        {
            SlackBotToken = SlackBotTokenBox.Password,
            SlackAppLevelToken = SlackAppLevelTokenBox.Password,
            SlackUserToken = SlackUserTokenBox.Password,
            SlackSelfUserId = SlackSelfUserIdBox.Text,
            AiProvider = provider,
            AnthropicApiKey = AnthropicApiKeyBox.Password,
            AnthropicModel = string.IsNullOrWhiteSpace(AnthropicModelBox.Text) ? "claude-sonnet-5-5" : AnthropicModelBox.Text,
            OpenAiCompatibleApiKey = OpenAiApiKeyBox.Password,
            OpenAiCompatibleBaseUrl = string.IsNullOrWhiteSpace(OpenAiBaseUrlBox.Text) ? "https://openrouter.ai/api/v1" : OpenAiBaseUrlBox.Text,
            OpenAiCompatibleModel = string.IsNullOrWhiteSpace(OpenAiModelBox.Text) ? "openrouter/free" : OpenAiModelBox.Text
        });

        ConnectionsSavedBar.Severity = InfoBarSeverity.Success;
        ConnectionsSavedBar.Title = "Saved";
        ConnectionsSavedBar.Message = "Restart the app for these changes to take effect.";
        ConnectionsSavedBar.IsOpen = true;
        RestartButton.Visibility = Visibility.Visible;
    }

    private void OnRestartApp(object sender, RoutedEventArgs e) => AppInstance.Restart(string.Empty);

    private void LoadOpenProjectFields()
    {
        var values = DesktopSettingsEditor.Load();
        OpenProjectBaseUrlBox.Text = values.OpenProjectBaseUrl ?? string.Empty;
        OpenProjectApiTokenBox.Password = values.OpenProjectApiToken ?? string.Empty;
        OpenProjectIntervalBox.Value = values.OpenProjectSyncIntervalMinutes;
        OpenProjectThresholdBox.Value = values.OpenProjectRepositoryMatchConfidenceThreshold;

        foreach (var item in OpenProjectStatusFilterBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == values.OpenProjectStatusFilter)
            {
                OpenProjectStatusFilterBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnSaveOpenProject(object sender, RoutedEventArgs e)
    {
        var current = DesktopSettingsEditor.Load();
        var statusFilter = (OpenProjectStatusFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        DesktopSettingsEditor.Save(current with
        {
            OpenProjectBaseUrl = OpenProjectBaseUrlBox.Text,
            OpenProjectApiToken = OpenProjectApiTokenBox.Password,
            OpenProjectSyncIntervalMinutes = (int)(double.IsNaN(OpenProjectIntervalBox.Value) ? 15 : OpenProjectIntervalBox.Value),
            OpenProjectStatusFilter = statusFilter,
            OpenProjectRepositoryMatchConfidenceThreshold = (int)(double.IsNaN(OpenProjectThresholdBox.Value) ? 80 : OpenProjectThresholdBox.Value)
        });

        OpenProjectSavedBar.Severity = InfoBarSeverity.Success;
        OpenProjectSavedBar.Title = "Saved";
        OpenProjectSavedBar.Message = "Restart the app for these changes to take effect.";
        OpenProjectSavedBar.IsOpen = true;
        RestartButton.Visibility = Visibility.Visible;
    }

    private async void OnTestOpenProjectConnection(object sender, RoutedEventArgs e)
    {
        TestOpenProjectConnectionButton.IsEnabled = false;
        OpenProjectStatusBar.IsOpen = false;
        try
        {
            // Tests the currently-running connection (built from settings at last launch), not
            // whatever is unsaved in the fields above - save first if they were just edited.
            var ok = await _triageAppService.TestOpenProjectConnectionAsync();
            OpenProjectStatusBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            OpenProjectStatusBar.Title = ok ? "Connected" : "Could not connect";
            OpenProjectStatusBar.Message = ok
                ? "OpenProject is reachable with the saved base URL/token."
                : "Check the base URL/token above, save, and restart the app before testing again.";
            OpenProjectStatusBar.IsOpen = true;
        }
        finally
        {
            TestOpenProjectConnectionButton.IsEnabled = true;
        }
    }

    private async void OnSyncNow(object sender, RoutedEventArgs e)
    {
        SyncNowButton.IsEnabled = false;
        OpenProjectStatusBar.IsOpen = false;
        try
        {
            await _triageAppService.SyncOpenProjectNowAsync();
            await RefreshOpenProjectStatusAsync();
        }
        catch (Exception ex)
        {
            // async void click handlers must never let an exception escape unhandled - this app
            // has no global unhandled-exception handler, so that would crash the whole process.
            OpenProjectStatusBar.Severity = InfoBarSeverity.Error;
            OpenProjectStatusBar.Title = "Sync failed";
            OpenProjectStatusBar.Message = ex.Message;
            OpenProjectStatusBar.IsOpen = true;
        }
        finally
        {
            SyncNowButton.IsEnabled = true;
        }
    }

    private async Task RefreshOpenProjectStatusAsync()
    {
        var run = await _triageAppService.GetLastOpenProjectSyncRunAsync();
        if (run is null)
        {
            return;
        }

        var completed = run.CompletedAtUtc?.ToLocalTime().ToString("g") ?? "in progress";
        if (run.ErrorMessage is not null)
        {
            OpenProjectStatusBar.Severity = InfoBarSeverity.Error;
            OpenProjectStatusBar.Title = $"Last sync failed ({completed})";
            OpenProjectStatusBar.Message = run.ErrorMessage;
        }
        else
        {
            OpenProjectStatusBar.Severity = InfoBarSeverity.Informational;
            OpenProjectStatusBar.Title = $"Last sync {completed}";
            OpenProjectStatusBar.Message = $"{run.ItemsCreated} new, {run.ItemsUpdated} updated, {run.ItemsFailed} failed.";
        }
        OpenProjectStatusBar.IsOpen = true;
    }

    private async void OnExportSettings(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"aiautomations-settings-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("JSON settings", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(file.Path, DesktopSettingsEditor.ExportRawJson());
            ConnectionsSavedBar.Severity = InfoBarSeverity.Success;
            ConnectionsSavedBar.Title = "Exported";
            ConnectionsSavedBar.Message = file.Path;
            ConnectionsSavedBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ConnectionsSavedBar.Severity = InfoBarSeverity.Error;
            ConnectionsSavedBar.Title = "Export failed";
            ConnectionsSavedBar.Message = ex.Message;
            ConnectionsSavedBar.IsOpen = true;
        }
    }

    private async void OnImportSettings(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Import settings?",
            Content = "This replaces all current connection settings with the contents of the chosen file. Restart the app afterward for it to take effect.",
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            DesktopSettingsEditor.ImportRawJson(await File.ReadAllTextAsync(file.Path));
            LoadConnectionsFields();

            ConnectionsSavedBar.Severity = InfoBarSeverity.Success;
            ConnectionsSavedBar.Title = "Imported";
            ConnectionsSavedBar.Message = "Restart the app for these changes to take effect.";
            ConnectionsSavedBar.IsOpen = true;
            RestartButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ConnectionsSavedBar.Severity = InfoBarSeverity.Error;
            ConnectionsSavedBar.Title = "Import failed";
            ConnectionsSavedBar.Message = ex.Message;
            ConnectionsSavedBar.IsOpen = true;
        }
    }

    private async void OnBackupDatabase(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"aiautomations-backup-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("SQLite database", [".db"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            BackupRestoreBar.IsOpen = false;
            await _triageAppService.BackupDatabaseAsync(file.Path);
            BackupRestoreBar.Severity = InfoBarSeverity.Success;
            BackupRestoreBar.Title = "Backup complete";
            BackupRestoreBar.Message = file.Path;
            BackupRestoreBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            BackupRestoreBar.Severity = InfoBarSeverity.Error;
            BackupRestoreBar.Title = "Backup failed";
            BackupRestoreBar.Message = ex.Message;
            BackupRestoreBar.IsOpen = true;
        }
    }

    private async void OnRestoreDatabase(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window)
        {
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".db");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore from backup?",
            Content = "This replaces everything currently in the database with the contents of the backup file, and restarts the app immediately. This can't be undone.",
            PrimaryButtonText = "Restore and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            _triageAppService.RestoreDatabase(file.Path);
            AppInstance.Restart(string.Empty);
        }
        catch (Exception ex)
        {
            BackupRestoreBar.Severity = InfoBarSeverity.Error;
            BackupRestoreBar.Title = "Restore failed";
            BackupRestoreBar.Message = ex.Message;
            BackupRestoreBar.IsOpen = true;
        }
    }

    private async Task RefreshAsync()
    {
        var repos = await _triageAppService.GetRepositoriesAsync();

        RepoPanel.Children.Clear();

        if (repos.Count == 0)
        {
            RepoPanel.Children.Add(new TextBlock { Text = "No repositories registered yet.", Foreground = new SolidColorBrush(Colors.Gray) });
            return;
        }

        foreach (var repo in repos)
        {
            RepoPanel.Children.Add(BuildRow(repo));
        }
    }

    private FrameworkElement BuildRow(RepositoryProfileDto repo)
    {
        var grid = new Grid { Padding = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = repo.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 0);

        var path = new TextBlock { Text = repo.Path, FontFamily = new FontFamily("Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(path, 1);

        var keywords = new TextBlock { Text = repo.Keywords ?? string.Empty, Foreground = new SolidColorBrush(Colors.Gray), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(keywords, 2);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var editButton = new Button { Content = "Edit" };
        editButton.Click += async (_, _) => await EditAsync(repo);
        var removeButton = new Button { Content = "Remove" };
        removeButton.Click += async (_, _) => await DeleteAsync(repo.Id);
        buttons.Children.Add(editButton);
        buttons.Children.Add(removeButton);
        Grid.SetColumn(buttons, 3);

        grid.Children.Add(name);
        grid.Children.Add(path);
        grid.Children.Add(keywords);
        grid.Children.Add(buttons);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.2 },
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private async Task DeleteAsync(Guid id)
    {
        await _triageAppService.DeleteRepositoryAsync(id);
        await RefreshAsync();
    }

    private async Task EditAsync(RepositoryProfileDto repo)
    {
        var dialog = new RepositoryEditDialog(repo) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await _triageAppService.UpdateRepositoryAsync(repo.Id, dialog.RepoName, dialog.RepoPath, dialog.RepoKeywords);
        await RefreshAsync();
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewNameBox.Text) || string.IsNullOrWhiteSpace(NewPathBox.Text))
        {
            return;
        }

        await _triageAppService.CreateRepositoryAsync(
            NewNameBox.Text,
            NewPathBox.Text,
            string.IsNullOrWhiteSpace(NewKeywordsBox.Text) ? null : NewKeywordsBox.Text);

        NewNameBox.Text = "";
        NewPathBox.Text = "";
        NewKeywordsBox.Text = "";

        await RefreshAsync();
    }
}
