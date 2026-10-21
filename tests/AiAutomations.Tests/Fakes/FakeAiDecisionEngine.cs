using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;

namespace AiAutomations.Tests.Fakes;

public class FakeAiDecisionEngine : IAiDecisionEngine
{
    public Func<string, string>? GenerateTextResponder { get; set; }

    public Task<MessageDecision> ClassifyAsync(
        SlackMessage message, IReadOnlyList<SlackMessage> threadContext, IReadOnlyList<RepositoryProfile> knownRepositories, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not needed by OpenProject sync tests.");

    public Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(GenerateTextResponder?.Invoke(prompt) ?? """{"repositoryName": null, "confidence": 0}""");
}
