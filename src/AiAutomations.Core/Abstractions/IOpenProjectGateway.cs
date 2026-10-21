using AiAutomations.Core.Models;

namespace AiAutomations.Core.Abstractions;

/// <summary>Wraps all OpenProject REST API v3 interaction behind one seam, mirroring ISlackGateway.</summary>
public interface IOpenProjectGateway
{
    /// <summary>
    /// True once a sync attempt has succeeded at least once since the app started - used for the
    /// health strip, same role as ISlackGateway.IsConnected. Reflects the result of the most
    /// recent sync attempt rather than a live ping, so reading it never makes a network call.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>Work packages assigned to the configured account, filtered by OpenProjectOptions.StatusFilter.</summary>
    Task<IReadOnlyList<OpenProjectWorkPackage>> GetAssignedWorkPackagesAsync(CancellationToken cancellationToken);

    /// <summary>Posts a comment as a work package journal entry - never conflicts with a concurrent edit.</summary>
    Task PostCommentAsync(int workPackageId, string comment, CancellationToken cancellationToken);

    /// <summary>Updates percentageDone, fetching the work package's current lockVersion
    /// immediately before the write for optimistic-locking safety.</summary>
    Task UpdatePercentageDoneAsync(int workPackageId, int percentageDone, CancellationToken cancellationToken);

    /// <summary>Live connectivity probe for the Settings page's "Test connection" button -
    /// unlike IsConnected, this always makes a real API call.</summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken);
}
