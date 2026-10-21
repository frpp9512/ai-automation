using System.Text.Json;
using AiAutomations.Core.Models;
using AiAutomations.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AiAutomations.Desktop.Views.Controls;

/// <summary>
/// Full-context read view for a single action, opened either from an Inbox card's "Details"
/// link or by clicking a toast notification. Never shows a raw Payload string - always the
/// parsed, human-readable text for whatever ActionType it is.
/// </summary>
public sealed partial class ActionDetailDialog : ContentDialog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string? _messagePermalink;

    public ActionDetailDialog(ActionItemDto action, MessageDecisionDto? decision, SlackMessageDto? message)
    {
        InitializeComponent();

        TypeText.Text = action.Type.ToString();
        UrgencyText.Text = decision?.Urgency.ToString() ?? string.Empty;
        ConfidenceText.Text = decision is not null ? $"{decision.Confidence:P0} confidence" : string.Empty;
        StatusText.Text = action.Status.ToString();

        SummaryText.Text = decision?.Summary ?? "(no summary)";
        TimestampText.Text = $"Received {action.CreatedAtUtc.ToLocalTime():g}" +
            (action.ResolvedAtUtc is { } resolved ? $" - resolved {resolved.ToLocalTime():g}" : string.Empty);

        FailureBar.IsOpen = action.Status == ActionStatus.Failed;
        FailureBar.Message = action.FailureReason ?? "No further details were recorded.";

        if (message is not null)
        {
            SourceText.Text = $"ORIGINAL MESSAGE - {(message.IsDirectMessage ? "DM" : "mention")} from {message.DisplayName}";
            MessageText.Text = message.Text;
            MessageSection.Visibility = Visibility.Visible;

            _messagePermalink = message.Permalink;
            ViewInSlackLink.Visibility = string.IsNullOrWhiteSpace(_messagePermalink) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            MessageSection.Visibility = Visibility.Collapsed;
        }

        PopulateDetailsAndRepo(action, decision);
    }

    private async void OnViewInSlackClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_messagePermalink) && Uri.TryCreate(_messagePermalink, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private void PopulateDetailsAndRepo(ActionItemDto action, MessageDecisionDto? decision)
    {
        string? repoName = null;

        switch (action.Type)
        {
            case ActionType.Reply:
                DetailsHeader.Text = "REPLY TEXT";
                MarkdownRenderer.RenderInto(DetailsText, action.Payload ?? "(no reply drafted)");
                break;

            case ActionType.Ticket:
                DetailsHeader.Text = "TASK DETAILS";
                var ticket = string.IsNullOrWhiteSpace(action.Payload) ? null : TryDeserialize<TicketPayload>(action.Payload);
                MarkdownRenderer.RenderInto(DetailsText, decision?.TaskDetails ?? ticket?.Title ?? decision?.Summary ?? string.Empty);
                repoName = ticket?.RepositoryName;
                break;

            case ActionType.TaskBrief:
                DetailsHeader.Text = "BRIEF";
                var brief = string.IsNullOrWhiteSpace(action.Payload) ? null : TryDeserialize<TaskBriefPayload>(action.Payload);
                MarkdownRenderer.RenderInto(DetailsText, brief?.Brief ?? "(no brief generated)");
                repoName = brief?.RepoName;
                break;

            default:
                DetailsSection.Visibility = Visibility.Collapsed;
                break;
        }

        if (!string.IsNullOrWhiteSpace(repoName))
        {
            RepoText.Text = repoName;
            RepoSection.Visibility = Visibility.Visible;
        }
        else
        {
            RepoSection.Visibility = Visibility.Collapsed;
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
}
