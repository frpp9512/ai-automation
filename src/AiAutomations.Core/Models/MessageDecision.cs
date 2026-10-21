namespace AiAutomations.Core.Models;

public class MessageDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required Guid MessageId { get; init; }
    public SlackMessage? Message { get; init; }

    public MessageIntent Intent { get; init; }
    public Urgency Urgency { get; init; }
    public required string Summary { get; init; }
    public string? SuggestedReply { get; init; }

    /// <summary>Detailed paragraph (context + suggested approach) for Task/Implementation/Planning intents; null otherwise.</summary>
    public string? TaskDetails { get; init; }

    /// <summary>Name of the best-matching RepositoryProfile, chosen by the model from the known registry; null if none fit.</summary>
    public string? SuggestedRepositoryName { get; init; }

    public double Confidence { get; init; }
    public string? RawModelOutput { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<ActionItem> Actions { get; init; } = [];
}
