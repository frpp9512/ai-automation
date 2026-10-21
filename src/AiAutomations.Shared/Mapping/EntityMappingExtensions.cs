using AiAutomations.Core.Models;

namespace AiAutomations.Shared.Mapping;

public static class EntityMappingExtensions
{
    public static SlackMessageDto ToDto(this SlackMessage m) => new(
        m.Id, m.SlackChannelId, m.SlackUserId, m.SlackUserName, m.SlackTs, m.ThreadTs, m.Text, m.Permalink,
        m.IsDirectMessage, m.IsMention, m.ReceivedAtUtc);

    public static MessageDecisionDto ToDto(this MessageDecision d) => new(
        d.Id, d.MessageId, d.Intent, d.Urgency, d.Summary, d.SuggestedReply, d.TaskDetails,
        d.SuggestedRepositoryName, d.Confidence, d.CreatedAtUtc);

    public static ActionItemDto ToDto(this ActionItem a) => new(
        a.Id, a.DecisionId, a.Type, a.Status, a.Payload, a.CreatedAtUtc, a.ResolvedAtUtc, a.FailureReason);

    public static ClaudeSessionDto ToDto(this ClaudeSession s) => new(
        s.Id, s.ActionItemId, s.RepoPath, s.Status, s.ResultSummary, s.StartedAtUtc, s.FinishedAtUtc);

    public static AutoActionRuleDto ToDto(this AutoActionRule r) => new(
        r.Id, r.Name, r.IsEnabled, r.Priority, r.AppliesTo, r.RequiredIntent, r.MinConfidence, r.AllowlistIds);

    public static RepositoryProfileDto ToDto(this RepositoryProfile r) => new(r.Id, r.Name, r.Path, r.Keywords, r.CreatedAtUtc);

    public static WorkItemDto ToDto(this WorkItem w) => new(
        w.Id, w.Title, w.Description, w.Status, w.Priority, w.Type, w.RepositoryName, w.Labels,
        w.DueAtUtc, w.PercentComplete, w.ExternalSystem, w.ExternalId, w.ExternalUrl, w.SourceDecisionId,
        w.CreatedAtUtc, w.UpdatedAtUtc, w.CompletedAtUtc);

    public static WorkItemCommentDto ToDto(this WorkItemComment c) => new(c.Id, c.WorkItemId, c.Text, c.IsAutomated, c.CreatedAtUtc);
}
