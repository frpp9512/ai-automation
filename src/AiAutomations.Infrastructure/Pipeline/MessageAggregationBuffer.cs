using System.Collections.Concurrent;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Pipeline;

/// <inheritdoc cref="IMessageAggregationBuffer"/>
public class MessageAggregationBuffer(ILogger<MessageAggregationBuffer> logger) : IMessageAggregationBuffer, IDisposable
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, Timer> _timers = new();

    public void ScheduleClassification(SlackMessage message, Func<SlackMessage, Task> classify)
    {
        var key = BuildKey(message);

        if (_timers.TryRemove(key, out var previous))
        {
            previous.Dispose();
        }

        _timers[key] = new Timer(state =>
        {
            _timers.TryRemove(key, out _);
            _ = RunAsync(message, classify);
        }, null, QuietPeriod, Timeout.InfiniteTimeSpan);
    }

    private async Task RunAsync(SlackMessage message, Func<SlackMessage, Task> classify)
    {
        try
        {
            await classify(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Debounced classification failed for message {MessageId}", message.Id);
        }
    }

    /// <summary>Groups by channel+thread for a formal thread reply; otherwise by channel+sender -
    /// the "conversation starter" case where several top-level messages from the same person land
    /// one after another before any thread exists yet.</summary>
    private static string BuildKey(SlackMessage message) =>
        $"{message.SlackChannelId}:{message.ThreadTs ?? $"user:{message.SlackUserId}"}";

    public void Dispose()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }
    }
}
