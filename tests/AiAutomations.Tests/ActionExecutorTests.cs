using AiAutomations.Core.Models;
using AiAutomations.Infrastructure.Data;
using AiAutomations.Infrastructure.Pipeline;
using AiAutomations.Infrastructure.Sessions;
using AiAutomations.Shared;
using AiAutomations.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace AiAutomations.Tests;

public class ActionExecutorTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ActionExecutor CreateExecutor(
        AppDbContext db,
        FakeSlackGateway slack,
        FakeClaudeSessionLauncher launcher,
        FakeRealtimeNotifier notifier,
        bool autoLaunch = false) =>
        new(db, slack, launcher, notifier, Options.Create(new ClaudeSessionOptions { AutoLaunch = autoLaunch }), NullLogger<ActionExecutor>.Instance);

    private static (SlackMessage Message, MessageDecision Decision) SeedMessageAndDecision(AppDbContext db, string? threadTs = null)
    {
        var message = new SlackMessage
        {
            SlackChannelId = "C123",
            SlackUserId = "U456",
            SlackTs = "100.001",
            ThreadTs = threadTs,
            Text = "can you look at this?"
        };
        var decision = new MessageDecision
        {
            MessageId = message.Id,
            Intent = MessageIntent.ReplyNeeded,
            Urgency = Urgency.Medium,
            Summary = "needs a reply"
        };
        db.Messages.Add(message);
        db.Decisions.Add(decision);
        db.SaveChanges();
        return (message, decision);
    }

    [Fact]
    public async Task Reply_action_sends_via_slack_gateway_and_is_marked_executed()
    {
        using var db = NewDb();
        var (message, decision) = SeedMessageAndDecision(db);
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.Reply, Payload = "On it!" };
        db.Actions.Add(action);
        db.SaveChanges();

        var slack = new FakeSlackGateway();
        var executor = CreateExecutor(db, slack, new FakeClaudeSessionLauncher(), new FakeRealtimeNotifier());

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Executed, action.Status);
        var sent = Assert.Single(slack.SentReplies);
        Assert.Equal(message.SlackChannelId, sent.ChannelId);
        Assert.Equal(message.SlackTs, sent.ThreadTs); // no existing thread -> replies anchor on the original message
        Assert.Equal("On it!", sent.Text);
    }

    [Fact]
    public async Task Reply_action_replies_in_existing_thread_when_present()
    {
        using var db = NewDb();
        var (message, decision) = SeedMessageAndDecision(db, threadTs: "90.000");
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.Reply, Payload = "On it!" };
        db.Actions.Add(action);
        db.SaveChanges();

        var slack = new FakeSlackGateway();
        var executor = CreateExecutor(db, slack, new FakeClaudeSessionLauncher(), new FakeRealtimeNotifier());

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal("90.000", Assert.Single(slack.SentReplies).ThreadTs);
    }

    [Fact]
    public async Task Reply_action_with_no_payload_is_marked_failed_and_sends_nothing()
    {
        using var db = NewDb();
        var (_, decision) = SeedMessageAndDecision(db);
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.Reply, Payload = null };
        db.Actions.Add(action);
        db.SaveChanges();

        var slack = new FakeSlackGateway();
        var executor = CreateExecutor(db, slack, new FakeClaudeSessionLauncher(), new FakeRealtimeNotifier());

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Empty(slack.SentReplies);
    }

    [Fact]
    public async Task TaskBrief_action_without_autolaunch_creates_a_work_item_without_starting_a_session()
    {
        using var db = NewDb();
        var (_, decision) = SeedMessageAndDecision(db);
        // No RepoPath yet (AI found no confident match) - should NOT block finalizing the brief,
        // since without AutoLaunch nothing is actually launched.
        var payload = JsonSerializer.Serialize(new TaskBriefPayload(RepoName: null, RepoPath: null, Prompt: "plan the fix", Brief: "## plan the fix"));
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.TaskBrief, Payload = payload };
        db.Actions.Add(action);
        db.SaveChanges();

        var launcher = new FakeClaudeSessionLauncher();
        var notifier = new FakeRealtimeNotifier();
        var executor = CreateExecutor(db, new FakeSlackGateway(), launcher, notifier, autoLaunch: false);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Executed, action.Status);
        Assert.Empty(launcher.StartedSessions);
        Assert.Empty(db.Sessions);

        var workItem = Assert.Single(db.WorkItems);
        Assert.Equal(decision.Summary, workItem.Title);
        Assert.Equal("## plan the fix", workItem.Description);
        Assert.Equal(decision.Id, workItem.SourceDecisionId);
        Assert.Single(notifier.WorkItemChanges);
    }

    [Fact]
    public async Task TaskBrief_action_with_autolaunch_and_no_repo_path_is_marked_failed()
    {
        using var db = NewDb();
        var (_, decision) = SeedMessageAndDecision(db);
        var payload = JsonSerializer.Serialize(new TaskBriefPayload(RepoName: null, RepoPath: null, Prompt: "plan the fix", Brief: "## plan the fix"));
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.TaskBrief, Payload = payload };
        db.Actions.Add(action);
        db.SaveChanges();

        var launcher = new FakeClaudeSessionLauncher();
        var executor = CreateExecutor(db, new FakeSlackGateway(), launcher, new FakeRealtimeNotifier(), autoLaunch: true);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Empty(launcher.StartedSessions);
    }

    [Fact]
    public async Task TaskBrief_action_with_autolaunch_and_repo_path_starts_session_and_persists_it()
    {
        using var db = NewDb();
        var (_, decision) = SeedMessageAndDecision(db);
        var payload = JsonSerializer.Serialize(new TaskBriefPayload(RepoName: "app", RepoPath: "X:/repos/app", Prompt: "plan the fix", Brief: "## plan the fix"));
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.TaskBrief, Payload = payload };
        db.Actions.Add(action);
        db.SaveChanges();

        var launcher = new FakeClaudeSessionLauncher();
        var notifier = new FakeRealtimeNotifier();
        var executor = CreateExecutor(db, new FakeSlackGateway(), launcher, notifier, autoLaunch: true);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Executed, action.Status);
        var started = Assert.Single(launcher.StartedSessions);
        Assert.Equal(action.Id, started.ActionItemId);
        Assert.Equal("X:/repos/app", started.RepoPath);
        Assert.Single(db.Sessions);
        Assert.Single(notifier.SessionStatusChanges);
    }

    [Fact]
    public async Task Ticket_action_creates_a_self_hosted_work_item()
    {
        using var db = NewDb();
        var (_, decision) = SeedMessageAndDecision(db);
        var payload = JsonSerializer.Serialize(new TicketPayload("file a ticket", "player-ui"));
        var action = new ActionItem { DecisionId = decision.Id, Type = ActionType.Ticket, Payload = payload };
        db.Actions.Add(action);
        db.SaveChanges();

        var notifier = new FakeRealtimeNotifier();
        var executor = CreateExecutor(db, new FakeSlackGateway(), new FakeClaudeSessionLauncher(), notifier);

        await executor.ExecuteAsync(action, CancellationToken.None);

        Assert.Equal(ActionStatus.Executed, action.Status);
        var workItem = Assert.Single(db.WorkItems);
        Assert.Equal("file a ticket", workItem.Title);
        Assert.Equal("player-ui", workItem.RepositoryName);
        Assert.Equal(decision.Id, workItem.SourceDecisionId);
        Assert.Equal(WorkItemStatus.Todo, workItem.Status);
        Assert.Single(notifier.WorkItemChanges);
    }
}
