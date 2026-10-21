using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>Classifies a Slack message (with thread context) into a structured decision.</summary>
public interface IAiDecisionEngine
{
    Task<MessageDecision> ClassifyAsync(
        SlackMessage message,
        IReadOnlyList<SlackMessage> threadContext,
        IReadOnlyList<RepositoryProfile> knownRepositories,
        CancellationToken cancellationToken);

    /// <summary>Free-form plain-text completion - used for on-demand helpers (e.g. improving a
    /// Work Item's description) that don't need ClassifyAsync's structured JSON shape.</summary>
    Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken);
}
