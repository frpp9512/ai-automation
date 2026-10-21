namespace AiAutomations.Core.Models;

/// <summary>
/// A note on a WorkItem - either typed by the user, or appended automatically when a later
/// Slack message is recognized as belonging to an already-tracked task (same thread).
/// </summary>
public class WorkItemComment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid WorkItemId { get; init; }
    public WorkItem? WorkItem { get; init; }

    public required string Text { get; set; }

    /// <summary>True when this was appended automatically from a linked Slack message, not typed by the user.</summary>
    public bool IsAutomated { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
