using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;

namespace AiAutomations.Tests.Fakes;

public class FakeSlackGateway : ISlackGateway
{
    public List<(string ChannelId, string? ThreadTs, string Text)> SentReplies { get; } = [];

    public event Func<SlackMessage, Task>? OnMessageReceived;
    public event Func<DateTimeOffset, Task>? OnReconnectedAfterGap;

    public bool IsConnected => true;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<SlackMessage>> GetThreadContextAsync(string channelId, string threadTs, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SlackMessage>>([]);

    public Task<IReadOnlyList<SlackMessage>> GetRecentHistoryAsync(string channelId, string beforeTs, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SlackMessage>>([]);

    public Task SendReplyAsync(string channelId, string? threadTs, string text, CancellationToken cancellationToken)
    {
        SentReplies.Add((channelId, threadTs, text));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SlackMessage>> SearchMessagesInRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SlackMessage>>([]);

    public Task<string?> GetUserDisplayNameAsync(string? userId, CancellationToken cancellationToken) =>
        Task.FromResult(userId);

    public Task RaiseMessageReceivedAsync(SlackMessage message) => OnMessageReceived?.Invoke(message) ?? Task.CompletedTask;

    public Task RaiseReconnectedAfterGapAsync(DateTimeOffset disconnectedAtUtc) => OnReconnectedAfterGap?.Invoke(disconnectedAtUtc) ?? Task.CompletedTask;
}
