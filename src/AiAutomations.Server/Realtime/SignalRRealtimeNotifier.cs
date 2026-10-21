using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Server.Hubs;
using AiAutomations.Shared;
using AiAutomations.Shared.Mapping;
using Microsoft.AspNetCore.SignalR;

namespace AiAutomations.Server.Realtime;

public class SignalRRealtimeNotifier(IHubContext<InboxHub> hub) : IRealtimeNotifier
{
    public Task MessageReceivedAsync(SlackMessage message) =>
        hub.Clients.All.SendAsync(HubEvents.MessageReceived, message.ToDto());

    public Task DecisionReadyAsync(MessageDecision decision) =>
        hub.Clients.All.SendAsync(HubEvents.DecisionReady, decision.ToDto());

    public Task ActionStatusChangedAsync(ActionItem action) =>
        hub.Clients.All.SendAsync(HubEvents.ActionStatusChanged, action.ToDto());

    public Task SessionStatusChangedAsync(ClaudeSession session) =>
        hub.Clients.All.SendAsync(HubEvents.SessionStatusChanged, session.ToDto());

    public Task WorkItemChangedAsync(WorkItem workItem) =>
        hub.Clients.All.SendAsync(HubEvents.WorkItemChanged, workItem.ToDto());

    public Task WorkItemCommentAddedAsync(WorkItem workItem, WorkItemComment comment) =>
        hub.Clients.All.SendAsync(HubEvents.WorkItemCommentAdded, workItem.ToDto(), comment.ToDto());
}
