namespace AiAutomations.Core.Abstractions;

/// <summary>
/// Live count of Slack messages that have arrived and are currently in flight - either sitting
/// in the aggregation/debounce window or actively being classified - but haven't produced a
/// decision (or been auto-linked) yet. Purely in-memory/ephemeral, for surfacing "being
/// processed right now" in the health strip; not persisted and not a substitute for the message
/// log in the database.
/// </summary>
public interface IPipelineActivityTracker
{
    int PendingCount { get; }

    void Increment();

    void Decrement();
}
