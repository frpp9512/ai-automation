namespace AiAutomations.Core.Abstractions;

/// <summary>Records token usage for an AI API call, for the health details "AI usage" summary.
/// Best-effort by design - a tracking failure must never fail the AI call it's reporting on.</summary>
public interface IAiUsageTracker
{
    Task RecordAsync(string provider, string model, string purpose, int inputTokens, int outputTokens, CancellationToken cancellationToken = default);
}
