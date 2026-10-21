using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Hosting;

/// <summary>
/// Bridges the singleton ISlackGateway's events to the scoped IMessageIngestPipeline
/// (a new DI scope per message, since AppDbContext is scoped) and starts the Socket
/// Mode connection once subscribed. Also runs a catch-up backfill on startup (from the
/// last message this app ever saw, to now) and again whenever the gateway reports it
/// reconnected after an unexpected drop - so neither a restart nor a network blip can
/// leave a silent gap in what got triaged.
///
/// Every message is persisted immediately (so it shows up in history right away), but AI
/// classification is debounced per-conversation via IMessageAggregationBuffer - a burst of
/// several messages in a row only costs one classification call, run against the latest message
/// once the burst goes quiet.
/// </summary>
public class SlackIngestHostedService(
    ISlackGateway slackGateway,
    IServiceScopeFactory scopeFactory,
    IMessageAggregationBuffer aggregationBuffer,
    IPipelineActivityTracker activityTracker,
    ILogger<SlackIngestHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        slackGateway.OnMessageReceived += OnMessageReceivedAsync;
        slackGateway.OnReconnectedAfterGap += OnReconnectedAfterGapAsync;
        await slackGateway.StartAsync(cancellationToken);

        if (slackGateway.IsConnected)
        {
            await RunCatchUpBackfillAsync(since: null, reason: "startup", cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        slackGateway.OnMessageReceived -= OnMessageReceivedAsync;
        slackGateway.OnReconnectedAfterGap -= OnReconnectedAfterGapAsync;
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SlackMessage message)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IMessageIngestPipeline>();

        SlackMessage? persisted;
        try
        {
            persisted = await pipeline.PersistAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Slack message {ChannelId}/{Ts}", message.SlackChannelId, message.SlackTs);
            return;
        }

        if (persisted is not null)
        {
            activityTracker.Increment();
            aggregationBuffer.ScheduleClassification(persisted, ClassifyAsync);
        }
    }

    private async Task ClassifyAsync(SlackMessage message)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IMessageIngestPipeline>();
        try
        {
            await pipeline.ClassifyAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to classify Slack message {ChannelId}/{Ts}", message.SlackChannelId, message.SlackTs);
        }
        finally
        {
            activityTracker.Decrement();
        }
    }

    private Task OnReconnectedAfterGapAsync(DateTimeOffset disconnectedAtUtc) =>
        RunCatchUpBackfillAsync(disconnectedAtUtc, "reconnect", CancellationToken.None);

    /// <summary>since=null means "derive it from the last message this app ever recorded" (the
    /// startup case); a first-ever run with no messages at all has nothing to catch up on.</summary>
    private async Task RunCatchUpBackfillAsync(DateTimeOffset? since, string reason, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            var effectiveSince = since;
            if (effectiveSince is null)
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                effectiveSince = await db.Messages.MaxAsync(m => (DateTimeOffset?)m.ReceivedAtUtc, cancellationToken);
            }

            if (effectiveSince is null)
            {
                return;
            }

            var runner = scope.ServiceProvider.GetRequiredService<IBackfillRunner>();
            var outcome = await runner.RunAsync(effectiveSince.Value, DateTimeOffset.UtcNow, cancellationToken);

            logger.LogInformation(
                "Catch-up backfill ({Reason}) from {Since:u}: found {Found}, ingested {New} new, {Skipped} already seen",
                reason, effectiveSince, outcome.MessagesFound, outcome.NewMessages, outcome.Skipped);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catch-up backfill ({Reason}) failed", reason);
        }
    }
}
