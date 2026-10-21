using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Options;

namespace AiAutomations.Infrastructure.Ai;

public class AnthropicDecisionEngine(HttpClient httpClient, IOptions<AnthropicOptions> options, IAiUsageTracker usageTracker) : IAiDecisionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<MessageDecision> ClassifyAsync(
        SlackMessage message,
        IReadOnlyList<SlackMessage> threadContext,
        IReadOnlyList<RepositoryProfile> knownRepositories,
        CancellationToken cancellationToken)
    {
        var opts = options.Value;
        var prompt = ClassificationPrompt.Build(message, threadContext, knownRepositories);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", opts.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new AnthropicRequest(
                opts.Model,
                opts.MaxTokens,
                [new AnthropicMessage("user", prompt)])),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, cancellationToken)
                   ?? throw new InvalidOperationException("Empty response from Anthropic API");
        var text = body.Content.FirstOrDefault(c => c.Type == "text")?.Text
                   ?? throw new InvalidOperationException("No text block in Anthropic response");

        await RecordUsageAsync("Classify", body.Usage, cancellationToken);

        var classification = ClassificationPrompt.Parse(text);

        return new MessageDecision
        {
            MessageId = message.Id,
            Intent = Enum.Parse<MessageIntent>(classification.Intent, ignoreCase: true),
            Urgency = Enum.Parse<Urgency>(classification.Urgency, ignoreCase: true),
            Summary = classification.Summary,
            SuggestedReply = classification.SuggestedReply,
            TaskDetails = classification.TaskDetails,
            SuggestedRepositoryName = classification.SuggestedRepository,
            Confidence = classification.Confidence,
            RawModelOutput = text
        };
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken)
    {
        var opts = options.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", opts.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new AnthropicRequest(opts.Model, opts.MaxTokens, [new AnthropicMessage("user", prompt)])),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(JsonOptions, cancellationToken)
                   ?? throw new InvalidOperationException("Empty response from Anthropic API");

        await RecordUsageAsync("GenerateText", body.Usage, cancellationToken);

        return body.Content.FirstOrDefault(c => c.Type == "text")?.Text
               ?? throw new InvalidOperationException("No text block in Anthropic response");
    }

    private async Task RecordUsageAsync(string purpose, AnthropicUsage? usage, CancellationToken cancellationToken)
    {
        if (usage is null)
        {
            return;
        }

        await usageTracker.RecordAsync("Anthropic", options.Value.Model, purpose, usage.InputTokens, usage.OutputTokens, cancellationToken);
    }

    private record AnthropicRequest(string Model, [property: JsonPropertyName("max_tokens")] int MaxTokens, AnthropicMessage[] Messages);
    private record AnthropicMessage(string Role, string Content);
    private record AnthropicResponse(List<AnthropicContentBlock> Content, AnthropicUsage? Usage);
    private record AnthropicContentBlock(string Type, string Text);
    private record AnthropicUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}
