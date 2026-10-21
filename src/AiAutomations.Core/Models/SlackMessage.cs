namespace AiAutomations.Core.Models;

public class SlackMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string SlackChannelId { get; init; }
    public required string SlackUserId { get; init; }

    /// <summary>Resolved display name for SlackUserId, if it could be looked up at ingest
    /// time - null means fall back to showing the raw ID.</summary>
    public string? SlackUserName { get; init; }

    public required string SlackTs { get; init; }
    public string? ThreadTs { get; init; }
    public required string Text { get; init; }
    public string? Permalink { get; init; }
    public bool IsDirectMessage { get; init; }
    public bool IsMention { get; init; }
    public string? RawJson { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public MessageDecision? Decision { get; init; }
}
