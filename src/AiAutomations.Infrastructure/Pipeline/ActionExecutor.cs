using System.Text.Json;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.Sessions;
using AiAutomations.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAutomations.Infrastructure.Pipeline;

public class ActionExecutor(
    AppDbContext db,
    ISlackGateway slackGateway,
    IClaudeSessionLauncher sessionLauncher,
    IRealtimeNotifier notifier,
    IOptions<ClaudeSessionOptions> claudeOptions,
    ILogger<ActionExecutor> logger) : IActionExecutor
{
    public async Task ExecuteAsync(ActionItem action, CancellationToken cancellationToken)
    {
        try
        {
            switch (action.Type)
            {
                case ActionType.Reply:
                    await ExecuteReplyAsync(action, cancellationToken);
                    break;
                case ActionType.TaskBrief:
                    await ExecuteTaskBriefAsync(action, cancellationToken);
                    break;
                case ActionType.Ticket:
                    await ExecuteTicketAsync(action, cancellationToken);
                    break;
                default:
                    throw new NotSupportedException($"Unknown action type {action.Type}");
            }

            action.Status = ActionStatus.Executed;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute action {ActionId} ({Type})", action.Id, action.Type);
            action.Status = ActionStatus.Failed;
            action.FailureReason = ex.Message;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.ActionStatusChangedAsync(action);
    }

    private async Task ExecuteReplyAsync(ActionItem action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.Payload))
        {
            throw new InvalidOperationException("Reply action has no text - edit the payload before approving.");
        }

        var decision = await db.Decisions
            .Include(d => d.Message)
            .FirstOrDefaultAsync(d => d.Id == action.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Decision {action.DecisionId} not found for action {action.Id}");

        var message = decision.Message
            ?? throw new InvalidOperationException($"Decision {decision.Id} has no linked message");

        var threadTs = message.ThreadTs ?? message.SlackTs;
        await slackGateway.SendReplyAsync(message.SlackChannelId, threadTs, action.Payload, cancellationToken);
    }

    private async Task ExecuteTaskBriefAsync(ActionItem action, CancellationToken cancellationToken)
    {
        var payload = string.IsNullOrWhiteSpace(action.Payload)
            ? null
            : JsonSerializer.Deserialize<TaskBriefPayload>(action.Payload);

        if (payload is null)
        {
            throw new InvalidOperationException("TaskBrief action has no payload.");
        }

        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == action.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Decision {action.DecisionId} not found for action {action.Id}");

        // Always leaves a trackable Work Item - the AutoLaunch session below (when enabled)
        // is a bonus, not a substitute for having this on the board.
        var workItem = new WorkItem
        {
            Title = decision.Summary,
            Description = payload.Brief,
            Priority = MapPriority(decision.Urgency),
            RepositoryName = payload.RepoName,
            SourceDecisionId = decision.Id
        };
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.WorkItemChangedAsync(workItem);

        if (!claudeOptions.Value.AutoLaunch)
        {
            // No Anthropic key this version - the brief is already fully built (see
            // MessageIngestPipeline.BuildBrief); approving just finalizes it for manual handoff.
            return;
        }

        if (string.IsNullOrWhiteSpace(payload.RepoPath))
        {
            throw new InvalidOperationException("RepoPath must be set before approving a TaskBrief action with AutoLaunch enabled.");
        }

        var session = await sessionLauncher.StartAsync(action.Id, payload.RepoPath, payload.Prompt, cancellationToken);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.SessionStatusChangedAsync(session);
    }

    private async Task ExecuteTicketAsync(ActionItem action, CancellationToken cancellationToken)
    {
        var decision = await db.Decisions.FirstOrDefaultAsync(d => d.Id == action.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Decision {action.DecisionId} not found for action {action.Id}");

        var payload = string.IsNullOrWhiteSpace(action.Payload)
            ? null
            : JsonSerializer.Deserialize<TicketPayload>(action.Payload);

        var workItem = new WorkItem
        {
            Title = payload?.Title ?? decision.Summary,
            Description = decision.TaskDetails ?? decision.Summary,
            Priority = MapPriority(decision.Urgency),
            RepositoryName = payload?.RepositoryName ?? decision.SuggestedRepositoryName,
            SourceDecisionId = decision.Id
        };

        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.WorkItemChangedAsync(workItem);
    }

    private static WorkItemPriority MapPriority(Urgency urgency) => urgency switch
    {
        Urgency.High => WorkItemPriority.High,
        Urgency.Medium => WorkItemPriority.Medium,
        _ => WorkItemPriority.Low
    };
}
