namespace AiAutomations.Core.Models;

public enum WorkItemStatus
{
    Todo,
    InProgress,
    InReview,
    Done,
    Cancelled
}

public enum WorkItemPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum WorkItemType
{
    Task,
    Bug,
    Feature,
    Chore
}

/// <summary>
/// A self-hosted task/ticket, modeled on the fields most systems (Jira, OpenProject, etc.)
/// use, so the field set itself is the contract for later syncing out to one of those rather
/// than something to redesign at that point. ExternalSystem/ExternalId/ExternalUrl are reserved
/// for that - unused until a real sync target exists.
/// </summary>
public class WorkItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Todo;
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;
    public WorkItemType Type { get; set; } = WorkItemType.Task;

    /// <summary>Free-text repository name (usually matches a RepositoryProfile.Name, not enforced).</summary>
    public string? RepositoryName { get; set; }

    /// <summary>Comma-separated, like Jira labels.</summary>
    public string? Labels { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }

    /// <summary>0-100, user-maintained (not inferred from Status).</summary>
    public int PercentComplete { get; set; }

    /// <summary>Reserved for a future external work tracker sync - e.g. "Jira", "PROJ-123", a browse URL.</summary>
    public string? ExternalSystem { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }

    /// <summary>Set when this was created from a Slack-triaged decision, for traceability.</summary>
    public Guid? SourceDecisionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
