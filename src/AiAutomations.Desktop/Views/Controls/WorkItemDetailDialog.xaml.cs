using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace AiAutomations.Desktop.Views.Controls;

public sealed partial class WorkItemDetailDialog : ContentDialog
{
    private const string NoRepoOption = "(none)";

    private readonly TriageAppService _triageAppService;
    private readonly WorkItemDto _item;
    private readonly List<RepositoryProfileDto> _repos;
    private string? _sourceMessagePermalink;

    public WorkItemDetailDialog(WorkItemDto item, IReadOnlyList<RepositoryProfileDto> repos, TriageAppService triageAppService)
    {
        InitializeComponent();
        _item = item;
        _repos = repos.ToList();
        _triageAppService = triageAppService;

        TitleBox.Text = item.Title;
        DescriptionBox.Text = item.Description ?? string.Empty;

        PriorityBox.ItemsSource = Enum.GetValues<WorkItemPriority>();
        PriorityBox.SelectedItem = item.Priority;
        TypeBox.ItemsSource = Enum.GetValues<WorkItemType>();
        TypeBox.SelectedItem = item.Type;

        var repoOptions = new List<string> { NoRepoOption };
        repoOptions.AddRange(_repos.Select(r => r.Name));
        RepoBox.ItemsSource = repoOptions;
        RepoBox.SelectedItem = item.RepositoryName ?? NoRepoOption;

        DueDatePicker.Date = item.DueAtUtc;

        PercentSlider.Value = item.PercentComplete;
        PercentLabel.Text = $"{item.PercentComplete}%";

        Loaded += async (_, _) =>
        {
            await LoadCommentsAsync();
            await LoadSourceMessageLinkAsync();
        };
    }

    private async Task LoadSourceMessageLinkAsync()
    {
        if (_item.SourceDecisionId is not { } decisionId)
        {
            return;
        }

        var decision = await _triageAppService.GetDecisionAsync(decisionId);
        if (decision is null)
        {
            return;
        }

        var message = await _triageAppService.GetMessageAsync(decision.MessageId);
        if (string.IsNullOrWhiteSpace(message?.Permalink))
        {
            return;
        }

        _sourceMessagePermalink = message.Permalink;
        ViewInSlackLink.Visibility = Visibility.Visible;
    }

