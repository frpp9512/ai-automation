namespace AiAutomations.Core.Models;

/// <summary>
/// A repository the user maintains, registered so the classifier can suggest which one a
/// task belongs to. Keywords are free text (comma-separated) describing what the repo is
/// about - included in the classification prompt alongside Name.
/// </summary>
public class RepositoryProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Name { get; set; }
    public required string Path { get; set; }
    public string? Keywords { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
