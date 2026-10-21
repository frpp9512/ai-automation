using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAutomations.Infrastructure.OpenProject;

/// <summary>
/// Portable IHostedService (same shape as SlackIngestHostedService) that periodically pulls
/// OpenProject work packages assigned to the configured account into WorkItem. Push-back
/// (comments, percentageDone) is deliberately NOT done here - it's manual/per-item, triggered by
/// the user via TriageAppService, consistent with this app's "nothing auto-sends" rule.
///
/// Registered as both a singleton (so TriageAppService's "Sync Now" button can call
/// <see cref="RunNowAsync"/>, via <see cref="IOpenProjectSyncTrigger"/>, on the same running
/// instance) and as the IHostedService the generic host starts/stops - see AddInfrastructure.
///
/// Known limitation: every pulled field (including Status and PercentComplete) is fully
/// OpenProject-owned and overwritten on each pull - a local edit or a board drag made on a synced
/// item between polls can be overwritten by the next sync. No conflict detection is built for
/// this; push a %/comment change before the next poll if it matters.
///
/// <see cref="RunNowAsync"/> never lets an exception escape (everything it does is wrapped in
/// try/catch, log-and-continue) - both so a single bad run can't permanently kill
/// <see cref="RunLoopAsync"/>'s background loop, and so a thrown exception can't crash the WinUI
/// app via an unguarded async-void "Sync now" button click.
/// </summary>
public class OpenProjectSyncHostedService(
    IOpenProjectGateway gateway,
    IServiceScopeFactory scopeFactory,
    IOptions<OpenProjectOptions> options,
    ILogger<OpenProjectSyncHostedService> logger) : IHostedService, IOpenProjectSyncTrigger
{
    /// <summary>How long to wait before re-attempting a billed AI repository-match call for the
    /// same still-unconfirmed item, so an item nobody's resolved yet doesn't re-bill the AI
    /// provider on every single poll (every 15 minutes by default) indefinitely.</summary>
    private static readonly TimeSpan RepositoryMatchRetryCooldown = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private CancellationTokenSource? _stoppingCts;
    private Task? _loopTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stoppingCts?.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunNowAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // RunNowAsync itself shouldn't throw (see its own try/catch), but this is a
                // last-resort safety net - without it, any exception here would fault this loop's
                // task permanently, silently stopping the periodic sync until an app restart.
                logger.LogError(ex, "OpenProject sync loop iteration failed unexpectedly");
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.SyncIntervalMinutes));
            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Runs one sync pass immediately - used by both the periodic loop above and the
    /// Settings page's manual "Sync Now" button (via IOpenProjectSyncTrigger), so both paths share
    /// identical behavior. Never throws - every failure is logged and reflected in the returned
    /// OpenProjectSyncRun instead.</summary>
    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return;
        }

        // A manual "Sync Now" click overlapping the periodic timer's own in-flight run would
        // otherwise let both independently see "no existing WorkItem yet" for the same work
        // package and both create one - skip entirely rather than run two passes concurrently.
        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            logger.LogInformation("OpenProject sync already in progress - skipping this trigger");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var aiEngine = scope.ServiceProvider.GetRequiredService<IAiDecisionEngine>();
            var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

            var run = new OpenProjectSyncRun();

            try
            {
                var workPackages = await gateway.GetAssignedWorkPackagesAsync(cancellationToken);
                var knownRepositories = await db.Repositories.ToListAsync(cancellationToken);

                foreach (var wp in workPackages)
                {
                    try
                    {
                        var existing = await db.WorkItems.FirstOrDefaultAsync(
                            w => w.ExternalSystem == "OpenProject" && w.ExternalId == wp.Id.ToString(),
                            cancellationToken);

                        if (existing is null)
                        {
                            var item = WorkItem.CreateFromOpenProject(wp.Id.ToString(), wp.Subject);
                            await ApplyAsync(item, wp, knownRepositories, aiEngine, cancellationToken);
                            db.WorkItems.Add(item);
                            await db.SaveChangesAsync(cancellationToken);
                            await notifier.WorkItemChangedAsync(item);
                            run.ItemsCreated++;
                        }
                        else
                        {
                            await ApplyAsync(existing, wp, knownRepositories, aiEngine, cancellationToken);
                            await db.SaveChangesAsync(cancellationToken);
                            await notifier.WorkItemChangedAsync(existing);
                            run.ItemsUpdated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to sync OpenProject work package #{Id}", wp.Id);
                        run.ItemsFailed++;

                        // db is reused across every iteration of this loop - without clearing the
                        // tracker, the entity that just failed to save stays tracked as
                        // Added/Modified, and the NEXT item's SaveChangesAsync would try to flush
                        // it too, likely failing again and cascading one bad item's failure onto
                        // every item processed afterward in this same run.
                        db.ChangeTracker.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenProject sync run failed");
                run.ErrorMessage = ex.Message;
            }

            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            try
            {
                db.OpenProjectSyncRuns.Add(run);
                await db.SaveChangesAsync(cancellationToken);
                await notifier.OpenProjectSyncCompletedAsync(run);
            }
            catch (Exception ex)
            {
                // Recording the run's own result/notifying about it must never itself be able to
                // propagate out of RunNowAsync - see the class-level doc comment.
                logger.LogError(ex, "Failed to record the OpenProject sync run's result");
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>Maps every OpenProject-owned field onto item - see the class-level known
    /// limitation about this always overwriting local edits to those same fields.</summary>
    private async Task ApplyAsync(
        WorkItem item, OpenProjectWorkPackage wp, List<RepositoryProfile> knownRepositories, IAiDecisionEngine aiEngine, CancellationToken cancellationToken)
    {
        item.Title = wp.Subject;
        item.Description = wp.Description;
        item.Status = MapStatus(wp);
        item.DueAtUtc = wp.DueDate;
        item.PercentComplete = Math.Clamp(wp.PercentageDone, 0, 100);
        item.ExternalUrl = $"{options.Value.BaseUrl!.TrimEnd('/')}/work_packages/{wp.Id}";
        item.LastSyncedAtUtc = DateTimeOffset.UtcNow;
        item.SyncError = null;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Repository assignment stays whatever it was once the user (or a confident auto-match)
        // has confirmed it - re-matching every pull would fight a manual pick or a deliberate
        // "none applies" confirmation. While still unconfirmed, only retry the (billed) AI match
        // once the cooldown has elapsed, rather than on every single poll.
        var dueForRetry = item.LastRepositoryMatchAttemptAtUtc is null
            || DateTimeOffset.UtcNow - item.LastRepositoryMatchAttemptAtUtc.Value >= RepositoryMatchRetryCooldown;
        if (!item.RepositoryAssignmentConfirmed && dueForRetry)
        {
            await MatchRepositoryAsync(item, wp, knownRepositories, aiEngine, cancellationToken);
        }
    }

    private async Task MatchRepositoryAsync(
        WorkItem item, OpenProjectWorkPackage wp, List<RepositoryProfile> knownRepositories, IAiDecisionEngine aiEngine, CancellationToken cancellationToken)
    {
        var exact = knownRepositories.FirstOrDefault(r => string.Equals(r.Name, wp.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            item.RepositoryName = exact.Name;
            item.RepositoryAssignmentConfirmed = true;
            return;
        }

        if (knownRepositories.Count == 0)
        {
            return;
        }

        // Only stamp the cooldown once an actual (billed) AI call is about to be made - the exact
        // match / no-repositories-registered paths above cost nothing and don't need to wait.
        item.LastRepositoryMatchAttemptAtUtc = DateTimeOffset.UtcNow;

        RepositoryMatchPrompt.Result result;
        try
        {
            var prompt = RepositoryMatchPrompt.Build(wp.ProjectName, wp.Subject, knownRepositories);
            var raw = await aiEngine.GenerateTextAsync(prompt, cancellationToken);
            result = RepositoryMatchPrompt.Parse(raw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Repository-match AI call failed for OpenProject work package #{Id} - leaving unconfirmed", wp.Id);
            return;
        }

        var matched = knownRepositories.FirstOrDefault(r => string.Equals(r.Name, result.RepositoryName, StringComparison.OrdinalIgnoreCase));
        if (matched is not null && result.Confidence >= options.Value.RepositoryMatchConfidenceThreshold)
        {
            item.RepositoryName = matched.Name;
            item.RepositoryAssignmentConfirmed = true;
        }
        // Below threshold (or no match): leave RepositoryAssignmentConfirmed = false - the Tasks
        // board surfaces this as "needs a repository" until the user picks one or confirms none.
    }

    private static WorkItemStatus MapStatus(OpenProjectWorkPackage wp)
    {
        if (wp.StatusIsClosed)
        {
            return WorkItemStatus.Done;
        }

        var name = wp.StatusName ?? string.Empty;
        if (name.Contains("review", StringComparison.OrdinalIgnoreCase))
        {
            return WorkItemStatus.InReview;
        }
        if (name.Contains("progress", StringComparison.OrdinalIgnoreCase))
        {
            return WorkItemStatus.InProgress;
        }

        return WorkItemStatus.Todo;
    }
}
