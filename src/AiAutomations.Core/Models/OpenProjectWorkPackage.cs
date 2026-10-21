namespace AiAutomations.Core.Models;

/// <summary>
/// A work package as read from the OpenProject API - not persisted itself, just the shape
/// OpenProjectSyncHostedService maps into a WorkItem on pull.
/// </summary>
public record OpenProjectWorkPackage(
    int Id,
    string Subject,
    string? Description,
    string? ProjectName,
    string? TypeName,
    string? StatusName,
    bool StatusIsClosed,
    int PercentageDone,
    DateTimeOffset? DueDate,
    DateTimeOffset UpdatedAt,
    decimal SpentHours,
    decimal? EstimatedHours);
