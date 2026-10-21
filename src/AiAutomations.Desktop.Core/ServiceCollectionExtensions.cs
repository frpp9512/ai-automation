using AiAutomations.Core.Abstractions;
using AiAutomations.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiAutomations.Desktop.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything the desktop app needs to run the same Slack listener + AI pipeline as
    /// the server, in-process: AddInfrastructure (DB, Slack gateway, AI engine, pipeline,
    /// the two hosted services) plus the desktop-specific notifier and app service.
    /// </summary>
    public static IServiceCollection AddDesktopHost(this IServiceCollection services, IConfiguration configuration)
    {
        // Registered explicitly (not just relied on via HostApplicationBuilder) so
        // IConfiguration resolves correctly from any composition root, not only App.xaml.cs's.
        services.AddSingleton(configuration);
        services.AddInfrastructure(configuration);
        services.AddSingleton<DesktopRealtimeNotifier>();
        services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<DesktopRealtimeNotifier>());
        services.AddSingleton<TriageAppService>();

        return services;
    }
}
