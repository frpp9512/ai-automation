using AiAutomations.Core.Abstractions;

namespace AiAutomations.Infrastructure.Pipeline;

public class BackfillRunner(ISlackGateway slackGateway, IMessageIngestPipeline pipeline) : IBackfillRunner
{
    public async Task<BackfillOutcome> RunAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var messages = await slackGateway.SearchMessagesInRangeAsync(from, to, cancellationToken);

        var newCount = 0;
        foreach (var message in messages)
        {
            var decision = await pipeline.IngestAsync(message, cancellationToken);
            if (decision is not null)
            {
                newCount++;
            }
        }

        return new BackfillOutcome(messages.Count, newCount, messages.Count - newCount);
    }
}