    private async void OnViewInSlack(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_sourceMessagePermalink) && Uri.TryCreate(_sourceMessagePermalink, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void OnPercentChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        PercentLabel.Text = $"{(int)e.NewValue}%";

    private void OnTogglePreview(object sender, RoutedEventArgs e)
    {
        var showingPreview = PreviewToggle.IsChecked == true;

        if (showingPreview)
        {
            MarkdownRenderer.RenderInto(DescriptionPreview, DescriptionBox.Text);
        }

        DescriptionBox.Visibility = showingPreview ? Visibility.Collapsed : Visibility.Visible;
        DescriptionPreviewBorder.Visibility = showingPreview ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadCommentsAsync()
    {
        var comments = await _triageAppService.GetCommentsAsync(_item.Id);
        CommentsPanel.Children.Clear();

        if (comments.Count == 0)
        {
            CommentsPanel.Children.Add(new TextBlock { Text = "No comments yet.", Foreground = new SolidColorBrush(Colors.Gray), FontSize = 12 });
            return;
        }

        foreach (var comment in comments)
        {
            CommentsPanel.Children.Add(BuildCommentRow(comment));
        }
    }

    private static FrameworkElement BuildCommentRow(WorkItemCommentDto comment)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{comment.CreatedAtUtc.ToLocalTime():g}{(comment.IsAutomated ? " - auto-linked from Slack" : string.Empty)}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Gray)
        });
        panel.Children.Add(new TextBlock { Text = comment.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13 });

        return new Border
        {
            Padding = new Thickness(0, 0, 0, 6),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Colors.Gray) { Opacity = 0.15 },
            Child = panel
        };
    }

    private async void OnAddComment(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewCommentBox.Text))
        {
            return;
        }

        await _triageAppService.AddCommentAsync(_item.Id, NewCommentBox.Text);
        NewCommentBox.Text = string.Empty;
        await LoadCommentsAsync();
    }

    private async void OnRefineComment(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewCommentBox.Text))
        {
            return;
        }

        RefineCommentButton.IsEnabled = false;
        RefineCommentSpinner.IsActive = true;
        RefineCommentSpinner.Visibility = Visibility.Visible;
        RefineCommentButtonText.Text = "Refining...";
        NewCommentBox.IsEnabled = false;
        AddCommentButton.IsEnabled = false;

        try
        {
            RefineCommentErrorBar.IsOpen = false;
            NewCommentBox.Text = await _triageAppService.RefineCommentAsync(_item.Id, NewCommentBox.Text);
        }
        catch (Exception ex)
        {
            RefineCommentErrorBar.Message = ex.Message;
            RefineCommentErrorBar.IsOpen = true;
        }
        finally
        {
            RefineCommentButton.IsEnabled = true;
            RefineCommentSpinner.IsActive = false;
            RefineCommentSpinner.Visibility = Visibility.Collapsed;
            RefineCommentButtonText.Text = "Refine with AI";
            NewCommentBox.IsEnabled = true;
            AddCommentButton.IsEnabled = true;
        }
    }

    private async void OnImproveWithAi(object sender, RoutedEventArgs e)
    {
        ImproveButton.IsEnabled = false;
        ImproveSpinner.IsActive = true;
        ImproveSpinner.Visibility = Visibility.Visible;
        ImproveButtonText.Text = "Improving...";

        // Lock the field being refined (and the button that would show a stale version of it)
        // so the user can't type into it, or re-trigger the refinement, while it's in flight -
        // otherwise their edits could get silently clobbered when the AI response lands.
        DescriptionBox.IsEnabled = false;
        PreviewToggle.IsEnabled = false;

        try
        {
            ImproveErrorBar.IsOpen = false;
            DescriptionBox.Text = await _triageAppService.ImproveWorkItemDescriptionAsync(_item.Id);

            if (PreviewToggle.IsChecked == true)
            {
                MarkdownRenderer.RenderInto(DescriptionPreview, DescriptionBox.Text);
            }
        }
        catch (Exception ex)
        {
            // A second ContentDialog can't be shown while this one (WorkItemDetailDialog
            // itself) is still open - WinUI3 allows only one at a time - so the error goes
            // inline instead, same as every other error surface in this app.
            ImproveErrorBar.Message = ex.Message;
            ImproveErrorBar.IsOpen = true;
        }
        finally
        {
            ImproveButton.IsEnabled = true;
            ImproveSpinner.IsActive = false;
            ImproveSpinner.Visibility = Visibility.Collapsed;
            ImproveButtonText.Text = "Improve with AI";
            DescriptionBox.IsEnabled = true;
            PreviewToggle.IsEnabled = true;
        }
    }

    /// <summary>Copies the task (as currently edited on screen, not just the last-saved state)
    /// as a clean markdown block a PM can paste straight into Jira/Trello/whatever they use.</summary>
    private async void OnExportForPm(object sender, RoutedEventArgs e)
    {
        var repoName = RepoBox.SelectedItem as string;
        var snapshot = _item with
        {
            Title = TitleBox.Text,
            Description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text,
            Priority = PriorityBox.SelectedItem as WorkItemPriority? ?? _item.Priority,
            Type = TypeBox.SelectedItem as WorkItemType? ?? _item.Type,
            RepositoryName = repoName is null or NoRepoOption ? null : repoName,
            DueAtUtc = DueDatePicker.Date,
            PercentComplete = (int)PercentSlider.Value
        };

        var comments = await _triageAppService.GetCommentsAsync(_item.Id);
        var text = WorkItemExportFormatter.Format(snapshot, comments);

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        var originalContent = ExportButton.Content;
        ExportButton.Content = "Copied!";
        await Task.Delay(1500);
        ExportButton.Content = originalContent;
    }

    private async void OnGenerateImplementationPrompt(object sender, RoutedEventArgs e)
    {
        ImplementationPromptButton.IsEnabled = false;
        ImplementationPromptSpinner.IsActive = true;
        ImplementationPromptSpinner.Visibility = Visibility.Visible;
        ImplementationPromptButtonText.Text = "Generating...";

        try
        {
            ImplementationPromptErrorBar.IsOpen = false;
            var prompt = await _triageAppService.GenerateImplementationPromptAsync(_item.Id);

            var package = new DataPackage();
            package.SetText(prompt);
            Clipboard.SetContent(package);

            ImplementationPromptButtonText.Text = "Copied!";
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            ImplementationPromptErrorBar.Message = ex.Message;
            ImplementationPromptErrorBar.IsOpen = true;
        }
        finally
        {
            ImplementationPromptButton.IsEnabled = true;
            ImplementationPromptSpinner.IsActive = false;
            ImplementationPromptSpinner.Visibility = Visibility.Collapsed;
            ImplementationPromptButtonText.Text = "Copy implementation prompt";
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                args.Cancel = true;
                return;
            }

            var repoName = RepoBox.SelectedItem as string;
            var request = new UpdateWorkItemRequest(
                TitleBox.Text,
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text,
                PriorityBox.SelectedItem as WorkItemPriority? ?? _item.Priority,
                TypeBox.SelectedItem as WorkItemType? ?? _item.Type,
                repoName is null or NoRepoOption ? null : repoName,
                _item.Labels,
                DueDatePicker.Date,
                (int)PercentSlider.Value);

            await _triageAppService.UpdateWorkItemAsync(_item.Id, request);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
