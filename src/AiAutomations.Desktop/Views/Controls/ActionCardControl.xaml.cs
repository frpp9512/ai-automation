using System.Text.Json;
using AiAutomations.Core.Models;
using AiAutomations.Desktop.Core;
using AiAutomations.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace AiAutomations.Desktop.Views.Controls;

public sealed partial class ActionCardControl : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Explicit "no repository applies" choice - distinct from "never picked one yet",
    /// so Approve can require an active decision rather than silently defaulting to nothing.</summary>
    private const string NoneRepoOption = "(none - not repo specific)";

    private readonly TriageAppService _triageAppService;

    private ActionItemDto _action = null!;
    private MessageDecisionDto? _decision;
    private SlackMessageDto? _message;
    private IReadOnlyList<RepositoryProfileDto> _repositories = [];
    private TaskBriefPayload? _brief;

    /// <summary>Fired with the (possibly edited) payload text when Approve is clicked.</summary>
    public event Func<ActionItemDto, string?, Task>? Approved;

    public event Func<ActionItemDto, Task>? Rejected;

    public ActionCardControl()
    {
        InitializeComponent();
        _triageAppService = App.Host.Services.GetRequiredService<TriageAppService>();
    }

    public void Populate(ActionItemDto action, MessageDecisionDto? decision, SlackMessageDto? message, IReadOnlyList<RepositoryProfileDto> repositories)
    {
        _action = action;
        _decision = decision;
        _message = message;
        _repositories = repositories;
        _brief = null;

        AccentBar.Background = new SolidColorBrush(decision?.Urgency switch
        {
            Urgency.High => Colors.IndianRed,
            Urgency.Medium => Colors.Orange,
            _ => Colors.Gray
        });

        TypeBadge.Text = action.Type.ToString();
        UrgencyBadge.Text = decision?.Urgency.ToString() ?? string.Empty;

        // For a Reply action, Approve already sends the reply - a separate "Reply" (decline-
        // with-feedback) button next to it would be confusingly redundant.
        ReplyButton.Visibility = action.Type == ActionType.Reply ? Visibility.Collapsed : Visibility.Visible;

        FailureBar.IsOpen = action.Status == ActionStatus.Failed;
        FailureBar.Message = action.FailureReason ?? "No further details were recorded.";

        ConfidenceBadge.Text = decision is null
            ? string.Empty
            : decision.Confidence < 0.5
                ? $"{decision.Confidence:P0} confidence - Needs review"
                : $"{decision.Confidence:P0} confidence";

        SummaryText.Text = decision?.Summary ?? "(no summary)";
        SourceText.Text = message is null
            ? string.Empty
            : $"From {message.DisplayName} - {(message.IsDirectMessage ? "DM" : "mention")}";
        ViewInSlackLink.Visibility = string.IsNullOrWhiteSpace(message?.Permalink) ? Visibility.Collapsed : Visibility.Visible;

        RepoPicker.Visibility = Visibility.Collapsed;
        NoBriefHint.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
        PayloadBox.Visibility = Visibility.Collapsed;
        PayloadBox.IsReadOnly = false;
        ApproveButton.IsEnabled = true;

        switch (action.Type)
        {
            case ActionType.Reply:
                PayloadBox.Visibility = Visibility.Visible;
                PayloadBox.Text = action.Payload ?? decision?.SuggestedReply ?? string.Empty;
                break;

            case ActionType.TaskBrief:
                if (!string.IsNullOrWhiteSpace(action.Payload))
                {
                    _brief = TryDeserialize<TaskBriefPayload>(action.Payload);
                }

                NoBriefHint.Visibility = string.IsNullOrWhiteSpace(_brief?.Brief) ? Visibility.Visible : Visibility.Collapsed;
                CopyButton.Visibility = Visibility.Visible;
                PayloadBox.Visibility = Visibility.Visible;
                PayloadBox.Text = _brief?.Brief ?? string.Empty;
                PopulateRepoPicker(_brief?.RepoName);
                break;

            case ActionType.Ticket:
            default:
                var ticket = string.IsNullOrWhiteSpace(action.Payload) ? null : TryDeserialize<TicketPayload>(action.Payload);
                PayloadBox.Visibility = Visibility.Visible;
                PayloadBox.Text = ticket?.Title ?? decision?.Summary ?? string.Empty;
                PopulateRepoPicker(ticket?.RepositoryName);
                break;
        }
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Pre-selects the matched repository when the AI found one - approval is immediately
    /// allowed in that "totally clear" case. When it didn't, nothing is pre-selected and
    /// Approve stays disabled until the user actively picks a repo or "(none)".
    /// </summary>
    private void PopulateRepoPicker(string? suggestedRepoName)
    {
        RepoPicker.Visibility = Visibility.Visible;

        var items = new List<string> { NoneRepoOption };
        items.AddRange(_repositories.Select(r => r.Name));
        RepoPicker.ItemsSource = items;

        var matched = _repositories.FirstOrDefault(r => r.Name == suggestedRepoName);
        RepoPicker.SelectedItem = matched?.Name; // null -> nothing selected, forces a deliberate choice

        UpdateApproveEnabled();
    }

    private void OnRepoSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateApproveEnabled();

    private void UpdateApproveEnabled()
    {
        if (_action.Type is not (ActionType.Ticket or ActionType.TaskBrief))
        {
            ApproveButton.IsEnabled = true;
            return;
        }

        // Nothing to choose from - don't block on a decision the user has no way to make.
        ApproveButton.IsEnabled = _repositories.Count == 0 || RepoPicker.SelectedItem is not null;
    }

    private string? SelectedRepoName() =>
        RepoPicker.SelectedItem as string is { } selected && selected != NoneRepoOption ? selected : null;

    private string? BuildEditedPayload() => BuildPayload(PayloadBox.Text, SelectedRepoName());

    private string? BuildPayload(string payloadText, string? repoName)
    {
        if (_action.Type == ActionType.TaskBrief)
        {
            var repo = _repositories.FirstOrDefault(r => r.Name == repoName);
            var updated = (_brief ?? new TaskBriefPayload(null, null, "", "")) with
            {
                RepoName = repo?.Name,
                RepoPath = repo?.Path,
                Prompt = payloadText,
                Brief = payloadText
            };
            return JsonSerializer.Serialize(updated);
        }

        if (_action.Type == ActionType.Ticket)
        {
            return JsonSerializer.Serialize(new TicketPayload(payloadText, repoName));
        }

        return payloadText;
    }

    private async void OnApprove(SplitButton sender, SplitButtonClickEventArgs e)
    {
        if (Approved is not null)
        {
            await Approved.Invoke(_action, BuildEditedPayload());
            await OfferAcknowledgmentAsync();
        }
    }

    /// <summary>"Refine and approve" - asks the AI to polish the current draft (grounded in the
    /// selected repository's context for Ticket/TaskBrief actions) and approves with the refined
    /// text immediately, without a manual review step.</summary>
    private async void OnRefineAndApprove(object sender, RoutedEventArgs e)
    {
        ApproveButton.IsEnabled = false;
        RejectButton.IsEnabled = false;
        PayloadBox.IsEnabled = false;
        RepoPicker.IsEnabled = false;

        try
        {
            RefineErrorBar.IsOpen = false;
            PayloadBox.Text = await _triageAppService.RefineActionDetailsAsync(_action.Id, PayloadBox.Text, SelectedRepoName());

            if (Approved is not null)
            {
                await Approved.Invoke(_action, BuildEditedPayload());
                await OfferAcknowledgmentAsync();
            }
        }
        catch (Exception ex)
        {
            RefineErrorBar.Message = ex.Message;
            RefineErrorBar.IsOpen = true;
        }
        finally
        {
            ApproveButton.IsEnabled = true;
            RejectButton.IsEnabled = true;
            PayloadBox.IsEnabled = true;
            RepoPicker.IsEnabled = true;
        }
    }

    /// <summary>"Check first" - opens a pre-filled review dialog so the details can be manually
    /// checked (and further edited) before deciding to approve or discard.</summary>
    private async void OnCheckFirst(object sender, RoutedEventArgs e)
    {
        var dialog = new ActionReviewDialog(_action, _decision, PayloadBox.Text, SelectedRepoName(), _repositories) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (Approved is not null)
            {
                await Approved.Invoke(_action, BuildPayload(dialog.PayloadText, dialog.SelectedRepoName));
                await OfferAcknowledgmentAsync();
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            if (Rejected is not null)
            {
                await Rejected.Invoke(_action);
            }
        }
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if (Rejected is not null)
        {
            await Rejected.Invoke(_action);
        }
    }

    /// <summary>"Reply" - declines the item but, unlike a plain Reject, also gives feedback: an
    /// AI-drafted explanation the user can edit, shown for explicit approval before anything is
    /// sent or declined. Cancelling the dialog leaves the item untouched.</summary>
    private async void OnReplyAndDecline(object sender, RoutedEventArgs e)
    {
        ReplyButton.IsEnabled = false;
        RejectButton.IsEnabled = false;
        ApproveButton.IsEnabled = false;

        string? draft;
        try
        {
            ReplyErrorBar.IsOpen = false;
            draft = await _triageAppService.GenerateDeclineFeedbackAsync(_action.Id);
        }
        catch (Exception ex)
        {
            ReplyErrorBar.Message = ex.Message;
            ReplyErrorBar.IsOpen = true;
            draft = null;
        }
        finally
        {
            ReplyButton.IsEnabled = true;
            RejectButton.IsEnabled = true;
            UpdateApproveEnabled();
        }

        if (string.IsNullOrWhiteSpace(draft))
        {
            if (draft is null && !ReplyErrorBar.IsOpen)
            {
                ReplyErrorBar.Message = "No source message was found to reply to.";
                ReplyErrorBar.IsOpen = true;
            }
            return;
        }

        var dialog = new DeclineFeedbackDialog(draft) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(dialog.MessageText))
        {
            return;
        }

        try
        {
            await _triageAppService.SendAcknowledgmentAsync(_action.Id, dialog.MessageText);
        }
        catch (Exception ex)
        {
            ReplyErrorBar.Message = ex.Message;
            ReplyErrorBar.IsOpen = true;
            return;
        }

        if (Rejected is not null)
        {
            await Rejected.Invoke(_action);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(PayloadBox.Text);
        Clipboard.SetContent(package);
    }

    private async void OnDetails(object sender, RoutedEventArgs e)
    {
        var dialog = new ActionDetailDialog(_action, _decision, _message) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnViewInSlack(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_message?.Permalink) && Uri.TryCreate(_message.Permalink, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    /// <summary>
    /// After an approval fully completes, offers (never auto-sends) a drafted acknowledgment
    /// reply on the originating Slack thread - this is a best-effort nicety, so any failure here
    /// is swallowed rather than surfaced, since it must never block or alarm on top of an
    /// approval that already succeeded.
    /// </summary>
    private async Task OfferAcknowledgmentAsync()
    {
        if (_message is null)
        {
            return;
        }

        string? draft;
        try
        {
            draft = await _triageAppService.GenerateAcknowledgmentAsync(_action.Id);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(draft))
        {
            return;
        }

        var dialog = new AcknowledgmentDialog(draft) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.MessageText))
        {
            try
            {
                await _triageAppService.SendAcknowledgmentAsync(_action.Id, dialog.MessageText);
            }
            catch
            {
                // Best-effort secondary feature - the approval itself already succeeded.
            }
        }
    }
}
