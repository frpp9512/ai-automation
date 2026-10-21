using AiAutomations.Core.Models;
using AiAutomations.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AiAutomations.Desktop.Views.Controls;

public sealed partial class WorkItemCardControl : UserControl
{
    public static readonly WorkItemStatus[] Columns =
    [
        WorkItemStatus.Todo, WorkItemStatus.InProgress, WorkItemStatus.InReview,
        WorkItemStatus.Done, WorkItemStatus.Cancelled
    ];

    private record StatusOption(WorkItemStatus Status, string Label);

    private static readonly List<StatusOption> StatusOptions = Columns.Select(s => new StatusOption(s, ColumnTitle(s))).ToList();

    private WorkItemDto _item = null!;
    private bool _suppressStatusEvent;

    public event Func<WorkItemDto, WorkItemStatus, Task>? StatusChanged;
    public event Func<WorkItemDto, Task>? DeleteRequested;
    public event Action<WorkItemDto>? EditRequested;

    public WorkItemCardControl()
    {
        InitializeComponent();
    }

    public static string ColumnTitle(WorkItemStatus status) => status switch
    {
        WorkItemStatus.InProgress => "In progress",
        WorkItemStatus.InReview => "In review",
        _ => status.ToString()
    };

    public void Populate(WorkItemDto item, int commentCount = 0)
    {
        _item = item;

        AccentBar.Background = new SolidColorBrush(item.Priority switch
        {
            WorkItemPriority.Critical or WorkItemPriority.High => Colors.IndianRed,
            WorkItemPriority.Medium => Colors.Orange,
            _ => Colors.Gray
        });

        TypeBadge.Text = item.Type.ToString();
        TitleText.Text = item.Title;

        // Compact card preview - stripped of markdown syntax rather than fully rendered, to
        // keep board cards light (a full RichTextBlock per card isn't worth it here).
        DescriptionText.Text = MarkdownRenderer.StripSyntax(item.Description, maxLength: 160);
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(item.Description) ? Visibility.Collapsed : Visibility.Visible;

        ProgressBar.Value = item.PercentComplete;
        ProgressLabel.Text = $"{item.PercentComplete}% complete";
        ProgressRow.Visibility = item.PercentComplete > 0 ? Visibility.Visible : Visibility.Collapsed;

        RepoText.Text = item.RepositoryName ?? string.Empty;
        RepoText.Visibility = string.IsNullOrWhiteSpace(item.RepositoryName) ? Visibility.Collapsed : Visibility.Visible;

        DueText.Text = item.DueAtUtc is { } due ? $"Due {due.ToLocalTime():MMM d}" : string.Empty;
        DueText.Visibility = item.DueAtUtc is null ? Visibility.Collapsed : Visibility.Visible;

        CommentCountText.Text = commentCount > 0 ? $"{commentCount} comment{(commentCount == 1 ? "" : "s")}" : string.Empty;
        CommentCountText.Visibility = commentCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        MetaRow.Visibility = RepoText.Visibility == Visibility.Visible || DueText.Visibility == Visibility.Visible || CommentCountText.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        _suppressStatusEvent = true;
        StatusPicker.ItemsSource = StatusOptions;
        StatusPicker.SelectedItem = StatusOptions.First(o => o.Status == item.Status);
        _suppressStatusEvent = false;
    }

    private async void OnStatusChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStatusEvent || StatusChanged is null)
        {
            return;
        }

        if (StatusPicker.SelectedItem is StatusOption { } option && option.Status != _item.Status)
        {
            await StatusChanged.Invoke(_item, option.Status);
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (DeleteRequested is not null)
        {
            await DeleteRequested.Invoke(_item);
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e) => EditRequested?.Invoke(_item);
}
