using System.Text.Json;
using AiAutomations.Core.Models;

namespace AiAutomations.Infrastructure.Ai;

/// <summary>Shared prompt-building/parsing logic used by both ClassificationPrompt and
/// RepositoryMatchPrompt - kept here once rather than duplicated in each.</summary>
internal static class AiPromptHelpers
{
    public static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Formats the known-repository registry for a prompt - "(none registered)" when empty.</summary>
    public static string BuildRepositoryListBlock(IReadOnlyList<RepositoryProfile> knownRepositories) =>
        knownRepositories.Count == 0
            ? "(none registered)"
            : string.Join('\n', knownRepositories.Select(r => $"- {r.Name}: {r.Keywords ?? "(no keywords)"}"));

    /// <summary>
    /// Extracts the first {...} JSON object from a model's raw text response. Free/weaker models
    /// occasionally ignore a "JSON only" instruction and answer in prose instead - this tolerates
    /// that rather than failing outright on well-formed-but-wrapped JSON.
    /// </summary>
    public static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
