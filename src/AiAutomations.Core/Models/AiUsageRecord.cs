namespace AiAutomations.Core.Models;

/// <summary>One AI API call's token usage - purely for the health details "AI usage" summary,
/// not used by any decision-making logic.</summary>
public class AiUsageRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Provider { get; init; }
    public required string Model { get; init; }

    /// <summary>What the call was for - "Classify", "GenerateText", etc.</summary>
    public required string Purpose { get; init; }

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    /// <summary>Best-effort - null when the model isn't in the known pricing table rather than
    /// guessing at a cost.</summary>
    public decimal? EstimatedCostUsd { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
