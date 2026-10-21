namespace AiAutomations.Infrastructure.Ai;

public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public required string ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-5-5";
    public int MaxTokens { get; set; } = 1024;
}
