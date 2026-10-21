using AiAutomations.Infrastructure;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Core.Abstractions;
using AiAutomations.Server;
using AiAutomations.Server.Hubs;
using AiAutomations.Server.Realtime;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // TODO: switch to migrations (dotnet ef migrations add Initial -p src/AiAutomations.Infrastructure -s src/AiAutomations.Server)
    // once the schema settles. EnsureCreated is just enough to get the scaffold running.
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await DatabaseSchemaUpgrader.ApplyAsync(db);
    await DefaultRepositorySeed.EnsureSeededAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// No HTTPS redirect here: this service is meant to sit behind Tailscale/VPN (which already
// encrypts the link) rather than manage its own TLS cert for a personal always-on service.
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<InboxHub>("/hubs/inbox");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
