namespace AiAutomations.Core.Abstractions;

/// <summary>
/// Minimal seam for manually triggering an OpenProject sync pass "now" - lets callers outside
/// Infrastructure (e.g. TriageAppService's "Sync Now" button) depend on an abstraction instead of
/// the concrete OpenProjectSyncHostedService, consistent with every other cross-layer dependency
/// in this codebase (ISlackGateway, IActionExecutor, IOpenProjectGateway, etc.).
/// </summary>
public interface IOpenProjectSyncTrigger
{
    Task RunNowAsync(CancellationToken cancellationToken);
}
