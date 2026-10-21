using AiAutomations.Core.Abstractions;

namespace AiAutomations.Infrastructure.Pipeline;

/// <inheritdoc cref="IPipelineActivityTracker"/>
public class PipelineActivityTracker : IPipelineActivityTracker
{
    private int _pendingCount;

    public int PendingCount => _pendingCount;

    public void Increment() => Interlocked.Increment(ref _pendingCount);

    public void Decrement() => Interlocked.Decrement(ref _pendingCount);
}
