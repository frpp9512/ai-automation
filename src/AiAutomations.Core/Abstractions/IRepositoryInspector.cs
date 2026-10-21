namespace AiAutomations.Core.Abstractions;

/// <summary>
/// Pulls a bounded amount of real technical context out of a local repository checkout, to
/// ground AI-generated text (e.g. an improved task description) in actual facts about the
/// codebase rather than guesses from just a title and a keywords string.
/// </summary>
public interface IRepositoryInspector
{
    /// <summary>Null if repoPath doesn't exist or isn't accessible.</summary>
    Task<string?> GetTechnicalContextAsync(string repoPath, CancellationToken cancellationToken);
}
