using System.Text.Json;
using AiAutomations.Core.Models;

namespace AiAutomations.Infrastructure.Ai;

/// <summary>Prompt text and response parsing shared by every IAiDecisionEngine implementation.</summary>
internal static class ClassificationPrompt
{
    // Keeps the prompt (and therefore input token cost) bounded regardless of how long a
    // message, a context line, or the registry's keyword list happens to be.
    private const int MaxMessageChars = 1500;
    private const int MaxContextLineChars = 250;

    public record Result(
        string Intent,
        string Urgency,
        string Summary,
        string? SuggestedReply,
        string? TaskDetails,
        string? SuggestedRepository,
        double Confidence);

    public static string Build(SlackMessage message, IReadOnlyList<SlackMessage> threadContext, IReadOnlyList<RepositoryProfile> knownRepositories)
    {
        var contextBlock = threadContext.Count == 0
            ? "(none)"
            : string.Join('\n', threadContext.Select(m => $"- {DisplayLabel(m)}: {Truncate(m.Text, MaxContextLineChars)}"));

        var repoBlock = AiPromptHelpers.BuildRepositoryListBlock(knownRepositories);

        return $$"""
            Triage this Slack message for a software engineer. Respond with ONLY this JSON shape, no prose:
            {"intent": "FyiOnly|ReplyNeeded|TaskNeeded|ImplementationNeeded|PlanningNeeded",
              "urgency": "Low|Medium|High",
              "summary": "one sentence",
              "suggestedReply": "drafted reply, or null",
              "taskDetails": "context + suggested approach, or null if intent is FyiOnly/ReplyNeeded",
              "suggestedRepository": "best-matching repo name below, or null if unclear",
              "confidence": 0.0-1.0}

            If you lack enough detail for a confident taskDetails or suggestedRepository, set it null and lower
            confidence rather than guessing - a human will fill in what's missing.

            Repos:
            {{repoBlock}}

            Context:
            {{contextBlock}}

            Message ({{DisplayLabel(message)}}): {{Truncate(message.Text, MaxMessageChars)}}
            """;
    }

    /// <summary>The AI's summary/taskDetails text tends to echo back whatever identifies the
    /// sender in the prompt verbatim - using the resolved display name here (falling back to the
    /// raw Slack ID only when no name could be resolved) means that text reads naturally instead
    /// of surfacing a bare user ID throughout the UI.</summary>
    private static string DisplayLabel(SlackMessage m) => string.IsNullOrWhiteSpace(m.SlackUserName) ? m.SlackUserId : m.SlackUserName;

    /// <summary>
    /// Parses a model's raw text response into a Result. Free/weaker models occasionally ignore
    /// the "JSON only" instruction and answer in prose instead - on failure, the raw response
    /// (truncated) is included in the exception so that's diagnosable without reproducing it.
    /// </summary>
    public static Result Parse(string rawResponseText)
    {
        try
        {
            var json = AiPromptHelpers.ExtractJson(rawResponseText);
            return JsonSerializer.Deserialize<Result>(json, AiPromptHelpers.CaseInsensitiveJsonOptions)
                   ?? throw new InvalidOperationException("Could not parse classification JSON (null result)");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Could not parse classification JSON. Raw response: {Truncate(rawResponseText, 500)}", ex);
        }
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
