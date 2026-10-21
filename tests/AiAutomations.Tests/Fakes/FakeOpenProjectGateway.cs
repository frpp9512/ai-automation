using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;

namespace AiAutomations.Tests.Fakes;

public class FakeOpenProjectGateway : IOpenProjectGateway
{
    public List<OpenProjectWorkPackage> WorkPackages { get; set; } = [];
    public List<(int WorkPackageId, string Comment)> PostedComments { get; } = [];
    public List<(int WorkPackageId, int Percentage)> PostedPercentages { get; } = [];
    public bool IsConnected { get; set; } = true;
    public bool ThrowOnGetAssigned { get; set; }
    public int CallCount { get; private set; }

    /// <summary>Awaited before returning, if set - lets a test hold a call "in flight" (e.g. via
    /// a TaskCompletionSource) to deterministically create overlap between two concurrent
    /// RunNowAsync calls.</summary>
    public Func<Task>? OnGetAssignedWorkPackages { get; set; }

    public async Task<IReadOnlyList<OpenProjectWorkPackage>> GetAssignedWorkPackagesAsync(CancellationToken cancellationToken)
    {
        CallCount++;

        if (OnGetAssignedWorkPackages is { } hook)
        {
            await hook();
        }

        if (ThrowOnGetAssigned)
        {
            throw new InvalidOperationException("Simulated OpenProject API failure.");
        }

        return WorkPackages;
    }

    public Task PostCommentAsync(int workPackageId, string comment, CancellationToken cancellationToken)
    {
        PostedComments.Add((workPackageId, comment));
        return Task.CompletedTask;
    }

    public Task UpdatePercentageDoneAsync(int workPackageId, int percentageDone, CancellationToken cancellationToken)
    {
        PostedPercentages.Add((workPackageId, percentageDone));
        return Task.CompletedTask;
    }

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(IsConnected);
}
