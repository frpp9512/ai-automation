using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>Launches and tracks a headless Claude Code session for implementation/planning work.</summary>
public interface IClaudeSessionLauncher
{
    /// <summary>Fires when a running session finishes (or fails), so its status can be persisted and pushed to clients.</summary>
    event Func<ClaudeSession, Task>? OnSessionStatusChanged;

    Task<ClaudeSession> StartAsync(Guid actionItemId, string repoPath, string prompt, CancellationToken cancellationToken);
}
