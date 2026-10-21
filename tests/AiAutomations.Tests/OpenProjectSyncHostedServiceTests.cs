using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.OpenProject;
using AiAutomations.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiAutomations.Tests;

public class OpenProjectSyncHostedServiceTests
{
    private static OpenProjectWorkPackage WorkPackage(
        int id = 1, string subject = "Fix the bug", string? projectName = "Website", int percentageDone = 10) =>
        new(id, subject, "Description", projectName, "Task", "In progress", StatusIsClosed: false, percentageDone, DueDate: null, UpdatedAt: DateTimeOffset.UtcNow, SpentHours: 0, EstimatedHours: null);

    private sealed record Harness(IServiceScopeFactory ScopeFactory, FakeRealtimeNotifier Notifier, FakeAiDecisionEngine Ai, string DbName)
    {
        public AppDbContext NewDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(DbName).Options);
    }

    private static Harness BuildHarness()
    {
        var dbName = Guid.NewGuid().ToString();
        var ai = new FakeAiDecisionEngine();
        var notifier = new FakeRealtimeNotifier();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<AiAutomations.Core.Abstractions.IAiDecisionEngine>(ai);
        services.AddSingleton<AiAutomations.Core.Abstractions.IRealtimeNotifier>(notifier);
        var provider = services.BuildServiceProvider();

        return new Harness(provider.GetRequiredService<IServiceScopeFactory>(), notifier, ai, dbName);
    }

    private static OpenProjectSyncHostedService CreateService(Harness harness, FakeOpenProjectGateway gateway, OpenProjectOptions? options = null) =>
        new(gateway, harness.ScopeFactory, Options.Create(options ?? new OpenProjectOptions { BaseUrl = "https://op.example.com", ApiToken = "token" }),
            NullLogger<OpenProjectSyncHostedService>.Instance);

    [Fact]
    public async Task Creates_a_new_work_item_for_an_unseen_work_package()
    {
        var harness = BuildHarness();
        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(id: 42)] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Equal("OpenProject", item.ExternalSystem);
        Assert.Equal("42", item.ExternalId);
        Assert.Equal("Fix the bug", item.Title);

        var run = Assert.Single(db.OpenProjectSyncRuns);
        Assert.Equal(1, run.ItemsCreated);
        Assert.Equal(0, run.ItemsUpdated);
        Assert.Single(harness.Notifier.OpenProjectSyncRuns);
    }

    [Fact]
    public async Task Updates_an_already_linked_work_item_instead_of_duplicating_it()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.WorkItems.Add(new WorkItem { Title = "Old title", ExternalSystem = "OpenProject", ExternalId = "42", PercentComplete = 0 });
            await seedDb.SaveChangesAsync();
        }

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(id: 42, subject: "New title", percentageDone: 75)] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Equal("New title", item.Title);
        Assert.Equal(75, item.PercentComplete);

        var run = Assert.Single(db.OpenProjectSyncRuns);
        Assert.Equal(0, run.ItemsCreated);
        Assert.Equal(1, run.ItemsUpdated);
    }

    [Fact]
    public async Task Auto_assigns_repository_on_exact_project_name_match_without_calling_ai()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.Repositories.Add(new RepositoryProfile { Name = "Website", Path = "C:/repos/website" });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => throw new InvalidOperationException("AI should not be called for an exact match.");

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(projectName: "Website")] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Equal("Website", item.RepositoryName);
        Assert.True(item.RepositoryAssignmentConfirmed);
    }

    [Fact]
    public async Task Auto_assigns_repository_when_ai_confidence_meets_the_threshold()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.Repositories.Add(new RepositoryProfile { Name = "Backend", Path = "C:/repos/backend" });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => """{"repositoryName": "Backend", "confidence": 90}""";

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(projectName: "Internal API")] };
        var service = CreateService(harness, gateway, new OpenProjectOptions { BaseUrl = "https://op.example.com", ApiToken = "token", RepositoryMatchConfidenceThreshold = 80 });

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Equal("Backend", item.RepositoryName);
        Assert.True(item.RepositoryAssignmentConfirmed);
    }

    [Fact]
    public async Task Leaves_repository_unconfirmed_when_ai_confidence_is_below_the_threshold()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.Repositories.Add(new RepositoryProfile { Name = "Backend", Path = "C:/repos/backend" });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => """{"repositoryName": "Backend", "confidence": 50}""";

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(projectName: "Internal API")] };
        var service = CreateService(harness, gateway, new OpenProjectOptions { BaseUrl = "https://op.example.com", ApiToken = "token", RepositoryMatchConfidenceThreshold = 80 });

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Null(item.RepositoryName);
        Assert.False(item.RepositoryAssignmentConfirmed);
    }

    [Fact]
    public async Task Does_not_re_match_a_repository_once_already_confirmed()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.WorkItems.Add(new WorkItem
            {
                Title = "Old title",
                ExternalSystem = "OpenProject",
                ExternalId = "42",
                RepositoryName = "Keep-me",
                RepositoryAssignmentConfirmed = true
            });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => throw new InvalidOperationException("AI should not be called once confirmed.");

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(id: 42, projectName: "Something else entirely")] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.Equal("Keep-me", item.RepositoryName);
    }

    [Fact]
    public async Task Records_a_failed_run_without_throwing_when_the_gateway_fails()
    {
        var harness = BuildHarness();
        var gateway = new FakeOpenProjectGateway { ThrowOnGetAssigned = true };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var run = Assert.Single(db.OpenProjectSyncRuns);
        Assert.NotNull(run.ErrorMessage);
        Assert.Empty(db.WorkItems);
    }

    [Fact]
    public async Task Does_nothing_when_openproject_is_not_configured()
    {
        var harness = BuildHarness();
        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage()] };
        var service = CreateService(harness, gateway, new OpenProjectOptions());

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        Assert.Empty(db.WorkItems);
        Assert.Empty(db.OpenProjectSyncRuns);
    }

    [Fact]
    public async Task Does_not_re_attempt_repository_match_within_the_cooldown_window()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.Repositories.Add(new RepositoryProfile { Name = "Backend", Path = "C:/repos/backend" });
            seedDb.WorkItems.Add(new WorkItem
            {
                Title = "Old title",
                ExternalSystem = "OpenProject",
                ExternalId = "42",
                RepositoryAssignmentConfirmed = false,
                LastRepositoryMatchAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => throw new InvalidOperationException("AI should not be called within the cooldown window.");

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(id: 42, projectName: "Something unmatched")] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.False(item.RepositoryAssignmentConfirmed);
        Assert.Null(item.RepositoryName);
    }

    [Fact]
    public async Task Re_attempts_repository_match_once_the_cooldown_has_elapsed()
    {
        var harness = BuildHarness();
        using (var seedDb = harness.NewDbContext())
        {
            seedDb.Repositories.Add(new RepositoryProfile { Name = "Backend", Path = "C:/repos/backend" });
            seedDb.WorkItems.Add(new WorkItem
            {
                Title = "Old title",
                ExternalSystem = "OpenProject",
                ExternalId = "42",
                RepositoryAssignmentConfirmed = false,
                LastRepositoryMatchAttemptAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
            });
            await seedDb.SaveChangesAsync();
        }
        harness.Ai.GenerateTextResponder = _ => """{"repositoryName": "Backend", "confidence": 90}""";

        var gateway = new FakeOpenProjectGateway { WorkPackages = [WorkPackage(id: 42, projectName: "Something unmatched")] };
        var service = CreateService(harness, gateway);

        await service.RunNowAsync(CancellationToken.None);

        using var db = harness.NewDbContext();
        var item = Assert.Single(db.WorkItems);
        Assert.True(item.RepositoryAssignmentConfirmed);
        Assert.Equal("Backend", item.RepositoryName);
    }

    [Fact]
    public async Task Skips_a_concurrent_sync_run_instead_of_racing_it()
    {
        var harness = BuildHarness();
        var gate = new TaskCompletionSource();
        var gateway = new FakeOpenProjectGateway
        {
            WorkPackages = [WorkPackage(id: 42)],
            OnGetAssignedWorkPackages = () => gate.Task
        };
        var service = CreateService(harness, gateway);

        // Starts RunNowAsync; execution runs synchronously up to the (still-pending) gate, so by
        // the time this line returns, the first call already holds the re-entrancy semaphore.
        var firstRun = service.RunNowAsync(CancellationToken.None);

        // A second call made while the first is still in flight must see the semaphore held and
        // return immediately, without ever calling the gateway.
        await service.RunNowAsync(CancellationToken.None);

        gate.SetResult();
        await firstRun;

        using var db = harness.NewDbContext();
        Assert.Single(db.WorkItems);
        Assert.Single(db.OpenProjectSyncRuns);
        Assert.Equal(1, gateway.CallCount);
    }
}
