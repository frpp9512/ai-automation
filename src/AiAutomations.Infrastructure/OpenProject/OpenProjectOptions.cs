namespace AiAutomations.Infrastructure.OpenProject;

public class OpenProjectOptions
{
    public const string SectionName = "OpenProject";

    /// <summary>OpenProject instance base URL, e.g. "https://project.example.com/".</summary>
    public string? BaseUrl { get; set; }

    /// <summary>API token (Account settings -> Access tokens in OpenProject), sent as Basic auth "apikey:{token}".</summary>
    public string? ApiToken { get; set; }

    /// <summary>How often OpenProjectSyncHostedService pulls assigned work packages, in minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>"all" (default - nothing assigned to you is excluded by status, backlog
    /// included), "open", or "closed" - same semantics as the OpenProject API's own status filter.</summary>
    public string StatusFilter { get; set; } = "all";

    /// <summary>
    /// Minimum AI-match confidence (0-100) required to auto-assign a pulled work package's
    /// project to a registered RepositoryProfile without asking the user. Below this, the
    /// WorkItem is created/updated with RepositoryName left as-is and
    /// RepositoryAssignmentConfirmed = false, surfaced in the Tasks board for manual pick.
    /// </summary>
    public int RepositoryMatchConfidenceThreshold { get; set; } = 80;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiToken);
}
