using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;

namespace AiAutomations.Tests.Fakes;

public class FakeClaudeSessionLauncher : IClaudeSessionLauncher
{
    public List<(Guid ActionItemId, string RepoPath, string Prompt)> StartedSessions { get; } = [];

    public event Func<ClaudeSession, Task>? OnSessionStatusChanged;

    public Task<ClaudeSession> StartAsync(Guid actionItemId, string repoPath, string prompt, CancellationToken cancellationToken)
    {
        StartedSessions.Add((actionItemId, repoPath, prompt));
        var session = new ClaudeSession
        {
            ActionItemId = actionItemId,
            RepoPath = repoPath,
            Prompt = prompt,
            Status = ClaudeSessionStatus.Running
        };
        return Task.FromResult(session);
    }

    public Task RaiseSessionStatusChangedAsync(ClaudeSession session) => OnSessionStatusChanged?.Invoke(session) ?? Task.CompletedTask;
}
