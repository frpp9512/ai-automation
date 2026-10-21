using System.Net.Http.Json;
using AiAutomations.Core.Models;
using AiAutomations.Shared;

namespace AiAutomations.Client.Web.Services;

/// <summary>Thin wrapper over the server's REST API. One instance per Blazor circuit.</summary>
public class ServerApiClient(HttpClient httpClient)
{
    public Task<List<SlackMessageDto>?> GetRecentMessagesAsync(int take = 50, CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<SlackMessageDto>>($"api/messages?take={take}", ct);

    public Task<List<MessageDecisionDto>?> GetRecentDecisionsAsync(int take = 50, CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<MessageDecisionDto>>($"api/decisions?take={take}", ct);

    public Task<List<ActionItemDto>?> GetPendingActionsAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<ActionItemDto>>("api/actions", ct);

    public Task<List<ClaudeSessionDto>?> GetRecentSessionsAsync(int take = 50, CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<ClaudeSessionDto>>($"api/sessions?take={take}", ct);

    public async Task<ActionItemDto?> ApproveActionAsync(Guid actionId, string? editedPayload = null, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/actions/{actionId}/approve", new ApproveActionRequest(editedPayload), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActionItemDto>(cancellationToken: ct);
    }

    public async Task<ActionItemDto?> RejectActionAsync(Guid actionId, string? reason = null, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/actions/{actionId}/reject", new RejectActionRequest(reason), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActionItemDto>(cancellationToken: ct);
    }

    public async Task<BackfillResultDto?> BackfillAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/messages/backfill", new BackfillRequest(from, to), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BackfillResultDto>(cancellationToken: ct);
    }

    public Task<HealthStatusDto?> GetHealthAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<HealthStatusDto>("api/health", ct);

    public Task<List<RepositoryProfileDto>?> GetRepositoriesAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<RepositoryProfileDto>>("api/repositories", ct);

    public async Task<RepositoryProfileDto?> CreateRepositoryAsync(string name, string path, string? keywords, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/repositories", new UpsertRepositoryRequest(name, path, keywords), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryProfileDto>(cancellationToken: ct);
    }

    public async Task DeleteRepositoryAsync(Guid id, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"api/repositories/{id}", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<WorkItemDto>?> GetWorkItemsAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<List<WorkItemDto>>("api/workitems", ct);

    public async Task<WorkItemDto?> CreateWorkItemAsync(UpsertWorkItemRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/workitems", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkItemDto>(cancellationToken: ct);
    }

    public async Task<WorkItemDto?> UpdateWorkItemStatusAsync(Guid id, WorkItemStatus status, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync($"api/workitems/{id}/status", new UpdateWorkItemStatusRequest(status), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WorkItemDto>(cancellationToken: ct);
    }

    public async Task DeleteWorkItemAsync(Guid id, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"api/workitems/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}
