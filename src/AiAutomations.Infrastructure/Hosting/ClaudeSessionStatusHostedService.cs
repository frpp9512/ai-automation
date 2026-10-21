using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Hosting;

/// <summary>
/// Bridges the singleton IClaudeSessionLauncher's completion events to the DB and to
/// clients - a session can finish long after the request that approved it has ended,
/// so this needs its own scope rather than relying on ActionExecutor's.
/// </summary>
public class ClaudeSessionStatusHostedService(
    IClaudeSessionLauncher sessionLauncher,
    IServiceScopeFactory scopeFactory,
    ILogger<ClaudeSessionStatusHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessionLauncher.OnSessionStatusChanged += OnSessionStatusChangedAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sessionLauncher.OnSessionStatusChanged -= OnSessionStatusChangedAsync;
        return Task.CompletedTask;
    }

    private async Task OnSessionStatusChangedAsync(ClaudeSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

        try
        {
            var existing = await db.Sessions.FindAsync(session.Id);
            if (existing is null)
            {
                logger.LogWarning("Session {SessionId} finished before it was persisted - dropping status update", session.Id);
                return;
            }

            existing.Status = session.Status;
            existing.FinishedAtUtc = session.FinishedAtUtc;
            await db.SaveChangesAsync();
            await notifier.SessionStatusChangedAsync(existing);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist completion for session {SessionId}", session.Id);
        }
    }
}
