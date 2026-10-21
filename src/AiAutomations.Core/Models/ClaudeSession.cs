namespace AiAutomations.Core.Models;

public class ClaudeSession
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid ActionItemId { get; init; }
    public ActionItem? ActionItem { get; init; }

    public required string RepoPath { get; init; }
    public required string Prompt { get; init; }
    public ClaudeSessionStatus Status { get; set; } = ClaudeSessionStatus.Starting;
    public string? LogPath { get; set; }
    public string? ResultSummary { get; set; }

    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAtUtc { get; set; }
}
