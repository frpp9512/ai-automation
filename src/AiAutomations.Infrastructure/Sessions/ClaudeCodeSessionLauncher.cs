using System.Collections.Concurrent;
using System.Diagnostics;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Sessions;

/// <summary>
/// Launches a headless Claude Code session ("claude -p &lt;prompt&gt;") in a target repo
/// and tracks its process. Deliberately never pushes/merges on its own - a session only
/// produces a local branch/diff (or a PR if the prompt asks for one) for human review.
/// </summary>
public class ClaudeCodeSessionLauncher(ILogger<ClaudeCodeSessionLauncher> logger) : IClaudeSessionLauncher
{
    // Keeps the Process object (and its EnableRaisingEvents subscription) alive for as
    // long as the process runs - nothing else holds a reference to it otherwise.
    private readonly ConcurrentDictionary<Guid, Process> _running = new();

    public event Func<ClaudeSession, Task>? OnSessionStatusChanged;

    public Task<ClaudeSession> StartAsync(Guid actionItemId, string repoPath, string prompt, CancellationToken cancellationToken)
    {
        var session = new ClaudeSession
        {
            ActionItemId = actionItemId,
            RepoPath = repoPath,
            Prompt = prompt,
            Status = ClaudeSessionStatus.Starting
        };

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            session.Status = process.ExitCode == 0 ? ClaudeSessionStatus.Completed : ClaudeSessionStatus.Failed;
            session.FinishedAtUtc = DateTimeOffset.UtcNow;
            logger.LogInformation("Claude Code session {SessionId} finished with status {Status}", session.Id, session.Status);
            _running.TryRemove(session.Id, out _);
            _ = OnSessionStatusChanged?.Invoke(session);
        };

        process.Start();
        session.Status = ClaudeSessionStatus.Running;
        _running[session.Id] = process;

        return Task.FromResult(session);
    }
}
