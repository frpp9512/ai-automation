namespace AiAutomations.Core.Models;

public enum MessageIntent
{
    Unclassified,
    FyiOnly,
    ReplyNeeded,
    TaskNeeded,
    ImplementationNeeded,
    PlanningNeeded
}

public enum Urgency
{
    Low,
    Medium,
    High
}

public enum ActionType
{
    Reply,
    Ticket,

    /// <summary>
    /// A detailed, ready-to-copy brief for implementation/planning work. By default (no
    /// Anthropic key / Claude:AutoLaunch=false) approving one just finalizes the brief for
    /// you to hand to a Claude Code session yourself; set AutoLaunch=true to have it launch
    /// a real headless session instead, once that's viable again.
    /// </summary>
    TaskBrief
}

public enum ActionStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Executed,
    Failed
}

public enum ClaudeSessionStatus
{
    Starting,
    Running,
    Completed,
    Failed
}
