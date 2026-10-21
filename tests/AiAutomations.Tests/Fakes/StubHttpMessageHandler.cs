using System.Net;
using System.Text;

namespace AiAutomations.Tests.Fakes;

/// <summary>
/// Test double standing in for the real network - queues canned responses (or exceptions) per
/// call and records every request sent, so a gateway's actual HttpClient-based mechanics (auth
/// headers, pagination, retry) can be exercised without a real network call.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responders.Enqueue(responder);

    public void EnqueueJson(HttpStatusCode status, string json) =>
        Enqueue(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public void EnqueueStatus(HttpStatusCode status) => Enqueue(_ => new HttpResponseMessage(status));

    public void EnqueueException(Exception exception) => Enqueue(_ => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException("No more stub responses queued.");
        }

        return _responders.Dequeue()(request);
    }
}
