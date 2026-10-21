using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>
/// Push seam from the core pipeline to connected clients.
/// Implemented in the Server project on top of a SignalR hub, so Core/Infrastructure
/// stay free of ASP.NET Core dependencies.
/// </summary>
public interface IRealtimeNotifier
{
    Task MessageReceivedAsync(SlackMessage message);
    Task DecisionReadyAsync(MessageDecision decision);
    Task ActionStatusChangedAsync(ActionItem action);
    Task SessionStatusChangedAsync(ClaudeSession session);
    Task WorkItemChangedAsync(WorkItem workItem);

    /// <summary>Fired specifically for an automated comment (a later Slack message in the same
    /// thread linked to an already-tracked Work Item) - distinct from WorkItemChangedAsync so a
    /// UI can choose to surface a "task updated" notification only for this, not for every
    /// manual edit too.</summary>
    Task WorkItemCommentAddedAsync(WorkItem workItem, WorkItemComment comment);
}
