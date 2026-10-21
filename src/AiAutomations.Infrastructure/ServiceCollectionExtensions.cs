using AiAutomations.Core.Abstractions;
using AiAutomations.Infrastructure.Ai;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.Hosting;
using AiAutomations.Infrastructure.Pipeline;
using AiAutomations.Infrastructure.Repositories;
using AiAutomations.Infrastructure.Sessions;
using AiAutomations.Infrastructure.Slack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAutomations.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(configuration.GetConnectionString("Default") ?? "Data Source=aiautomations.db"));

        services.Configure<SlackOptions>(configuration.GetSection(SlackOptions.SectionName));
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));
        services.Configure<OpenAiCompatibleOptions>(configuration.GetSection(OpenAiCompatibleOptions.SectionName));
        services.Configure<ClaudeSessionOptions>(configuration.GetSection(ClaudeSessionOptions.SectionName));

        // Ai:Provider picks which IAiDecisionEngine backs classification - "OpenAiCompatible"
        // (default, free - see OpenAiCompatibleOptions for the current base URL/model) or
        // "Anthropic". Both speak the same Core abstraction, so swapping is just config.
        if (string.Equals(configuration["Ai:Provider"], "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IAiDecisionEngine, AnthropicDecisionEngine>();
        }
        else
        {
            services.AddHttpClient<IAiDecisionEngine, OpenAiCompatibleDecisionEngine>();
        }
        services.AddSingleton<ISlackGateway, SlackGateway>();
        services.AddSingleton<IRepositoryInspector, RepositoryInspector>();
        services.AddSingleton<IClaudeSessionLauncher, ClaudeCodeSessionLauncher>();
        services.AddSingleton<IMessageAggregationBuffer, MessageAggregationBuffer>();
        services.AddSingleton<IPipelineActivityTracker, PipelineActivityTracker>();
        services.AddScoped<IAiUsageTracker, AiUsageTracker>();
        services.AddScoped<IMessageIngestPipeline, MessageIngestPipeline>();
        services.AddScoped<IActionExecutor, ActionExecutor>();
        services.AddScoped<IBackfillRunner, BackfillRunner>();

        // Portable IHostedService implementations (no ASP.NET dependency) - run the same
        // way under any Microsoft.Extensions.Hosting host, ASP.NET or otherwise.
        services.AddHostedService<SlackIngestHostedService>();
        services.AddHostedService<ClaudeSessionStatusHostedService>();

        return services;
    }
}
