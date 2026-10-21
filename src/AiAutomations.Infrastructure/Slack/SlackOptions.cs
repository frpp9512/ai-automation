namespace AiAutomations.Infrastructure.Slack;

public class SlackOptions
{
    public const string SectionName = "Slack";

    /// <summary>Bot token (xoxb-...) with im:history, app_mentions:read, channels:history, chat:write, users:read scopes.</summary>
    public required string BotToken { get; set; }

    /// <summary>App-level token (xapp-...) used to open the Socket Mode connection.</summary>
    public required string AppLevelToken { get; set; }

    /// <summary>
    /// User token (xoxp-...) for the person this app watches on their behalf.
    /// Required only for date-range backfill: search.messages (used to find messages
    /// sent to/mentioning this user) is not available to bot tokens at all.
    /// </summary>
    public string? UserToken { get; set; }

    /// <summary>This user's own Slack member ID (e.g. U0123ABC), used to build search queries and detect mentions.</summary>
    public required string SelfUserId { get; set; }
}
