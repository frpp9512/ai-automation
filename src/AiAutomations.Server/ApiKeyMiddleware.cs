namespace AiAutomations.Server;

/// <summary>
/// Minimal shared-secret check for remote clients. Pair with a network-level
/// restriction (e.g. only reachable over a Tailscale/VPN interface) - this
/// header check alone is not a substitute for that.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var expectedKey = configuration["ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            throw new InvalidOperationException("ApiKey is not configured. Set it in appsettings or user secrets before exposing this server remotely.");
        }

        // Header only - avoid ever putting the key in a URL/query string (logs, history, proxies).
        var providedKey = context.Request.Headers[HeaderName].FirstOrDefault();

        if (providedKey != expectedKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid API key.");
            return;
        }

        await next(context);
    }
}
