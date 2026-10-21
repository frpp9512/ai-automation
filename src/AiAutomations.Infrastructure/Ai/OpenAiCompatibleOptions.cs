namespace AiAutomations.Infrastructure.Ai;

/// <summary>
/// Config for any OpenAI-compatible chat completions endpoint - OpenRouter's free-model
/// router (openrouter.ai, randomly picks from currently-free models), xAI's Grok (api.x.ai),
/// Groq (api.groq.com/openai), or a local Ollama all speak the same shape, so switching
/// providers is just changing these three values. GitHub Models (models.github.ai) was
/// fully retired on 2026-07-30 - don't point this at it.
/// </summary>
public class OpenAiCompatibleOptions
{
    public const string SectionName = "OpenAiCompatible";

    public required string ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "openrouter/free";
    // Generous: some free-tier models route to a reasoning model that spends a chunk of
    // the budget "thinking" before the final answer, so a small limit can cut it off.
    public int MaxTokens { get; set; } = 2048;
}
