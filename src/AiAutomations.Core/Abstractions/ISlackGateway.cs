using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>Wraps all inbound/outbound Slack interaction behind one seam.</summary>
public interface ISlackGateway
{
    event Func<SlackMessage, Task>? OnMessageReceived;

    /// <summary>
    /// Fired after the Socket Mode connection is automatically re-established following an
    /// unexpected drop, with the UTC time the drop was first detected - the gap to backfill so
    /// nothing sent while disconnected is missed.
    /// </summary>
    event Func<DateTimeOffset, Task>? OnReconnectedAfterGap;

    /// <summary>True once Socket Mode has connected. Used for the dashboard's health status.</summary>
    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SlackMessage>> GetThreadContextAsync(string channelId, string threadTs, CancellationToken cancellationToken);

    /// <summary>
    /// Fallback context source for a message that isn't part of a formal Slack thread: the
    /// messages immediately before it in the same channel/DM, oldest first. Without this, a
    /// plain back-and-forth conversation (no one used "reply in thread") would get classified
    /// one message at a time with no memory of what was already said.
    /// </summary>
    Task<IReadOnlyList<SlackMessage>> GetRecentHistoryAsync(string channelId, string beforeTs, int limit, CancellationToken cancellationToken);

    Task SendReplyAsync(string channelId, string? threadTs, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Backfill: finds messages sent directly to, or mentioning, the watched user within
    /// [from, to], so a catch-up run can plan tasks for things that arrived before this
    /// service was listening. Requires a user token (search.messages is not available to
    /// bot tokens) - see SlackOptions.UserToken.
    /// </summary>
    Task<IReadOnlyList<SlackMessage>> SearchMessagesInRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Resolves a Slack user ID to a friendly display name (profile display name,
    /// falling back to real name then username), cached in-process since it never changes
    /// often enough to look up more than once per run. Null if it can't be resolved.</summary>
    Task<string?> GetUserDisplayNameAsync(string? userId, CancellationToken cancellationToken);
}
