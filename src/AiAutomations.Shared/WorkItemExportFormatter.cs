using System.Text;

namespace AiAutomations.Shared;

/// <summary>
/// Formats a WorkItem as plain markdown text a PM can paste straight into Jira/Trello/Asana/
/// whatever system they use to create the real ticket - deliberately generic (no target-system
/// schema assumed) since the point is a clean handoff, not an API integration.
/// </summary>
public static class WorkItemExportFormatter
{
    public static string Format(WorkItemDto item, IReadOnlyList<WorkItemCommentDto>? comments = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {item.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Type:** {item.Type}");
        sb.AppendLine($"**Priority:** {item.Priority}");
        sb.AppendLine($"**Status:** {item.Status}");
        sb.AppendLine($"**Repository:** {item.RepositoryName ?? "(none)"}");
        sb.AppendLine($"**Due:** {(item.DueAtUtc is { } due ? due.ToLocalTime().ToString("MMM d, yyyy") : "(none)")}");
        sb.AppendLine($"**Completion:** {item.PercentComplete}%");
        if (!string.IsNullOrWhiteSpace(item.Labels))
        {
            sb.AppendLine($"**Labels:** {item.Labels}");
        }
        sb.AppendLine();

        sb.AppendLine("## Description");
        sb.AppendLine(string.IsNullOrWhiteSpace(item.Description) ? "(none)" : item.Description);
        sb.AppendLine();

        if (comments is { Count: > 0 })
        {
            sb.AppendLine("## Comments");
            foreach (var comment in comments.OrderBy(c => c.CreatedAtUtc))
            {
                var tag = comment.IsAutomated ? " (auto-linked from Slack)" : string.Empty;
                sb.AppendLine($"- [{comment.CreatedAtUtc.ToLocalTime():MMM d, yyyy h:mm tt}{tag}] {comment.Text}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"_Originally tracked in AI Automations - created {item.CreatedAtUtc.ToLocalTime():MMM d, yyyy}._");

        return sb.ToString();
    }
}
