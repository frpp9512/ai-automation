namespace AiAutomations.Shared;

/// <summary>Method names used on the InboxHub, shared so client and server never drift.</summary>
public static class HubEvents
{
    public const string MessageReceived = nameof(MessageReceived);
    public const string DecisionReady = nameof(DecisionReady);
    public const string ActionStatusChanged = nameof(ActionStatusChanged);
    public const string SessionStatusChanged = nameof(SessionStatusChanged);
    public const string WorkItemChanged = nameof(WorkItemChanged);
    public const string WorkItemCommentAdded = nameof(WorkItemCommentAdded);
}
