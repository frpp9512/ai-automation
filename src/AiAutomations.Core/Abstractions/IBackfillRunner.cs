namespace AiAutomations.Core.Abstractions;

public record BackfillOutcome(int MessagesFound, int NewMessages, int Skipped);

/// <summary>
/// Shared implementation of "search Slack for a date range and run every result through the
/// ingest pipeline" - used by both hosts' manual Backfill endpoints and by the automatic
/// catch-up that runs after startup and after an unexpected Socket Mode reconnect.
/// </summary>
public interface IBackfillRunner
{
    Task<BackfillOutcome> RunAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
