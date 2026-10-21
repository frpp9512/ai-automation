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

    /// <summary>UTC time of the most recent successful OpenProject pull for this item; null for
    /// an item that was never synced (including every non-OpenProject WorkItem).</summary>
    public DateTimeOffset? LastSyncedAtUtc { get; set; }

    /// <summary>Error message from the most recent failed OpenProject sync attempt for this
    /// specific item, if any - cleared on the next successful sync.</summary>
    public string? SyncError { get; set; }

    /// <summary>
    /// False when RepositoryName was left unassigned because OpenProjectSyncHostedService's
    /// AI-based project match came back below the configured confidence threshold - surfaced in
    /// the Tasks board as "needs a repository" until the user picks one (or explicitly confirms
    /// none applies). Always true for a WorkItem not sourced from OpenProject, and for one where
    /// the match was confident enough to auto-assign.
    /// </summary>
    public bool RepositoryAssignmentConfirmed { get; set; } = true;

    /// <summary>UTC time of the most recent (billed) AI repository-match attempt for this item,
    /// while still unconfirmed - lets OpenProjectSyncHostedService apply a cooldown instead of
    /// re-matching (and re-billing) on every single sync poll. Null until the first attempt.</summary>
    public DateTimeOffset? LastRepositoryMatchAttemptAtUtc { get; set; }

    /// <summary>Set when this was created from a Slack-triaged decision, for traceability.</summary>
    public Guid? SourceDecisionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Creates a new WorkItem for a freshly-synced OpenProject work package, starting with
    /// RepositoryAssignmentConfirmed = false so OpenProjectSyncHostedService's AI-based repository
    /// match always gets a chance to run - a dedicated factory rather than a plain object
    /// initializer, so a future second OpenProject-creation path can't forget to set that flag the
    /// way the original implementation once did.
    /// </summary>
    public static WorkItem CreateFromOpenProject(string externalId, string title) => new()
    {
        Title = title,
        ExternalSystem = "OpenProject",
        ExternalId = externalId,
        RepositoryAssignmentConfirmed = false
    };
}
