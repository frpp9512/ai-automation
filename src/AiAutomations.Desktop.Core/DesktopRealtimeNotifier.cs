using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Shared;
using AiAutomations.Shared.Mapping;

namespace AiAutomations.Desktop.Core;

/// <summary>
/// In-process stand-in for the server's SignalR-based notifier - raises plain C# events
/// with the same DTOs instead of pushing over a hub. No UI/dispatcher dependency here on
/// purpose, so it stays unit-testable; the WinUI3 shell marshals these onto the UI thread.
/// </summary>
public class DesktopRealtimeNotifier : IRealtimeNotifier
{
    public event Action<SlackMessageDto>? MessageReceived;
    public event Action<MessageDecisionDto>? DecisionReady;
    public event Action<ActionItemDto>? ActionStatusChanged;
    public event Action<ClaudeSessionDto>? SessionStatusChanged;
    public event Action<WorkItemDto>? WorkItemChanged;
    public event Action<WorkItemDto, WorkItemCommentDto>? WorkItemCommentAdded;

    public Task MessageReceivedAsync(SlackMessage message)
    {
        MessageReceived?.Invoke(message.ToDto());
        return Task.CompletedTask;
    }

    public Task DecisionReadyAsync(MessageDecision decision)
    {
        DecisionReady?.Invoke(decision.ToDto());
        return Task.CompletedTask;
    }

    public Task ActionStatusChangedAsync(ActionItem action)
    {
        ActionStatusChanged?.Invoke(action.ToDto());
        return Task.CompletedTask;
    }

    public Task SessionStatusChangedAsync(ClaudeSession session)
    {
        SessionStatusChanged?.Invoke(session.ToDto());
        return Task.CompletedTask;
    }

    public Task WorkItemChangedAsync(WorkItem workItem)
    {
        WorkItemChanged?.Invoke(workItem.ToDto());
        return Task.CompletedTask;
    }

    public Task WorkItemCommentAddedAsync(WorkItem workItem, WorkItemComment comment)
    {
        WorkItemCommentAdded?.Invoke(workItem.ToDto(), comment.ToDto());
        return Task.CompletedTask;
    }
}
