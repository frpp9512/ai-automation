namespace AiAutomations.Core.Models;

/// <summary>
/// Policy that decides whether an ActionItem can be executed automatically
/// or must wait for approval in a client. Evaluated in order by Priority.
/// </summary>
public class AutoActionRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }

    public ActionType AppliesTo { get; set; }
    public MessageIntent? RequiredIntent { get; set; }
    public double MinConfidence { get; set; } = 1.0;

    /// <summary>Comma-separated Slack user/channel IDs this rule allows; empty = everyone.</summary>
    public string? AllowlistIds { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
