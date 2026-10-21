using System.Text;
using System.Text.Json;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Pipeline;

public class MessageIngestPipeline(
    AppDbContext db,
    IAiDecisionEngine decisionEngine,
    ISlackGateway slackGateway,
    IRealtimeNotifier notifier,
    ILogger<MessageIngestPipeline> logger) : IMessageIngestPipeline
{
    /// <summary>
    /// How many prior channel messages to pull as context when a message isn't part of a formal
    /// thread. Kept small - each one goes into the classification prompt, and ClassificationPrompt
    /// truncates each line too, so this bounds total context size rather than relying on that alone.
    /// </summary>
    private const int RecentHistoryLimit = 6;

    public async Task<SlackMessage?> PersistAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        var alreadySeen = await db.Messages.AnyAsync(
            m => m.SlackChannelId == message.SlackChannelId && m.SlackTs == message.SlackTs,
            cancellationToken);
        if (alreadySeen)
        {
            return null;
        }

        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.MessageReceivedAsync(message);

        if (await TryLinkToExistingWorkItemAsync(message, cancellationToken))
        {
            // A later reply in a thread that already produced a tracked Work Item - record it
            // as a comment on that item instead of re-classifying (saves an AI call entirely,
            // and avoids spawning a duplicate task for the same piece of work).
            return null;
        }

        return message;
    }

    public async Task<MessageDecision?> ClassifyAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        var conversationContext = await GetConversationContextAsync(message, cancellationToken);
        var knownRepositories = await db.Repositories.ToListAsync(cancellationToken);

        MessageDecision decision;
        try
        {
            decision = await decisionEngine.ClassifyAsync(message, conversationContext, knownRepositories, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Classification failed for message {MessageId}", message.Id);
            return null;
        }

        db.Decisions.Add(decision);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.DecisionReadyAsync(decision);

        var matchedRepo = knownRepositories.FirstOrDefault(r =>
            string.Equals(r.Name, decision.SuggestedRepositoryName, StringComparison.OrdinalIgnoreCase));

        var action = DeriveAction(decision, message, conversationContext, matchedRepo);
        if (action is not null)
        {
            db.Actions.Add(action);
            await db.SaveChangesAsync(cancellationToken);
            await notifier.ActionStatusChangedAsync(action);
        }

        return decision;
    }

    public async Task<MessageDecision?> IngestAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        var persisted = await PersistAsync(message, cancellationToken);
        return persisted is null ? null : await ClassifyAsync(persisted, cancellationToken);
    }

    /// <summary>
    /// Free, deterministic linking - no AI call. If this message is a reply in a thread whose
    /// root already produced a Work Item (via an approved Ticket/TaskBrief), record it as a
    /// comment on that Work Item instead of running it through classification again.
    /// </summary>
    private async Task<bool> TryLinkToExistingWorkItemAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.ThreadTs))
        {
            return false;
        }

        var linkedWorkItem = await (
            from workItem in db.WorkItems
            where workItem.SourceDecisionId != null
            join decision in db.Decisions on workItem.SourceDecisionId equals decision.Id
            join rootMessage in db.Messages on decision.MessageId equals rootMessage.Id
            where rootMessage.SlackChannelId == message.SlackChannelId && rootMessage.SlackTs == message.ThreadTs
            select workItem
        ).FirstOrDefaultAsync(cancellationToken);

        if (linkedWorkItem is null)
        {
            return false;
        }

        var comment = new WorkItemComment
        {
            WorkItemId = linkedWorkItem.Id,
            Text = $"Slack update from {DisplayLabel(message)}: {message.Text}",
            IsAutomated = true
        };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        await notifier.WorkItemCommentAddedAsync(linkedWorkItem, comment);

        logger.LogInformation(
            "Linked message {MessageId} to existing WorkItem {WorkItemId} via thread {ThreadTs} - skipped classification",
            message.Id, linkedWorkItem.Id, message.ThreadTs);

        return true;
    }

    /// <summary>
    /// Every message we've ever ingested (live or via backfill) stays in the DB as a log, so
    /// repeated pulls/pushes over the same conversation don't have to keep re-fetching it from
    /// Slack - and that stored history is itself reusable context for classifying new messages
    /// in the same thread/channel. Slack is only called when the log doesn't already have enough,
    /// and whatever it returns is folded into the log for next time.
    /// </summary>
    private async Task<IReadOnlyList<SlackMessage>> GetConversationContextAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(message.ThreadTs))
        {
            var storedThread = await db.Messages
                .Where(m => m.SlackChannelId == message.SlackChannelId
                            && (m.ThreadTs == message.ThreadTs || m.SlackTs == message.ThreadTs)
                            && m.SlackTs != message.SlackTs)
                .OrderBy(m => m.SlackTs)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (storedThread.Count > 0)
            {
                return storedThread;
            }

            var fetchedThread = await slackGateway.GetThreadContextAsync(message.SlackChannelId, message.ThreadTs, cancellationToken);
            await LogContextMessagesAsync(fetchedThread, cancellationToken);
            return fetchedThread;
        }

        var storedRecent = await db.Messages
            .Where(m => m.SlackChannelId == message.SlackChannelId && m.SlackTs != message.SlackTs)
            .OrderByDescending(m => m.SlackTs)
            .Take(RecentHistoryLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (storedRecent.Count >= RecentHistoryLimit)
        {
            return storedRecent.OrderBy(m => m.SlackTs).ToList();
        }

        var fetchedRecent = await slackGateway.GetRecentHistoryAsync(message.SlackChannelId, message.SlackTs, RecentHistoryLimit, cancellationToken);
        await LogContextMessagesAsync(fetchedRecent, cancellationToken);

        return storedRecent
            .Concat(fetchedRecent)
            .GroupBy(m => m.SlackTs)
            .Select(g => g.First())
            .OrderBy(m => m.SlackTs)
            .TakeLast(RecentHistoryLimit)
            .ToList();
    }

    /// <summary>Persists context messages fetched from Slack into the log, skipping any already stored.</summary>
    private async Task LogContextMessagesAsync(IReadOnlyList<SlackMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var m in messages)
        {
            var exists = await db.Messages.AnyAsync(
                x => x.SlackChannelId == m.SlackChannelId && x.SlackTs == m.SlackTs,
                cancellationToken);
            if (!exists)
            {
                db.Messages.Add(m);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ActionItem? DeriveAction(
        MessageDecision decision,
        SlackMessage message,
        IReadOnlyList<SlackMessage> conversationContext,
        RepositoryProfile? matchedRepo) => decision.Intent switch
    {
        MessageIntent.ReplyNeeded => new ActionItem { DecisionId = decision.Id, Type = ActionType.Reply, Payload = decision.SuggestedReply },
        MessageIntent.TaskNeeded => new ActionItem
        {
            DecisionId = decision.Id,
            Type = ActionType.Ticket,
            Payload = JsonSerializer.Serialize(new TicketPayload(decision.Summary, matchedRepo?.Name))
        },
        MessageIntent.ImplementationNeeded or MessageIntent.PlanningNeeded => new ActionItem
        {
            DecisionId = decision.Id,
            Type = ActionType.TaskBrief,
            Payload = JsonSerializer.Serialize(new TaskBriefPayload(
                RepoName: matchedRepo?.Name,
                RepoPath: matchedRepo?.Path,
                Prompt: decision.TaskDetails ?? decision.Summary,
                Brief: BuildBrief(decision, message, conversationContext, matchedRepo)))
        },
        _ => null
    };

    private static string BuildBrief(MessageDecision decision, SlackMessage message, IReadOnlyList<SlackMessage> conversationContext, RepositoryProfile? matchedRepo)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {decision.Summary}");
        sb.AppendLine();
        sb.AppendLine($"**Urgency:** {decision.Urgency}");
        sb.AppendLine($"**Repository:** {matchedRepo?.Name ?? "(none matched - set one before using this)"}" +
                       (matchedRepo is null ? "" : $" ({matchedRepo.Path})"));
        if (message.Permalink is not null)
        {
            sb.AppendLine($"**Source:** {message.Permalink}");
        }
        sb.AppendLine();

        if (conversationContext.Count > 0)
        {
            sb.AppendLine("### Conversation");
            foreach (var m in conversationContext)
            {
                sb.AppendLine($"- {DisplayLabel(m)}: {m.Text}");
            }
            sb.AppendLine($"- {DisplayLabel(message)}: {message.Text}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### Message");
            sb.AppendLine(message.Text);
            sb.AppendLine();
        }

        sb.AppendLine("### Task");
        sb.AppendLine(decision.TaskDetails ?? decision.Summary);

        return sb.ToString();
    }

    /// <summary>Resolved display name when one was looked up at ingest time, falling back to the
    /// raw Slack user ID only when it wasn't - keeps generated text (briefs, auto-linked
    /// comments) from surfacing a bare ID wherever a sender needs to be named.</summary>
    private static string DisplayLabel(SlackMessage m) => string.IsNullOrWhiteSpace(m.SlackUserName) ? m.SlackUserId : m.SlackUserName;
}
