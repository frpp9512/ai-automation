using System.Collections.Concurrent;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlackNet;
using SlackNet.Events;
using SlackNet.SocketMode;
using SlackNet.WebApi;

namespace AiAutomations.Infrastructure.Slack;

/// <summary>
/// Socket Mode gateway to Slack, built on SlackNet. Watches for messages sent directly
/// to, or mentioning (personally or via a Slack user group they belong to), SlackOptions.SelfUserId.
///
/// Slack app setup this depends on (see ARCHITECTURE.md for the full checklist):
///  - Socket Mode enabled, with an app-level token (xapp-...) that has connections:write.
///  - Bot token (xoxb-...) scopes: im:history, chat:write, users:read.
///  - Event subscriptions: message.im (bot-scoped, for DMs to the bot) AND, to see mentions
///    of the *human* user rather than the app, message.channels/message.groups/message.mpim
///    subscribed "on behalf of user" (user token events) so Handle(MessageEvent) can scan
///    every message the user can see for their own &lt;@UserId&gt; mention. AppMention
///    (app_mention) only fires when the bot/app itself is @-mentioned, so it's handled here
///    too but isn't the primary "mentions me" signal.
///  - UserToken (xoxp-...) for: date-range backfill (search.messages isn't available to bot
///    tokens at all), user group membership lookups (usergroups:read, user scope), and as a
///    fallback for SendReplyAsync when the bot isn't a participant in the target conversation
///    (e.g. a DM between the human user and someone else) - that fallback needs chat:write on
///    the user token too, which isn't required for backfill alone and is easy to miss granting.
/// </summary>
public class SlackGateway(IOptions<SlackOptions> options, ILogger<SlackGateway> logger)
    : ISlackGateway, IEventHandler<MessageEvent>, IEventHandler<AppMention>
{
    private static readonly TimeSpan GroupMembershipRefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConnectionWatchdogInterval = TimeSpan.FromSeconds(20);

    private readonly SlackOptions _options = options.Value;
    private SlackServiceBuilder? _builder;
    private ISlackApiClient? _apiClient;
    private ISlackApiClient? _userApiClient;
    private ISlackSocketModeClient? _socketModeClient;

    // Replaced wholesale on refresh (never mutated in place), so reads from the event handler
    // never need to lock against a concurrent refresh.
    private volatile IReadOnlySet<string> _memberGroupIds = new HashSet<string>();

    // Set the moment the watchdog first observes Connected go false; cleared (and the gap
    // reported) once it comes back. Only this watchdog loop touches it, so no locking needed.
    private DateTimeOffset? _disconnectedAtUtc;

    // Display names change rarely enough that one lookup per user per process run is plenty -
    // avoids a users.info call for every single message from an already-seen sender.
    private readonly ConcurrentDictionary<string, string?> _userNameCache = new();

    public event Func<SlackMessage, Task>? OnMessageReceived;
    public event Func<DateTimeOffset, Task>? OnReconnectedAfterGap;

    public bool IsConnected => _socketModeClient?.Connected ?? false;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.AppLevelToken))
        {
            logger.LogWarning("Slack:BotToken / Slack:AppLevelToken are not configured - Socket Mode connection skipped");
            return;
        }

        _builder = new SlackServiceBuilder()
            .UseApiToken(_options.BotToken)
            .UseAppLevelToken(_options.AppLevelToken)
            .RegisterEventHandler<MessageEvent>(this)
            .RegisterEventHandler<AppMention>(this);

        _apiClient = _builder.GetApiClient();
        _userApiClient = string.IsNullOrWhiteSpace(_options.UserToken)
            ? null
            : _apiClient.WithAccessToken(_options.UserToken);

        await RefreshUserGroupMembershipsAsync(cancellationToken);
        _ = RunPeriodicGroupMembershipRefreshAsync(cancellationToken);

        _socketModeClient = _builder.GetSocketModeClient();
        await _socketModeClient.Connect(new SocketModeConnectionOptions(), cancellationToken);
        logger.LogInformation("Slack Socket Mode connected");

        _ = RunConnectionWatchdogAsync(cancellationToken);
    }

    /// <summary>
    /// SlackNet's socket client exposes no disconnect/reconnect event, so this polls Connected
    /// and, on finding it false, replaces the client with a fresh one from the same builder
    /// (rather than assuming reconnecting the same instance is supported) and retries on the
    /// next tick if that fails too. Once back up, reports how long the gap was so the caller
    /// can backfill whatever arrived while disconnected.
    /// </summary>
    private async Task RunConnectionWatchdogAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ConnectionWatchdogInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_socketModeClient is null || _builder is null)
                {
                    continue;
                }

                if (_socketModeClient.Connected)
                {
                    if (_disconnectedAtUtc is { } gapStart)
                    {
                        _disconnectedAtUtc = null;
                        logger.LogInformation("Slack Socket Mode reconnected (was down since {GapStart:u})", gapStart);

                        if (OnReconnectedAfterGap is not null)
                        {
                            try
                            {
                                await OnReconnectedAfterGap.Invoke(gapStart);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Reconnect-gap handler threw");
                            }
                        }
                    }

                    continue;
                }

                _disconnectedAtUtc ??= DateTimeOffset.UtcNow;
                logger.LogWarning("Slack Socket Mode is disconnected (down since {GapStart:u}) - attempting to reconnect", _disconnectedAtUtc);

                try
                {
                    var freshClient = _builder.GetSocketModeClient();
                    await freshClient.Connect(new SocketModeConnectionOptions(), cancellationToken);

                    var previousClient = _socketModeClient;
                    _socketModeClient = freshClient;
                    await DisposeSocketClientAsync(previousClient);

                    logger.LogInformation("Slack Socket Mode reconnect succeeded");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Slack Socket Mode reconnect attempt failed - will retry in {Interval}", ConnectionWatchdogInterval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App shutting down - nothing to do.
        }
    }

    private static async Task DisposeSocketClientAsync(ISlackSocketModeClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch
        {
            // Best-effort cleanup of the replaced connection - never let this block reconnecting.
        }
    }

    public async Task Handle(MessageEvent slackEvent)
    {
        // Ignore edits/deletes/joins/bot echoes - only plain human messages matter here.
        if (!string.IsNullOrEmpty(slackEvent.Subtype) || slackEvent.Hidden || !string.IsNullOrEmpty(slackEvent.BotId))
        {
            return;
        }

        if (string.IsNullOrEmpty(slackEvent.User) || string.IsNullOrEmpty(slackEvent.Text))
        {
            return;
        }

        var isDirectMessage = slackEvent.ChannelType == "im";
        var isMention = IsMentionOfWatchedUser(slackEvent.Text);

        if (!isDirectMessage && !isMention)
        {
            return; // a message in a channel we can see that doesn't involve the watched user
        }

        await RaiseAsync(await ToSlackMessageAsync(slackEvent, isDirectMessage, isMention, CancellationToken.None));
    }

    public async Task Handle(AppMention slackEvent)
    {
        if (string.IsNullOrEmpty(slackEvent.User) || string.IsNullOrEmpty(slackEvent.Text))
        {
            return;
        }

        await RaiseAsync(await ToSlackMessageAsync(slackEvent, isDirectMessage: false, isMention: true, CancellationToken.None));
    }

    public async Task<IReadOnlyList<SlackMessage>> GetThreadContextAsync(string channelId, string threadTs, CancellationToken cancellationToken)
    {
        // Prefer the user token: most messages this app cares about (channels/groups/mpim the
        // human is in but the bot never joined) are only readable by the user token - the bot
        // token can only read conversations it's actually a member of.
        var client = _userApiClient ?? _apiClient;
        if (client is null)
        {
            return [];
        }

        var response = await client.Conversations.Replies(channelId, threadTs, cancellationToken: cancellationToken);
        var results = new List<SlackMessage>();
        foreach (var m in response.Messages.Where(m => m.Ts != threadTs))
        {
            results.Add(await ToSlackMessageAsync(m, isDirectMessage: false, isMention: false, cancellationToken));
        }
        return results;
    }

    public async Task<IReadOnlyList<SlackMessage>> GetRecentHistoryAsync(string channelId, string beforeTs, int limit, CancellationToken cancellationToken)
    {
        var client = _userApiClient ?? _apiClient;
        if (client is null)
        {
            return [];
        }

        var response = await client.Conversations.History(channelId, latestTs: beforeTs, inclusive: false, limit: limit, cancellationToken: cancellationToken);

        var results = new List<SlackMessage>();
        foreach (var m in response.Messages)
        {
            results.Add(await ToSlackMessageAsync(m, isDirectMessage: false, isMention: false, cancellationToken));
        }

        // The API returns newest-first; flip to chronological order to match GetThreadContextAsync.
        results.Reverse();
        return results;
    }

    public async Task SendReplyAsync(string channelId, string? threadTs, string text, CancellationToken cancellationToken)
    {
        if (_apiClient is null)
        {
            throw new InvalidOperationException("SlackGateway has not connected yet (StartAsync not called, or tokens are missing).");
        }

        try
        {
            await _apiClient.Chat.PostMessage(new Message { Channel = channelId, Text = text, ThreadTs = threadTs }, cancellationToken);
        }
        catch (Exception ex) when (_userApiClient is not null && IsChannelAccessError(ex))
        {
            // The bot token isn't actually a participant of this conversation - e.g. a DM
            // between the human user and someone else, only ever seen through the user token's
            // own search/backfill (the bot was never invited to it, so posting as the bot fails
            // with channel_not_found even though the channel ID itself is correct). Retry as the
            // human user, who genuinely is a party to it.
            logger.LogInformation(ex, "Bot token couldn't post to {ChannelId} - retrying with the user token", channelId);

            // AsUser=true tells Slack's classic chat.postMessage to post as the authenticated
            // human rather than defaulting to "as the bot" even when called with a user token -
            // without it, Slack asks for the bot-only chat:write:bot scope, which a user token
            // can never hold, instead of the user-grantable chat:write scope it actually needs.
            await _userApiClient.Chat.PostMessage(new Message { Channel = channelId, Text = text, ThreadTs = threadTs, AsUser = true }, cancellationToken);
        }
    }

    private static bool IsChannelAccessError(Exception ex) =>
        ex.Message.Contains("channel_not_found", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("not_in_channel", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SlackMessage>> SearchMessagesInRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (_userApiClient is null)
        {
            logger.LogWarning("Slack:UserToken is not configured - date-range search is unavailable (search.messages requires a user token, not a bot token)");
            return [];
        }

        // NOTE: `to:` normally expects a @username, not a raw user ID - verify this resolves
        // correctly against a real workspace and adjust to `to:@{username}` if it doesn't.
        var dateRange = $"after:{from:yyyy-MM-dd} before:{to.AddDays(1):yyyy-MM-dd}";

        var queries = new List<(string Query, bool IsMention)>
        {
            ($"<@{_options.SelfUserId}> {dateRange}", true),
            ($"to:{_options.SelfUserId} {dateRange}", false)
        };
        queries.AddRange(_memberGroupIds.Select(groupId => ($"<!subteam^{groupId}> {dateRange}", true)));

        // Safety cap: 20 pages (~400 messages at the default page size) per query, so a very
        // busy date range can't turn a backfill into an unbounded crawl.
        const int maxPages = 20;

        var found = new Dictionary<(string Channel, string Ts), SlackMessage>();
        foreach (var (query, isMention) in queries)
        {
            var page = 1;
            int totalPages;
            do
            {
                var response = await _userApiClient.Search.Messages(query, page: page, cancellationToken: cancellationToken);
                totalPages = Math.Max(1, response.Messages.Paging.Pages);

                foreach (var match in response.Messages.Matches)
                {
                    if (string.IsNullOrEmpty(match.Channel?.Id) || string.IsNullOrEmpty(match.Ts))
                    {
                        continue;
                    }

                    found[(match.Channel.Id, match.Ts)] = new SlackMessage
                    {
                        SlackChannelId = match.Channel.Id,
                        SlackUserId = match.User ?? "",
                        SlackUserName = await GetUserDisplayNameAsync(match.User, cancellationToken),
                        SlackTs = match.Ts,
                        Text = match.Text ?? "",
                        Permalink = match.Permalink,
                        IsDirectMessage = match.Channel.IsIm,
                        IsMention = isMention || IsMentionOfWatchedUser(match.Text),
                        ReceivedAtUtc = DateTimeOffset.UtcNow
                    };
                }

                page++;
            } while (page <= totalPages && page <= maxPages);
        }

        return found.Values.ToList();
    }

    private async Task RefreshUserGroupMembershipsAsync(CancellationToken cancellationToken)
    {
        if (_userApiClient is null)
        {
            return; // group-mention detection needs the user token (usergroups:read, user scope)
        }

        try
        {
            var groups = await _userApiClient.UserGroups.List(includeUsers: true, cancellationToken: cancellationToken);
            _memberGroupIds = groups
                .Where(g => g.Users is not null && g.Users.Contains(_options.SelfUserId))
                .Select(g => g.Id)
                .ToHashSet();
            logger.LogInformation("Refreshed Slack user group memberships: {Count} group(s)", _memberGroupIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh Slack user group memberships - group mentions won't be detected until this succeeds");
        }
    }

    private async Task RunPeriodicGroupMembershipRefreshAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(GroupMembershipRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshUserGroupMembershipsAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // App shutting down - nothing to do.
        }
    }

    /// <summary>True if the text contains a direct `&lt;@UserId&gt;` mention, or mentions a Slack user group the watched user belongs to.</summary>
    private bool IsMentionOfWatchedUser(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Contains($"<@{_options.SelfUserId}>", StringComparison.Ordinal))
        {
            return true;
        }

        return _memberGroupIds.Any(groupId => text.Contains($"<!subteam^{groupId}", StringComparison.Ordinal));
    }

    private Task RaiseAsync(SlackMessage message) => OnMessageReceived?.Invoke(message) ?? Task.CompletedTask;

    public async Task<string?> GetUserDisplayNameAsync(string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        if (_userNameCache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var client = _apiClient ?? _userApiClient;
        if (client is null)
        {
            return null;
        }

        try
        {
            var user = await client.Users.Info(userId, includeLocale: false, cancellationToken);
            var name = FirstNonEmpty(user.Profile?.DisplayName, user.RealName, user.Name);
            _userNameCache[userId] = name;
            return name;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve display name for Slack user {UserId}", userId);
            _userNameCache[userId] = null; // don't retry every single message from this user
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private async Task<SlackMessage> ToSlackMessageAsync(MessageEventBase e, bool isDirectMessage, bool isMention, CancellationToken cancellationToken) => new()
    {
        SlackChannelId = e.Channel ?? "",
        SlackUserId = e.User ?? "",
        SlackUserName = await GetUserDisplayNameAsync(e.User, cancellationToken),
        SlackTs = e.Ts ?? "",
        ThreadTs = e.ThreadTs,
        Text = e.Text ?? "",
        IsDirectMessage = isDirectMessage,
        IsMention = isMention,
        ReceivedAtUtc = DateTimeOffset.UtcNow
    };
}
