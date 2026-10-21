using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Options;

namespace AiAutomations.Infrastructure.Ai;

/// <summary>IAiDecisionEngine over any OpenAI-compatible /chat/completions endpoint - see OpenAiCompatibleOptions.</summary>
public class OpenAiCompatibleDecisionEngine(HttpClient httpClient, IOptions<OpenAiCompatibleOptions> options, IAiUsageTracker usageTracker) : IAiDecisionEngine
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

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        request.Content = JsonContent.Create(new ChatRequest(
            opts.Model,
            [new OutgoingMessage("user", prompt)],
            opts.MaxTokens,
            new ResponseFormat("json_object")));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
                   ?? throw new InvalidOperationException("Empty response from the AI provider");
        var responseMessage = body.Choices.FirstOrDefault()?.Message
                               ?? throw new InvalidOperationException("No message in the AI provider's response");

        // Some models (notably reasoning models behind a free-tier router that can land on
        // any backing model) spend the whole token budget "thinking" and leave Content null,
        // with the actual answer only in Reasoning. Fall back to that rather than fail outright.
        var text = responseMessage.Content ?? responseMessage.Reasoning
                   ?? throw new InvalidOperationException("No usable content in the AI provider's response");

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

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        request.Content = JsonContent.Create(new PlainChatRequest(opts.Model, [new OutgoingMessage("user", prompt)], opts.MaxTokens));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
                   ?? throw new InvalidOperationException("Empty response from the AI provider");
        var responseMessage = body.Choices.FirstOrDefault()?.Message
                               ?? throw new InvalidOperationException("No message in the AI provider's response");

        await RecordUsageAsync("GenerateText", body.Usage, cancellationToken);

        return responseMessage.Content ?? responseMessage.Reasoning
               ?? throw new InvalidOperationException("No usable content in the AI provider's response");
    }

    private async Task RecordUsageAsync(string purpose, ChatUsage? usage, CancellationToken cancellationToken)
    {
        if (usage is null)
        {
            return;
        }

        await usageTracker.RecordAsync("OpenAiCompatible", options.Value.Model, purpose, usage.PromptTokens, usage.CompletionTokens, cancellationToken);
    }

    private record ChatRequest(
        string Model,
        OutgoingMessage[] Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat);
    private record PlainChatRequest(string Model, OutgoingMessage[] Messages, [property: JsonPropertyName("max_tokens")] int MaxTokens);
    private record OutgoingMessage(string Role, string Content);
    private record ResponseFormat(string Type);
    private record ChatResponse(List<ChatChoice> Choices, ChatUsage? Usage);
    private record ChatChoice(ResponseMessage Message);
    private record ResponseMessage(string? Content, string? Reasoning);
    private record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
