using AiAutomations.Core.Models;

namespace AiAutomations.Shared;

// Flat, serialization-friendly views of the Core entities, used over REST and SignalR.
// Deliberately separate from Core.Models so the wire contract can evolve independently
// of the EF-mapped domain model.

public record SlackMessageDto(
    Guid Id,
    string SlackChannelId,
    string SlackUserId,
    string? SlackUserName,
    string SlackTs,
    string? ThreadTs,
    string Text,
    string? Permalink,
    bool IsDirectMessage,
    bool IsMention,
    DateTimeOffset ReceivedAtUtc)
{
    /// <summary>Display name if resolved, otherwise the raw Slack user ID - use this wherever
    /// a sender is shown to the user instead of SlackUserId directly.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(SlackUserName) ? SlackUserId : SlackUserName;
}

public record MessageDecisionDto(
    Guid Id,
    Guid MessageId,
    MessageIntent Intent,
    Urgency Urgency,
    string Summary,
    string? SuggestedReply,
    string? TaskDetails,
    string? SuggestedRepositoryName,
    double Confidence,
    DateTimeOffset CreatedAtUtc);

public record ActionItemDto(
    Guid Id,
    Guid DecisionId,
    ActionType Type,
    ActionStatus Status,
    string? Payload,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? FailureReason);

public record ClaudeSessionDto(
    Guid Id,
    Guid ActionItemId,
    string RepoPath,
    ClaudeSessionStatus Status,
    string? ResultSummary,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

public record ApproveActionRequest(string? EditedPayload);

public record RejectActionRequest(string? Reason);

public record BackfillRequest(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// The shape stored in ActionItem.Payload for ActionType.TaskBrief. RepoName/RepoPath are
/// pre-filled from the AI's suggested repository match when one was found; Brief is the full
/// ready-to-copy markdown (conversation + task details) for handing to Claude Code manually.
/// </summary>
public record TaskBriefPayload(string? RepoName, string? RepoPath, string Prompt, string Brief);

/// <summary>
/// The shape stored in ActionItem.Payload for ActionType.Ticket. RepositoryName is pre-filled
/// from the AI's suggested match when one was found - null means the AI couldn't determine one
/// and the user should pick it (or explicitly confirm none applies) before approving.
/// </summary>
public record TicketPayload(string Title, string? RepositoryName);

public record BackfillResultDto(int MessagesFound, int NewMessages, int Skipped);

public record AutoActionRuleDto(
    Guid Id,
    string Name,
    bool IsEnabled,
    int Priority,
    ActionType AppliesTo,
    MessageIntent? RequiredIntent,
    double MinConfidence,
    string? AllowlistIds);

public record RepositoryProfileDto(Guid Id, string Name, string Path, string? Keywords, DateTimeOffset CreatedAtUtc);

public record UpsertRepositoryRequest(string Name, string Path, string? Keywords);

public record HealthStatusDto(bool DatabaseOk, bool SlackConnected, bool AiProviderConfigured, string AiProvider, int PendingMessageCount = 0);

/// <summary>AI token/cost usage over a trailing window (currently the last 24 hours) - see
/// TriageAppService.GetAiUsageSummaryAsync. EstimatedCostUsd is null when none of the window's
/// calls matched a known model in the pricing table, rather than showing a guessed total.</summary>
public record AiUsageSummaryDto(
    int CallCount,
    long InputTokens,
    long OutputTokens,
    decimal? EstimatedCostUsd,
    IReadOnlyList<AiUsageByModelDto> ByModel);

public record AiUsageByModelDto(
    string Provider,
    string Model,
    int CallCount,
    long InputTokens,
    long OutputTokens,
    decimal? EstimatedCostUsd);

public record WorkItemDto(
    Guid Id,
    string Title,
    string? Description,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    WorkItemType Type,
    string? RepositoryName,
    string? Labels,
    DateTimeOffset? DueAtUtc,
    int PercentComplete,
    string? ExternalSystem,
    string? ExternalId,
    string? ExternalUrl,
    Guid? SourceDecisionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public record UpsertWorkItemRequest(
    string Title,
    string? Description,
    WorkItemPriority Priority,
    WorkItemType Type,
    string? RepositoryName,
    string? Labels,
    DateTimeOffset? DueAtUtc);

/// <summary>Full edit of an existing WorkItem's fields (distinct from UpsertWorkItemRequest,
/// which is create-only and has no PercentComplete since a brand-new item starts at 0).</summary>
public record UpdateWorkItemRequest(
    string Title,
    string? Description,
    WorkItemPriority Priority,
    WorkItemType Type,
    string? RepositoryName,
    string? Labels,
    DateTimeOffset? DueAtUtc,
    int PercentComplete);

public record UpdateWorkItemStatusRequest(WorkItemStatus Status);

public record WorkItemCommentDto(Guid Id, Guid WorkItemId, string Text, bool IsAutomated, DateTimeOffset CreatedAtUtc);

public record AddWorkItemCommentRequest(string Text);
