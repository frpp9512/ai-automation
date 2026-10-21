using AiAutomations.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace AiAutomations.Client.Web.Services;

/// <summary>
/// One shared connection to the server's InboxHub for the whole app (not per-circuit) -
/// Blazor components subscribe/unsubscribe to these events from OnInitialized/Dispose.
/// </summary>
public class RealtimeClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public event Action<SlackMessageDto>? MessageReceived;
    public event Action<MessageDecisionDto>? DecisionReady;
    public event Action<ActionItemDto>? ActionStatusChanged;
    public event Action<ClaudeSessionDto>? SessionStatusChanged;
    public event Action<WorkItemDto>? WorkItemChanged;

    public RealtimeClient(IOptions<ServerConnectionOptions> options)
    {
        var opts = options.Value;
        _connection = new HubConnectionBuilder()
            .WithUrl($"{opts.BaseUrl.TrimEnd('/')}/hubs/inbox", httpOptions =>
            {
                httpOptions.Headers.Add("X-Api-Key", opts.ApiKey);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<SlackMessageDto>(HubEvents.MessageReceived, m => MessageReceived?.Invoke(m));
        _connection.On<MessageDecisionDto>(HubEvents.DecisionReady, d => DecisionReady?.Invoke(d));
        _connection.On<ActionItemDto>(HubEvents.ActionStatusChanged, a => ActionStatusChanged?.Invoke(a));
        _connection.On<ClaudeSessionDto>(HubEvents.SessionStatusChanged, s => SessionStatusChanged?.Invoke(s));
        _connection.On<WorkItemDto>(HubEvents.WorkItemChanged, w => WorkItemChanged?.Invoke(w));
    }

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public Task StartAsync(CancellationToken cancellationToken = default) => _connection.StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
