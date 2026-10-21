namespace AiAutomations.Core.Models;

/// <summary>One OpenProjectSyncHostedService run - purely for the sync-history/health surfacing
/// requirement ("errors visible and actionable"), not used by any decision-making logic.</summary>
public class OpenProjectSyncRun
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int ItemsCreated { get; set; }
    public int ItemsUpdated { get; set; }
    public int ItemsFailed { get; set; }

    /// <summary>Set when the whole run failed outright (e.g. couldn't reach the API at all), as
    /// opposed to a per-item failure already counted in ItemsFailed.</summary>
    public string? ErrorMessage { get; set; }
}
