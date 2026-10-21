using AiAutomations.Client.Web.Components;
using AiAutomations.Client.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ServerConnectionOptions>(builder.Configuration.GetSection(ServerConnectionOptions.SectionName));

builder.Services.AddHttpClient<ServerApiClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServerConnectionOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    // Backfill classifies every found message sequentially through the AI provider (often 5-15s
    // each), so the whole request can easily exceed the 100s HttpClient default and get aborted
    // mid-flight - which also cancels the in-flight work on the server side.
    http.Timeout = TimeSpan.FromMinutes(15);
});

builder.Services.AddSingleton<RealtimeClient>();

var app = builder.Build();

// Fire-and-forget: don't block Blazor startup if the server isn't up yet.
// WithAutomaticReconnect only covers drops after a successful start, so retry the first connect here too.
_ = Task.Run(async () =>
{
    var realtime = app.Services.GetRequiredService<RealtimeClient>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RealtimeClient.Startup");
    while (true)
    {
        try
        {
            await realtime.StartAsync();
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to server's realtime hub, retrying in 5s");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
