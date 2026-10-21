using AiAutomations.Core.Abstractions;
using AiAutomations.Infrastructure.Ai;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.Hosting;
using AiAutomations.Infrastructure.OpenProject;
using AiAutomations.Infrastructure.Pipeline;
using AiAutomations.Infrastructure.Repositories;
using AiAutomations.Infrastructure.Sessions;
using AiAutomations.Infrastructure.Slack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        services.Configure<OpenProjectOptions>(configuration.GetSection(OpenProjectOptions.SectionName));

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

        // Registered via IHttpClientFactory (named client, not AddHttpClient<TClient,TImpl>,
        // which is transient by default) but kept as a singleton gateway instance - IsConnected
        // needs to persist across every caller (the sync loop, the health check, the Settings
        // page's "test connection" button), not reset on every DI resolution.
        services.AddHttpClient(nameof(OpenProjectGateway));
        services.AddSingleton<IOpenProjectGateway>(sp => new OpenProjectGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenProjectGateway)),
            sp.GetRequiredService<IOptions<OpenProjectOptions>>(),
            sp.GetRequiredService<ILogger<OpenProjectGateway>>()));

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

        // Also registered as itself (not just IHostedService) and as IOpenProjectSyncTrigger, so
        // TriageAppService's "Sync Now" button can call RunNowAsync() - through the interface,
        // not the concrete type - on the same running instance the host started.
        services.AddSingleton<OpenProjectSyncHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenProjectSyncHostedService>());
        services.AddSingleton<IOpenProjectSyncTrigger>(sp => sp.GetRequiredService<OpenProjectSyncHostedService>());

        return services;
    }
}
