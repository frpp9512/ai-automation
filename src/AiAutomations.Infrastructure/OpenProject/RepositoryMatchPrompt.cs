using System.Text.Json;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Ai;

namespace AiAutomations.Infrastructure.OpenProject;

/// <summary>
/// Prompt/parsing for matching an OpenProject work package's project to a registered
/// RepositoryProfile with a confidence score, via IAiDecisionEngine.GenerateTextAsync - there's
/// no dedicated decision-engine method for this (and no new one was added) since GenerateTextAsync
/// already exists for exactly this kind of on-demand, non-ClassifyAsync-shaped helper call.
/// </summary>
internal static class RepositoryMatchPrompt
{
    public record Result(string? RepositoryName, int Confidence);

    public static string Build(string? projectName, string workPackageSubject, IReadOnlyList<RepositoryProfile> knownRepositories)
    {
        var repoBlock = AiPromptHelpers.BuildRepositoryListBlock(knownRepositories);

        return $$"""
            Match an OpenProject work package to the best-fitting registered repository below.
            Respond with ONLY this JSON shape, no prose:
            {"repositoryName": "best-matching repo name below, or null if none fit",
              "confidence": 0-100}

            Repos:
            {{repoBlock}}

            OpenProject project: {{projectName ?? "(none)"}}
            Work package subject: {{workPackageSubject}}
            """;
    }

    public static Result Parse(string rawResponseText)
    {
        try
        {
            var json = AiPromptHelpers.ExtractJson(rawResponseText);
            return JsonSerializer.Deserialize<Result>(json, AiPromptHelpers.CaseInsensitiveJsonOptions) ?? new Result(null, 0);
        }
        catch (JsonException)
        {
            // A weaker/free model occasionally ignores the "JSON only" instruction - treat as
            // "no confident match" rather than fail the whole sync run over a formatting miss.
            return new Result(null, 0);
        }
    }
}
