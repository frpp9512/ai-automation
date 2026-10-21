using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Desktop.Views.Controls;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace AiAutomations.Desktop.Views;

public sealed partial class TasksPage : Page
{
    private readonly TriageAppService _triageAppService;
    private readonly DesktopRealtimeNotifier _notifier;

    private List<WorkItemDto> _items = [];
    private List<RepositoryProfileDto> _repos = [];
    private Dictionary<Guid, int> _commentCounts = [];

    private const string NoRepoOption = "(none)";

    public TasksPage()
    {
        InitializeComponent();

        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
        _notifier = App.Host.Services.GetRequiredService<DesktopRealtimeNotifier>();
        _notifier.WorkItemChanged += OnWorkItemChanged;
        _notifier.WorkItemCommentAdded += OnWorkItemCommentAdded;

        NewPriorityBox.ItemsSource = Enum.GetValues<WorkItemPriority>();
        NewPriorityBox.SelectedItem = WorkItemPriority.Medium;
        NewTypeBox.ItemsSource = Enum.GetValues<WorkItemType>();
        NewTypeBox.SelectedItem = WorkItemType.Task;

        Loaded += async (_, _) => await RefreshAsync();

        // Columns need to know the viewport height to size their drop zone to fill it (see
        // RenderBoard) - re-render on resize so that stays true as the window changes size.
        BoardScrollViewer.SizeChanged += (_, _) => RenderBoard();

        // Frame.Navigate creates a new TasksPage (and a fresh pair of subscriptions above) every
        // time this tab is revisited - without unsubscribing, every past instance would keep
        // refreshing forever.
        Unloaded += (_, _) =>
        {
            _notifier.WorkItemChanged -= OnWorkItemChanged;
            _notifier.WorkItemCommentAdded -= OnWorkItemCommentAdded;
        };
    }

    private void OnWorkItemChanged(WorkItemDto _) => DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private void OnWorkItemCommentAdded(WorkItemDto _, WorkItemCommentDto __) => DispatcherQueue.TryEnqueue(async () => await RefreshAsync());

    private async Task RefreshAsync()
    {
        _items = await _triageAppService.GetWorkItemsAsync();
        _repos = await _triageAppService.GetRepositoriesAsync();
        _commentCounts = await _triageAppService.GetCommentCountsAsync();

        var repoOptions = new List<string> { NoRepoOption };
        repoOptions.AddRange(_repos.Select(r => r.Name));
        NewRepoBox.ItemsSource = repoOptions;

        RenderBoard();
    }

    private void RenderBoard()
    {
        BoardPanel.Children.Clear();

        // A Panel with no Background is click/drop-through outside its children's own bounds -
        // Transparent (not null) makes the whole column hit-testable, and the MinHeight stretches
        // that hit-test area down to the bottom of the visible board, so dropping on the empty
        // space below the last card (or in a column with no cards at all) works exactly like
        // dropping on a card or the column header.
        var columnMinHeight = Math.Max(BoardScrollViewer.ActualHeight - 8, 200);

        foreach (var status in WorkItemCardControl.Columns)
        {
            var column = new StackPanel { Spacing = 8 };

            var itemsInColumn = _items.Where(i => i.Status == status).OrderByDescending(i => i.Priority).ToList();

            column.Children.Add(new TextBlock
            {
                Text = $"{WorkItemCardControl.ColumnTitle(status).ToUpperInvariant()} ({itemsInColumn.Count})",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Margin = new Thickness(4, 0, 0, 4)
            });

            foreach (var item in itemsInColumn)
            {
                var card = new WorkItemCardControl { CanDrag = true };
                card.Populate(item, _commentCounts.GetValueOrDefault(item.Id));
                card.StatusChanged += async (i, s) => await MoveToAsync(i, s);
                card.DeleteRequested += async i => await DeleteAsync(i);
                card.EditRequested += OnEditRequested;
                card.DragStarting += (_, e) => e.Data.SetText(item.Id.ToString());
                column.Children.Add(card);
            }

            // The Border (not the inner StackPanel) is the actual drop target - wrapping it this
            // way gives a place to toggle a visible highlight while a card is dragged over the
            // column, on top of the same "fill the whole viewport height, Background must be
            // non-null to be hit-testable outside its children's own bounds" treatment as before.
            var columnBorder = new Border
            {
                Width = 260,
                MinHeight = columnMinHeight,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                AllowDrop = true,
                Child = column
            };
            columnBorder.DragOver += OnColumnDragOver;
            columnBorder.DragEnter += (_, _) => HighlightColumn(columnBorder);
            columnBorder.DragLeave += (_, _) => ResetColumnHighlight(columnBorder);
            columnBorder.Drop += async (_, e) =>
            {
                ResetColumnHighlight(columnBorder);
                await OnColumnDropAsync(e, status);
            };

            BoardPanel.Children.Add(columnBorder);
        }
    }

