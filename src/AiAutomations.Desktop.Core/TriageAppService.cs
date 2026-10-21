using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared;
using AiAutomations.Shared.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAutomations.Desktop.Core;

/// <summary>
/// In-process equivalent of ServerApiClient + the read/write controllers it talks to -
/// same EF queries and IActionExecutor/IRealtimeNotifier calls, just without the HTTP
/// wrapper. Resolves a scoped AppDbContext/IActionExecutor per call since this service
/// itself is a singleton (bound to the app's lifetime, not a per-request scope).
/// </summary>
public class TriageAppService(IServiceScopeFactory scopeFactory)
{
    /// <summary>Includes Failed alongside PendingApproval - an action whose execution failed
    /// after approval hasn't actually succeeded yet, so it stays actionable in the Inbox (edit
    /// and re-approve, or reject) instead of silently disappearing.</summary>
    public async Task<List<ActionItemDto>> GetPendingActionsAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Actions
            .Where(a => a.Status == ActionStatus.PendingApproval || a.Status == ActionStatus.Failed)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => a.ToDto())
            .ToListAsync(ct);
    }

    public async Task<List<MessageDecisionDto>> GetRecentDecisionsAsync(int take = 50, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Decisions
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(take)
            .Select(d => d.ToDto())
            .ToListAsync(ct);
    }

    public async Task<List<SlackMessageDto>> GetRecentMessagesAsync(int take = 50, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Messages
            .OrderByDescending(m => m.ReceivedAtUtc)
            .Take(take)
            .Select(m => m.ToDto())
            .ToListAsync(ct);
    }

    /// <summary>Used for the History nav item's unread badge - how many decisions have landed
    /// since the user last opened that page.</summary>
    public async Task<int> GetDecisionCountSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Decisions.CountAsync(d => d.CreatedAtUtc > since, ct);
    }

    public async Task<MessageDecisionDto?> GetDecisionAsync(Guid decisionId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var decision = await db.Decisions.FindAsync([decisionId], ct);
        return decision?.ToDto();
    }

    /// <summary>Looks up any action regardless of status - used for failure-notification
    /// deep-links, since a Failed action is no longer in GetPendingActionsAsync's result.</summary>
    public async Task<ActionItemDto?> GetActionAsync(Guid actionId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var action = await db.Actions.FindAsync([actionId], ct);
        return action?.ToDto();
    }

    public async Task<SlackMessageDto?> GetMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = await db.Messages.FindAsync([messageId], ct);
        return message?.ToDto();
    }

    public async Task<List<RepositoryProfileDto>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Repositories
            .OrderBy(r => r.Name)
            .Select(r => r.ToDto())
            .ToListAsync(ct);
    }

    public async Task<RepositoryProfileDto> CreateRepositoryAsync(string name, string path, string? keywords, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = new RepositoryProfile { Name = name, Path = path, Keywords = keywords };
        db.Repositories.Add(repo);
        await db.SaveChangesAsync(ct);
        return repo.ToDto();
    }

    public async Task<RepositoryProfileDto?> UpdateRepositoryAsync(Guid id, string name, string path, string? keywords, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = await db.Repositories.FindAsync([id], ct);
        if (repo is null) return null;

        repo.Name = name;
        repo.Path = path;
        repo.Keywords = keywords;
        await db.SaveChangesAsync(ct);
        return repo.ToDto();
    }

    public async Task DeleteRepositoryAsync(Guid id, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var repo = await db.Repositories.FindAsync([id], ct);
        if (repo is null) return;

        db.Repositories.Remove(repo);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<WorkItemDto>> GetWorkItemsAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.WorkItems
            .OrderByDescending(w => w.CreatedAtUtc)
            .Select(w => w.ToDto())
            .ToListAsync(ct);
    }

    public async Task<WorkItemDto> CreateWorkItemAsync(UpsertWorkItemRequest request, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

        var item = new WorkItem
        {
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            Type = request.Type,
            RepositoryName = request.RepositoryName,
            Labels = request.Labels,
            DueAtUtc = request.DueAtUtc
        };

        db.WorkItems.Add(item);
        await db.SaveChangesAsync(ct);
        await notifier.WorkItemChangedAsync(item);
        return item.ToDto();
    }

    public async Task<WorkItemDto?> UpdateWorkItemStatusAsync(Guid id, WorkItemStatus status, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

        var item = await db.WorkItems.FindAsync([id], ct);
        if (item is null) return null;

        item.Status = status;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        item.CompletedAtUtc = status is WorkItemStatus.Done or WorkItemStatus.Cancelled
            ? (item.CompletedAtUtc ?? DateTimeOffset.UtcNow)
            : null;

        await db.SaveChangesAsync(ct);
        await notifier.WorkItemChangedAsync(item);
        return item.ToDto();
    }

    public async Task DeleteWorkItemAsync(Guid id, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.WorkItems.FindAsync([id], ct);
        if (item is null) return;

        db.WorkItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkItemDto?> UpdateWorkItemAsync(Guid id, UpdateWorkItemRequest request, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

        var item = await db.WorkItems.FindAsync([id], ct);
        if (item is null) return null;

        item.Title = request.Title;
        item.Description = request.Description;
        item.Priority = request.Priority;
        item.Type = request.Type;
        item.RepositoryName = request.RepositoryName;
        item.Labels = request.Labels;
        item.DueAtUtc = request.DueAtUtc;
        item.PercentComplete = Math.Clamp(request.PercentComplete, 0, 100);
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await notifier.WorkItemChangedAsync(item);
        return item.ToDto();
    }

    public async Task<int> GetActiveWorkItemCountAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.WorkItems.CountAsync(w => w.Status != WorkItemStatus.Done && w.Status != WorkItemStatus.Cancelled, ct);
    }

    public async Task<List<WorkItemCommentDto>> GetCommentsAsync(Guid workItemId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.WorkItemComments
            .Where(c => c.WorkItemId == workItemId)
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => c.ToDto())
            .ToListAsync(ct);
    }

    /// <summary>One grouped query instead of one-per-card, so showing a comment count on every
    /// Tasks board card doesn't turn into an N+1 query pattern.</summary>
    public async Task<Dictionary<Guid, int>> GetCommentCountsAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.WorkItemComments
            .GroupBy(c => c.WorkItemId)
            .Select(g => new { WorkItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkItemId, x => x.Count, ct);
    }

    public async Task<WorkItemCommentDto> AddCommentAsync(Guid workItemId, string text, bool isAutomated = false, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var comment = new WorkItemComment { WorkItemId = workItemId, Text = text, IsAutomated = isAutomated };
        db.WorkItemComments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment.ToDto();
    }

    public async Task<ActionItemDto?> ApproveActionAsync(Guid actionId, string? editedPayload = null, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var executor = scope.ServiceProvider.GetRequiredService<IActionExecutor>();

        var action = await db.Actions.FindAsync([actionId], ct);
        if (action is null) return null;

        if (editedPayload is not null)
        {
            action.Payload = editedPayload;
        }

        action.Status = ActionStatus.Approved;
        action.ResolvedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.ActionStatusChangedAsync(action);

        // Flips action.Status to Executed/Failed itself and notifies again.
        await executor.ExecuteAsync(action, ct);

        return action.ToDto();
    }

    /// <summary>
    /// Drafts a short, friendly Slack reply acknowledging that the originating message was
    /// triaged and acted on. Never sent on its own - always returned for the caller to review
    /// (and the user to explicitly send) via <see cref="SendAcknowledgmentAsync"/>.
    /// </summary>
    public async Task<string?> GenerateAcknowledgmentAsync(Guid actionId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();

        var action = await db.Actions.FindAsync([actionId], ct)
            ?? throw new InvalidOperationException($"Action {actionId} not found");
        var decision = await db.Decisions.FindAsync([action.DecisionId], ct);
        if (decision is null) return null;

        var message = await db.Messages.FindAsync([decision.MessageId], ct);
        if (message is null) return null;

        var outcome = action.Type switch
        {
            ActionType.Reply => "a reply was sent back to you",
            ActionType.Ticket => "a ticket was created to track it",
            ActionType.TaskBrief => "a task was created and a technical brief prepared for implementation",
            _ => "it was processed"
        };

        var prompt = BuildAcknowledgmentPrompt(decision.Summary, message.Text, outcome);
        var draft = await aiEngine.GenerateTextAsync(prompt, ct);
        return draft.Trim();
    }

    private static string BuildAcknowledgmentPrompt(string summary, string originalText, string outcome) => $"""
        You're writing a brief, friendly Slack reply to acknowledge that someone's message has
        been triaged and acted on.

        Original message: {originalText}
        What was understood: {summary}
        What happened next: {outcome}

        Write a short (1-2 sentence) Slack reply acknowledging the message and letting them know
        {outcome}. Keep it casual and natural, like a quick human reply - no corporate tone, no
        preamble, no quotes. Respond with ONLY the reply text.
        """;

    /// <summary>
    /// Drafts a short, constructive Slack reply explaining that this item is being declined -
    /// used by the Inbox card's "Reply" option. Never sent on its own; the caller sends it (via
    /// <see cref="SendAcknowledgmentAsync"/>, which posts to whatever thread the action came from
    /// regardless of what the message says) only after the user explicitly approves it.
    /// </summary>
    public async Task<string?> GenerateDeclineFeedbackAsync(Guid actionId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();

        var action = await db.Actions.FindAsync([actionId], ct)
            ?? throw new InvalidOperationException($"Action {actionId} not found");
        var decision = await db.Decisions.FindAsync([action.DecisionId], ct);
        if (decision is null) return null;

        var message = await db.Messages.FindAsync([decision.MessageId], ct);
        if (message is null) return null;

        var prompt = BuildDeclineFeedbackPrompt(decision.Summary, message.Text);
        var draft = await aiEngine.GenerateTextAsync(prompt, ct);
        return draft.Trim();
    }

    private static string BuildDeclineFeedbackPrompt(string summary, string originalText) => $"""
        You're writing a brief, respectful Slack reply explaining that you're not going to act on
        someone's message - the goal is to give them useful feedback, not just silence.

        Original message: {originalText}
        What was understood: {summary}

        Write a short (1-2 sentence) Slack reply that lets them know this isn't being actioned,
        giving a plausible, constructive reason grounded only in what's actually known above (e.g.
        not enough detail, already handled elsewhere, out of scope) - don't invent specifics you
        don't have. If you genuinely can't infer a reason, just acknowledge it without one. Keep it
        casual and natural, no corporate tone, no preamble, no quotes. Respond with ONLY the reply
        text.
        """;

    /// <summary>Posts an acknowledgment (or decline-feedback) reply to the thread the action
    /// originated from - only ever called after the user has explicitly reviewed and approved
    /// sending it.</summary>
    public async Task SendAcknowledgmentAsync(Guid actionId, string text, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slackGateway = scope.ServiceProvider.GetRequiredService<ISlackGateway>();

        var action = await db.Actions.FindAsync([actionId], ct)
            ?? throw new InvalidOperationException($"Action {actionId} not found");
        var decision = await db.Decisions.FindAsync([action.DecisionId], ct)
            ?? throw new InvalidOperationException($"Decision for action {actionId} not found");
        var message = await db.Messages.FindAsync([decision.MessageId], ct)
            ?? throw new InvalidOperationException($"Message for decision {decision.Id} not found");

        await slackGateway.SendReplyAsync(message.SlackChannelId, message.ThreadTs ?? message.SlackTs, text, ct);
    }

    public async Task<ActionItemDto?> RejectActionAsync(Guid actionId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

        var action = await db.Actions.FindAsync([actionId], ct);
        if (action is null) return null;

        action.Status = ActionStatus.Rejected;
        action.ResolvedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.ActionStatusChangedAsync(action);

        return action.ToDto();
    }

    /// <summary>
    /// Asks the AI to rewrite a Work Item's description at a technical level, grounded in the
    /// real target repository's CLAUDE.md/README (or directory structure as a fallback) rather
    /// than guessing. Returns the suggested text for the user to review/edit before saving -
    /// never overwrites the item itself, consistent with how every other AI output in this app
    /// stays editable rather than applied silently.
    /// </summary>
    public async Task<string> ImproveWorkItemDescriptionAsync(Guid workItemId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();
        var repoInspector = scope.ServiceProvider.GetRequiredService<IRepositoryInspector>();

        var item = await db.WorkItems.FindAsync([workItemId], ct)
            ?? throw new InvalidOperationException($"WorkItem {workItemId} not found");

        string? repoContext = null;
        if (!string.IsNullOrWhiteSpace(item.RepositoryName))
        {
            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Name == item.RepositoryName, ct);
            if (repo is not null)
            {
                repoContext = await repoInspector.GetTechnicalContextAsync(repo.Path, ct);
            }
        }

        var prompt = BuildImprovePrompt(item.Title, item.Description, repoContext);
        var improved = await aiEngine.GenerateTextAsync(prompt, ct);
        return improved.Trim();
    }

    private static string BuildImprovePrompt(string title, string? description, string? repoContext) => $"""
        You're a senior software engineer writing a technical task description for another
        engineer on the team.

        {(repoContext is null ? "No repository context is available for this task." : $"Repository context:\n{repoContext}")}

        Task title: {title}
        Current description: {(string.IsNullOrWhiteSpace(description) ? "(none written yet)" : description)}

        Rewrite the description to be clear, specific, and technically grounded in the repository
        context above - name actual projects/components/files from it where relevant. Keep it
        concise (3-6 sentences, or a short bullet list). Do not invent file paths, APIs, or project
        names that aren't mentioned in the context above - phrase anything it doesn't cover
        generically rather than guessing specifics. Respond with ONLY the improved description
        text - no preamble, no markdown headers, no quotes around it.
        """;

    /// <summary>
    /// Asks the AI to refine a draft comment (clearer wording, professional tone) before it's
    /// posted - grounded in the work item's own title/description so the refinement stays on
    /// topic. Returns the suggested text for the user to review/edit; never posts it itself.
    /// </summary>
    public async Task<string> RefineCommentAsync(Guid workItemId, string draftText, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();

        var item = await db.WorkItems.FindAsync([workItemId], ct)
            ?? throw new InvalidOperationException($"WorkItem {workItemId} not found");

        var prompt = BuildRefineCommentPrompt(item.Title, item.Description, draftText);
        var refined = await aiEngine.GenerateTextAsync(prompt, ct);
        return refined.Trim();
    }

    private static string BuildRefineCommentPrompt(string title, string? description, string draftText) => $"""
        You're a software engineer refining a short status comment you're about to post on a
        tracked task, for teammates who may check in on it later.

        Task title: {title}
        Task description: {(string.IsNullOrWhiteSpace(description) ? "(none)" : description)}

        Draft comment: {draftText}

        Rewrite the draft to be clear, concise, and professional, keeping its original meaning
        and any specific facts/numbers/names it mentions - do not invent new ones. Keep it short
        (1-3 sentences). Respond with ONLY the refined comment text - no preamble, no quotes,
        no markdown.
        """;

    /// <summary>
    /// Asks the AI to polish an action's draft text (reply, ticket title, or task brief) before
    /// it's approved - grounded in the selected repository's context when one applies. Used by
    /// the Inbox card's "Refine and approve" option; returns the suggested text for the caller
    /// to approve with, never applies it on its own.
    /// </summary>
    public async Task<string> RefineActionDetailsAsync(Guid actionId, string currentText, string? repositoryName, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();
        var repoInspector = scope.ServiceProvider.GetRequiredService<IRepositoryInspector>();

        var action = await db.Actions.FindAsync([actionId], ct)
            ?? throw new InvalidOperationException($"Action {actionId} not found");
        var decision = await db.Decisions.FindAsync([action.DecisionId], ct);

        string? repoContext = null;
        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Name == repositoryName, ct);
            if (repo is not null)
            {
                repoContext = await repoInspector.GetTechnicalContextAsync(repo.Path, ct);
            }
        }

        var prompt = BuildRefineActionPrompt(action.Type, decision?.Summary, currentText, repoContext);
        var refined = await aiEngine.GenerateTextAsync(prompt, ct);
        return refined.Trim();
    }

    private static string BuildRefineActionPrompt(ActionType type, string? summary, string currentText, string? repoContext)
    {
        var kind = type switch
        {
            ActionType.Reply => "a Slack reply message",
            ActionType.Ticket => "a short ticket title",
            ActionType.TaskBrief => "a technical task brief for an engineer",
            _ => "a short piece of text"
        };

        return $"""
            You're refining {kind} before it's approved and sent/created.

            {(summary is null ? string.Empty : $"Context summary: {summary}\n")}
            {(repoContext is null ? string.Empty : $"Repository context:\n{repoContext}\n")}

            Current draft:
            {currentText}

            Rewrite it to be clearer and more polished while keeping the original meaning and any
            specific facts/names/numbers it mentions - do not invent new ones. Respond with ONLY
            the refined text - no preamble, no quotes, no markdown headers.
            """;
    }

    /// <summary>
    /// Builds a ready-to-paste implementation prompt for a Work Item, grounded in the real
    /// target repository's CLAUDE.md/README (same IRepositoryInspector context as the
    /// description-improve feature) plus the task's own comment history - meant to be handed
    /// to a bigger coding model (Claude Code, Codex) to actually do the implementation.
    /// </summary>
    public async Task<string> GenerateImplementationPromptAsync(Guid workItemId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repoInspector = scope.ServiceProvider.GetRequiredService<IRepositoryInspector>();

        var item = await db.WorkItems.FindAsync([workItemId], ct)
            ?? throw new InvalidOperationException($"WorkItem {workItemId} not found");

        string? repoContext = null;
        if (!string.IsNullOrWhiteSpace(item.RepositoryName))
        {
            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Name == item.RepositoryName, ct);
            if (repo is not null)
            {
                repoContext = await repoInspector.GetTechnicalContextAsync(repo.Path, ct);
            }
        }

        var comments = await db.WorkItemComments
            .Where(c => c.WorkItemId == workItemId)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);

        return BuildImplementationPrompt(item, repoContext, comments);
    }

    private static string BuildImplementationPrompt(WorkItem item, string? repoContext, List<WorkItemComment> comments)
    {
        var commentsBlock = comments.Count == 0
            ? "(none)"
            : string.Join("\n", comments.Select(c => $"- {c.CreatedAtUtc.ToLocalTime():g}: {c.Text}"));

        return $"""
            # Task: {item.Title}

            ## Description
            {(string.IsNullOrWhiteSpace(item.Description) ? "(none written)" : item.Description)}

            ## Priority / Type
            {item.Priority} / {item.Type}

            ## Target repository
            {(item.RepositoryName is null ? "(not specified - confirm with the user before assuming one)" : item.RepositoryName)}
            {(repoContext is null ? string.Empty : $"\n{repoContext}")}

            ## Comment history
            {commentsBlock}

            ---
            Implement this task in the repository above. Follow its existing conventions and
            architecture as shown in the context. If anything here is ambiguous or the repository
            context doesn't cover a detail you need, ask before guessing.
            """;
    }

    /// <summary>
    /// Catch-up: finds messages sent to/mentioning the watched user (or a group they belong to)
    /// in [From, To] that arrived before this app was listening, and runs them through the same
    /// classify-and-derive-actions pipeline as a live message. Requires Slack:UserToken to be
    /// configured (search.messages needs a user token).
    /// </summary>
    public async Task<BackfillResultDto> BackfillAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IBackfillRunner>();

        var outcome = await runner.RunAsync(from, to, ct);
        return new BackfillResultDto(outcome.MessagesFound, outcome.NewMessages, outcome.Skipped);
    }

    /// <summary>AI token/cost usage over the last 24 hours, for the health details dialog.</summary>
    public async Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var records = await db.AiUsageRecords.Where(r => r.CreatedAtUtc >= since).ToListAsync(ct);

        var byModel = records
            .GroupBy(r => (r.Provider, r.Model))
            .Select(g => new AiUsageByModelDto(
                Provider: g.Key.Provider,
                Model: g.Key.Model,
                CallCount: g.Count(),
                InputTokens: g.Sum(r => (long)r.InputTokens),
                OutputTokens: g.Sum(r => (long)r.OutputTokens),
                EstimatedCostUsd: g.Any(r => r.EstimatedCostUsd is not null) ? g.Sum(r => r.EstimatedCostUsd ?? 0) : null))
            .OrderByDescending(m => m.CallCount)
            .ToList();

        return new AiUsageSummaryDto(
            CallCount: records.Count,
            InputTokens: records.Sum(r => (long)r.InputTokens),
            OutputTokens: records.Sum(r => (long)r.OutputTokens),
            EstimatedCostUsd: byModel.Any(m => m.EstimatedCostUsd is not null) ? byModel.Sum(m => m.EstimatedCostUsd ?? 0) : null,
            ByModel: byModel);
    }

    /// <summary>Full path to the live SQLite database file - used by backup/restore.</summary>
    public string GetDatabaseFilePath()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Database.GetDbConnection().DataSource;
    }

    /// <summary>Writes a consistent full copy of the live database to destinationPath via
    /// SQLite's VACUUM INTO, so a backup taken while the app is running (and possibly mid-write)
    /// is never a torn snapshot the way a plain file copy could be.</summary>
    public async Task BackupDatabaseAsync(string destinationPath, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            var param = command.CreateParameter();
            param.ParameterName = "$path";
            param.Value = destinationPath;
            command.Parameters.Add(param);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Replaces the live database file with sourcePath, clearing SQLite's connection
    /// pool first so no pooled handle blocks the overwrite. The app must be restarted
    /// immediately after this - the running process (and anything it has cached) would
    /// otherwise keep behaving as if the old file were still there.</summary>
    public void RestoreDatabase(string sourcePath)
    {
        var dbPath = GetDatabaseFilePath();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(sourcePath, dbPath, overwrite: true);
    }

    public async Task<HealthStatusDto> GetHealthAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slackGateway = scope.ServiceProvider.GetRequiredService<ISlackGateway>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var activityTracker = scope.ServiceProvider.GetRequiredService<IPipelineActivityTracker>();

        var databaseOk = await db.Database.CanConnectAsync(ct);
        var provider = string.Equals(configuration["Ai:Provider"], "Anthropic", StringComparison.OrdinalIgnoreCase)
            ? "Anthropic"
            : "OpenAiCompatible";
        var apiKey = provider == "Anthropic" ? configuration["Anthropic:ApiKey"] : configuration["OpenAiCompatible:ApiKey"];

        return new HealthStatusDto(
            DatabaseOk: databaseOk,
            SlackConnected: slackGateway.IsConnected,
            AiProviderConfigured: !string.IsNullOrWhiteSpace(apiKey),
            AiProvider: provider,
            PendingMessageCount: activityTracker.PendingCount);
    }
}
