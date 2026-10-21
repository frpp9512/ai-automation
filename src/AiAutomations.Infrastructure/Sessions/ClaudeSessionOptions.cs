namespace AiAutomations.Infrastructure.Sessions;

public class ClaudeSessionOptions
{
    public const string SectionName = "Claude";

    /// <summary>
    /// When false (default - no Anthropic key needed), approving a TaskBrief action just
    /// finalizes the brief for you to copy into a Claude Code session yourself. When true,
    /// it instead launches a real headless session via IClaudeSessionLauncher.
    /// </summary>
    public bool AutoLaunch { get; set; }
}
