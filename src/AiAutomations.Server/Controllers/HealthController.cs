using AiAutomations.Core.Abstractions;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AiAutomations.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(AppDbContext db, ISlackGateway slackGateway, IConfiguration configuration, IPipelineActivityTracker activityTracker) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthStatusDto>> Get()
    {
        var databaseOk = await db.Database.CanConnectAsync();
        var provider = configuration["Ai:Provider"]?.Equals("Anthropic", StringComparison.OrdinalIgnoreCase) == true
            ? "Anthropic"
            : "OpenAiCompatible";
        var apiKey = provider == "Anthropic" ? configuration["Anthropic:ApiKey"] : configuration["OpenAiCompatible:ApiKey"];

        return Ok(new HealthStatusDto(
            DatabaseOk: databaseOk,
            SlackConnected: slackGateway.IsConnected,
            AiProviderConfigured: !string.IsNullOrWhiteSpace(apiKey),
            AiProvider: provider,
            PendingMessageCount: activityTracker.PendingCount));
    }
}
