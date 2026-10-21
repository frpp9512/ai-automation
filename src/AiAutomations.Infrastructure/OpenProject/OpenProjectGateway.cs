using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using AiAutomations.Core.Abstractions;
using AiAutomations.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiAutomations.Infrastructure.OpenProject;

/// <summary>
/// IOpenProjectGateway over OpenProject's REST API v3 - Basic auth ("apikey:{token}"),
/// application/hal+json, hand-rolled 429/5xx retry (no Polly anywhere in this repo). Mirrors the
/// mechanics of this account's own X:\tools\report-scripts OpenProject scripts (OpTaskList.cs/
/// OpTaskUpdate.cs): filters-as-JSON-query-param for reads, journal-entry comments, and
/// lockVersion-gated PATCH for field edits.
/// </summary>
public class OpenProjectGateway(HttpClient httpClient, IOptions<OpenProjectOptions> options, ILogger<OpenProjectGateway> logger) : IOpenProjectGateway
{
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public async Task<IReadOnlyList<OpenProjectWorkPackage>> GetAssignedWorkPackagesAsync(CancellationToken cancellationToken)
    {
        var opts = RequireConfigured();

        var filters = new JsonArray
        {
            new JsonObject { ["assignee"] = new JsonObject { ["operator"] = "=", ["values"] = new JsonArray("me") } }
        };
        if (opts.StatusFilter is "open" or "closed")
        {
            filters.Add(new JsonObject { ["status"] = new JsonObject { ["operator"] = opts.StatusFilter == "open" ? "o" : "c", ["values"] = new JsonArray() } });
        }
        var filtersQuery = Uri.EscapeDataString(filters.ToJsonString());

        var results = new List<OpenProjectWorkPackage>();
        var seen = 0;
        for (var page = 1; ; page++)
        {
            var (status, body) = await SendAsync(
                HttpMethod.Get,
                opts,
                $"api/v3/work_packages?pageSize=200&offset={page}&sortBy=%5B%5B%22updatedAt%22%2C%22desc%22%5D%5D&filters={filtersQuery}",
                body: null,
                cancellationToken);

            if (!IsSuccess(status))
            {
                throw new InvalidOperationException($"OpenProject returned {(int)status} listing work packages: {body?["message"]}");
            }

            var elements = body?["_embedded"]?["elements"]?.AsArray() ?? [];
            foreach (var el in elements)
            {
                results.Add(MapWorkPackage(el!));
                seen++;
            }

            if (elements.Count == 0 || seen >= ((int?)body?["total"] ?? 0))
            {
                break;
            }
        }

        return results;
    }

    public async Task PostCommentAsync(int workPackageId, string comment, CancellationToken cancellationToken)
    {
        var opts = RequireConfigured();

        var (status, body) = await SendAsync(
            HttpMethod.Post,
            opts,
            $"api/v3/work_packages/{workPackageId}/activities",
            new JsonObject { ["comment"] = new JsonObject { ["raw"] = comment } },
            cancellationToken);

        if (!IsSuccess(status))
        {
            throw new InvalidOperationException($"OpenProject returned {(int)status} posting a comment on #{workPackageId}: {body?["message"]}");
        }
    }

    public async Task UpdatePercentageDoneAsync(int workPackageId, int percentageDone, CancellationToken cancellationToken)
    {
        var opts = RequireConfigured();

        var (getStatus, workPackage) = await SendAsync(HttpMethod.Get, opts, $"api/v3/work_packages/{workPackageId}", body: null, cancellationToken);
        if (!IsSuccess(getStatus))
        {
            throw new InvalidOperationException($"OpenProject returned {(int)getStatus} reading #{workPackageId} before updating it: {workPackage?["message"]}");
        }
        var lockVersion = (int?)workPackage!["lockVersion"] ?? 0;

        var (patchStatus, patchBody) = await SendAsync(
            HttpMethod.Patch,
            opts,
            $"api/v3/work_packages/{workPackageId}",
            new JsonObject { ["lockVersion"] = lockVersion, ["percentageDone"] = percentageDone },
            cancellationToken);

        if (!IsSuccess(patchStatus))
        {
            throw new InvalidOperationException($"OpenProject returned {(int)patchStatus} updating percentageDone on #{workPackageId}: {patchBody?["message"]}");
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return false;
        }

        try
        {
            var (status, _) = await SendAsync(HttpMethod.Get, options.Value, "api/v3/users/me", body: null, cancellationToken);
            return IsSuccess(status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenProject connection test failed");
            return false;
        }
    }

    private OpenProjectOptions RequireConfigured()
    {
        var opts = options.Value;
        if (!opts.IsConfigured)
        {
            throw new InvalidOperationException("OpenProject is not configured (missing BaseUrl/ApiToken).");
        }
        return opts;
    }

    private static OpenProjectWorkPackage MapWorkPackage(JsonNode el)
    {
        var spentHours = (string?)el["spentTime"] is { } spentIso ? (decimal)XmlConvert.ToTimeSpan(spentIso).TotalHours : 0m;
        decimal? estimatedHours = (string?)el["estimatedTime"] is { } estIso ? (decimal)XmlConvert.ToTimeSpan(estIso).TotalHours : null;
        var statusTitle = (string?)el["_links"]?["status"]?["title"];

        return new OpenProjectWorkPackage(
            Id: (int)el["id"]!,
            Subject: (string?)el["subject"] ?? "(untitled)",
            Description: (string?)el["description"]?["raw"],
            ProjectName: (string?)el["_links"]?["project"]?["title"],
            TypeName: (string?)el["_links"]?["type"]?["title"],
            StatusName: statusTitle,
            StatusIsClosed: (bool?)el["_links"]?["status"]?["isClosed"] ?? false,
            PercentageDone: (int?)el["percentageDone"] ?? 0,
            DueDate: (string?)el["dueDate"] is { Length: > 0 } due ? DateTimeOffset.Parse(due) : null,
            UpdatedAt: (string?)el["updatedAt"] is { } updated ? DateTimeOffset.Parse(updated) : DateTimeOffset.UtcNow,
            SpentHours: spentHours,
            EstimatedHours: estimatedHours);
    }

    private async Task<(HttpStatusCode Status, JsonNode? Body)> SendAsync(
        HttpMethod method, OpenProjectOptions opts, string relativePath, JsonNode? body, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(opts.BaseUrl!.TrimEnd('/') + "/"), relativePath);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"apikey:{opts.ApiToken}")));
                request.Headers.Accept.ParseAdd("application/hal+json");
                if (body is not null)
                {
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                _isConnected = true;

                JsonNode? node = null;
                try
                {
                    node = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
                }
                catch
                {
                    // Non-JSON body (rare, e.g. a gateway error page) - treated as an empty body below.
                }

                return (response.StatusCode, node);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _isConnected = false;
                throw;
            }
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;
}