    private static void OnColumnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Move;

    private static void HighlightColumn(Border border)
    {
        border.Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) { Opacity = 0.12 };
        border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) { Opacity = 0.5 };
    }

    private static void ResetColumnHighlight(Border border)
    {
        border.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>Auto-scrolls the board horizontally while dragging a card near the left/right
    /// edge of the visible area, so reaching a column that's currently off-screen doesn't require
    /// dropping, manually scrolling, and starting the drag over again. DragOver bubbles up from
    /// whichever column the pointer is actually over, so this fires regardless of which column's
    /// own DragOver/Drop handler also runs for the same pointer move.</summary>
    private void OnBoardScrollViewerDragOver(object sender, DragEventArgs e)
    {
        const double edgeZone = 60;
        const double scrollStep = 24;

        var position = e.GetPosition(BoardScrollViewer);

        double? newHorizontalOffset = position.X switch
        {
            var x when x < edgeZone => Math.Max(0, BoardScrollViewer.HorizontalOffset - scrollStep),
            var x when x > BoardScrollViewer.ActualWidth - edgeZone => BoardScrollViewer.HorizontalOffset + scrollStep,
            _ => null
        };

        double? newVerticalOffset = position.Y switch
        {
            var y when y < edgeZone => Math.Max(0, BoardScrollViewer.VerticalOffset - scrollStep),
            var y when y > BoardScrollViewer.ActualHeight - edgeZone => BoardScrollViewer.VerticalOffset + scrollStep,
            _ => null
        };

        if (newHorizontalOffset is not null || newVerticalOffset is not null)
        {
            BoardScrollViewer.ChangeView(newHorizontalOffset, newVerticalOffset, null, disableAnimation: true);
        }
    }

    /// <summary>Kanban drag-and-drop - dropping a card on another column's StackPanel moves it to
    /// that status, the same transition the card's own status-change control already triggers.</summary>
    private async Task OnColumnDropAsync(DragEventArgs e, WorkItemStatus status)
    {
        if (!e.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var idText = await e.DataView.GetTextAsync();
        if (!Guid.TryParse(idText, out var id))
        {
            return;
        }

        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is not null && item.Status != status)
        {
            await MoveToAsync(item, status);
        }
    }

    private async void OnEditRequested(WorkItemDto item)
    {
        var dialog = new WorkItemDetailDialog(item, _repos, _triageAppService) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await RefreshAsync();
    }

    /// <summary>Opens the detail dialog for a specific Work Item - used for the "task updated"
    /// notification deep-link. Silent no-op if the item was deleted before this fires.</summary>
    public void ShowDetailFor(Guid workItemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == workItemId);
        if (item is not null)
        {
            OnEditRequested(item);
        }
    }

    private async Task MoveToAsync(WorkItemDto item, WorkItemStatus status)
    {
        await _triageAppService.UpdateWorkItemStatusAsync(item.Id, status);
        await RefreshAsync();
    }

    private async Task DeleteAsync(WorkItemDto item)
    {
        await _triageAppService.DeleteWorkItemAsync(item.Id);
        await RefreshAsync();
    }

    private void OnToggleAddForm(object sender, RoutedEventArgs e)
    {
        var showing = AddForm.Visibility == Visibility.Visible;
        AddForm.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
        AddButton.Content = showing ? "+ Add task" : "Cancel";
    }

    private async void OnCreateTask(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewTitleBox.Text))
        {
            return;
        }

        var repoName = NewRepoBox.SelectedItem as string;
        var request = new UpsertWorkItemRequest(
            NewTitleBox.Text,
            string.IsNullOrWhiteSpace(NewDescriptionBox.Text) ? null : NewDescriptionBox.Text,
            NewPriorityBox.SelectedItem as WorkItemPriority? ?? WorkItemPriority.Medium,
            NewTypeBox.SelectedItem as WorkItemType? ?? WorkItemType.Task,
            repoName is null or NoRepoOption ? null : repoName,
            null,
            NewDueDatePicker.Date);

        await _triageAppService.CreateWorkItemAsync(request);

        NewTitleBox.Text = "";
        NewDescriptionBox.Text = "";
        NewRepoBox.SelectedItem = NoRepoOption;
        NewDueDatePicker.Date = null;
        AddForm.Visibility = Visibility.Collapsed;
        AddButton.Content = "+ Add task";

        await RefreshAsync();
    }
}
