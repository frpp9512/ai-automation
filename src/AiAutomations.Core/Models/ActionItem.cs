namespace AiAutomations.Core.Models;

public class ActionItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid DecisionId { get; init; }
    public MessageDecision? Decision { get; init; }

    public ActionType Type { get; init; }
    public ActionStatus Status { get; set; } = ActionStatus.PendingApproval;

    /// <summary>Type-specific payload, e.g. the draft reply text or the target repo for a session.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAtUtc { get; set; }

    /// <summary>Set when Status becomes Failed, so the user can see why instead of the action
    /// just silently disappearing from the pending list.</summary>
    public string? FailureReason { get; set; }

    public ClaudeSession? Session { get; init; }
}
