using System.Net;
using System.Text;
using AiAutomations.Infrastructure.OpenProject;
using AiAutomations.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiAutomations.Tests;

/// <summary>
/// Exercises the REAL OpenProjectGateway's HTTP mechanics (auth header, pagination, retry,
/// IsConnected transitions) against a StubHttpMessageHandler, rather than FakeOpenProjectGateway -
/// nothing else in the suite touches this class at all. Two tests (the server-error retry and the
/// exhausted-retries one) incur the gateway's real hardcoded backoff delays (2s and 2s+4s), since
/// those delays aren't configurable/injectable - accepted here for genuine coverage of a
/// previously completely untested integration point.
/// </summary>
public class OpenProjectGatewayTests
{
    private static (OpenProjectGateway Gateway, StubHttpMessageHandler Handler) CreateGateway(OpenProjectOptions? options = null)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var gateway = new OpenProjectGateway(
            httpClient,
            Options.Create(options ?? new OpenProjectOptions { BaseUrl = "https://op.example.com", ApiToken = "secret-token" }),
            NullLogger<OpenProjectGateway>.Instance);
        return (gateway, handler);
    }

    private const string EmptyPage = """{"_embedded":{"elements":[]},"total":0}""";

    private static string PageJson(int total, params int[] ids)
    {
        // Built via plain concatenation rather than raw-string interpolation - a $$"""...}}}"""
        // literal can't cleanly represent this much nested JSON (the closing braces collide with
        // the $$ interpolation delimiter's own brace-counting).
        var elements = string.Join(",", ids.Select(id =>
            "{\"id\":" + id + ",\"subject\":\"Work package " + id + "\",\"percentageDone\":0," +
            "\"_links\":{\"status\":{\"title\":\"New\",\"isClosed\":false}}}"));
        return "{\"_embedded\":{\"elements\":[" + elements + "]},\"total\":" + total + "}";
    }

    [Fact]
    public async Task Sends_basic_auth_and_hal_json_headers()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueJson(HttpStatusCode.OK, EmptyPage);

        await gateway.GetAssignedWorkPackagesAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("apikey:secret-token")), request.Headers.Authorization?.Parameter);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/hal+json");
    }

    [Fact]
    public async Task Stops_after_a_single_page_when_total_is_reached()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueJson(HttpStatusCode.OK, PageJson(total: 2, [1, 2]));

        var result = await gateway.GetAssignedWorkPackagesAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Paginates_until_all_elements_are_seen()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueJson(HttpStatusCode.OK, PageJson(total: 2, [1]));
        handler.EnqueueJson(HttpStatusCode.OK, PageJson(total: 2, [2]));

        var result = await gateway.GetAssignedWorkPackagesAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("offset=1", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("offset=2", handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task Retries_on_a_server_error_then_succeeds()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueStatus(HttpStatusCode.ServiceUnavailable);
        handler.EnqueueJson(HttpStatusCode.OK, PageJson(total: 1, [1]));

        var result = await gateway.GetAssignedWorkPackagesAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(gateway.IsConnected);
    }

    [Fact]
    public async Task Sets_IsConnected_false_after_exhausting_retries_on_a_network_failure()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueJson(HttpStatusCode.OK, EmptyPage);
        await gateway.GetAssignedWorkPackagesAsync(CancellationToken.None);
        Assert.True(gateway.IsConnected);

        handler.EnqueueException(new HttpRequestException("simulated network failure"));
        handler.EnqueueException(new HttpRequestException("simulated network failure"));
        handler.EnqueueException(new HttpRequestException("simulated network failure"));

        await Assert.ThrowsAsync<HttpRequestException>(() => gateway.GetAssignedWorkPackagesAsync(CancellationToken.None));

        Assert.False(gateway.IsConnected);
    }

    [Fact]
    public async Task TestConnectionAsync_returns_false_without_any_call_when_not_configured()
    {
        var (gateway, handler) = CreateGateway(new OpenProjectOptions());

        var result = await gateway.TestConnectionAsync(CancellationToken.None);

        Assert.False(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TestConnectionAsync_returns_true_on_a_successful_call()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueJson(HttpStatusCode.OK, """{"id": 1}""");

        Assert.True(await gateway.TestConnectionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TestConnectionAsync_returns_false_instead_of_throwing_on_failure()
    {
        var (gateway, handler) = CreateGateway();
        handler.EnqueueException(new HttpRequestException("simulated"));
        handler.EnqueueException(new HttpRequestException("simulated"));
        handler.EnqueueException(new HttpRequestException("simulated"));

        Assert.False(await gateway.TestConnectionAsync(CancellationToken.None));
    }
}
