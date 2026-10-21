using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;

namespace AiAutomations.Tests.Fakes;

public class FakeRealtimeNotifier : IRealtimeNotifier
{
    public List<ActionItem> ActionStatusChanges { get; } = [];
    public List<ClaudeSession> SessionStatusChanges { get; } = [];
    public List<WorkItem> WorkItemChanges { get; } = [];
    public List<(WorkItem WorkItem, WorkItemComment Comment)> WorkItemCommentsAdded { get; } = [];
    public List<OpenProjectSyncRun> OpenProjectSyncRuns { get; } = [];

    public Task MessageReceivedAsync(SlackMessage message) => Task.CompletedTask;

    public Task DecisionReadyAsync(MessageDecision decision) => Task.CompletedTask;

    public Task ActionStatusChangedAsync(ActionItem action)
    {
        ActionStatusChanges.Add(action);
        return Task.CompletedTask;
    }

    public Task SessionStatusChangedAsync(ClaudeSession session)
    {
        SessionStatusChanges.Add(session);
        return Task.CompletedTask;
    }

    public Task WorkItemChangedAsync(WorkItem workItem)
    {
        WorkItemChanges.Add(workItem);
        return Task.CompletedTask;
    }

    public Task WorkItemCommentAddedAsync(WorkItem workItem, WorkItemComment comment)
    {
        WorkItemCommentsAdded.Add((workItem, comment));
        return Task.CompletedTask;
    }

    public Task OpenProjectSyncCompletedAsync(OpenProjectSyncRun run)
    {
        OpenProjectSyncRuns.Add(run);
        return Task.CompletedTask;
    }
}
