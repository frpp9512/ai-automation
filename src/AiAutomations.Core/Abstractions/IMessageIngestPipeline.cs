using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>
/// The one path a SlackMessage takes to become a decision and actions, whether it
/// arrived live (Socket Mode) or from a date-range backfill search - persist, classify,
/// derive ActionItems, notify clients. Skips messages already seen (same channel+ts).
/// </summary>
public interface IMessageIngestPipeline
{
    /// <summary>Saves the message and checks whether it's a reply in a thread that's already
    /// tracked as a Work Item (recorded as a comment there, no AI call). Returns the message if
    /// there's still something to decide about it (worth classifying), or null if it was a
    /// duplicate or got auto-linked and there's nothing further to do.</summary>
    Task<SlackMessage?> PersistAsync(SlackMessage message, CancellationToken cancellationToken);

    /// <summary>Runs AI classification and derives any resulting ActionItem for an
    /// already-persisted message. Call this directly only when no debounce/aggregation is
    /// wanted (e.g. backfill); live ingestion should debounce via the aggregation buffer.</summary>
    Task<MessageDecision?> ClassifyAsync(SlackMessage message, CancellationToken cancellationToken);

    /// <summary>Convenience for callers (e.g. backfill) that want persist+classify in one step,
    /// with no debounce.</summary>
    Task<MessageDecision?> IngestAsync(SlackMessage message, CancellationToken cancellationToken);
}
