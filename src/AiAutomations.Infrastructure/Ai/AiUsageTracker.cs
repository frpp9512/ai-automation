using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace AiAutomations.Infrastructure.Ai;

/// <inheritdoc cref="IAiUsageTracker"/>
public class AiUsageTracker(AppDbContext db, ILogger<AiUsageTracker> logger) : IAiUsageTracker
{
    /// <summary>USD per 1M tokens (input, output), for models this app is actually configured to
    /// use. Matched by substring against the configured model name, since providers version
    /// their model strings (e.g. "claude-sonnet-4-5-20250929"). Anything unmatched (most
    /// OpenRouter free-tier models) gets no cost estimate rather than a guessed one.</summary>
    private static readonly (string ModelContains, decimal InputPerM, decimal OutputPerM)[] KnownPricing =
    [
        ("claude-opus", 15m, 75m),
        ("claude-sonnet", 3m, 15m),
        ("claude-haiku", 0.8m, 4m),
        ("claude-3-5-sonnet", 3m, 15m),
        ("claude-3-5-haiku", 0.8m, 4m),
        ("gpt-4o-mini", 0.15m, 0.6m),
        ("gpt-4o", 2.5m, 10m),
    ];

    public async Task RecordAsync(string provider, string model, string purpose, int inputTokens, int outputTokens, CancellationToken cancellationToken = default)
    {
        try
        {
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Provider = provider,
                Model = model,
                Purpose = purpose,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                EstimatedCostUsd = EstimateCost(model, inputTokens, outputTokens)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let usage tracking fail the AI call it's reporting on.
            logger.LogWarning(ex, "Failed to record AI usage for {Provider}/{Model}", provider, model);
        }
    }

    private static decimal? EstimateCost(string model, int inputTokens, int outputTokens)
    {
        var pricing = KnownPricing.FirstOrDefault(p => model.Contains(p.ModelContains, StringComparison.OrdinalIgnoreCase));
        if (pricing == default)
        {
            return null;
        }

        return inputTokens / 1_000_000m * pricing.InputPerM + outputTokens / 1_000_000m * pricing.OutputPerM;
    }
}
