using AiAutomations.Core.Models;

namespace AiAutomations.Infrastructure.Pipeline;

/// <summary>
/// Debounces when a freshly-persisted message actually gets classified, so a burst of several
/// messages in the same conversation (a common Slack pattern - someone typing their thought
/// across 2-3 separate messages instead of one) costs a single AI call instead of one per
/// message.
/// </summary>
public interface IMessageAggregationBuffer
{
    /// <summary>
    /// Schedules <paramref name="classify"/> to run for <paramref name="message"/>'s conversation
    /// after a quiet period with no further messages in it. If another message in the same
    /// conversation arrives first, the timer resets and only the newest message ends up
    /// classified - its stored conversation context already covers everything sent before it
    /// (each message is persisted immediately, before this debounce), so nothing is lost.
    /// </summary>
    void ScheduleClassification(SlackMessage message, Func<SlackMessage, Task> classify);
}
