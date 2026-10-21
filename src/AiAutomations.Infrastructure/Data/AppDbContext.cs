using AiAutomations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiAutomations.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SlackMessage> Messages => Set<SlackMessage>();
    public DbSet<MessageDecision> Decisions => Set<MessageDecision>();
    public DbSet<ActionItem> Actions => Set<ActionItem>();
    public DbSet<ClaudeSession> Sessions => Set<ClaudeSession>();
    public DbSet<AutoActionRule> Rules => Set<AutoActionRule>();
    public DbSet<RepositoryProfile> Repositories => Set<RepositoryProfile>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkItemComment> WorkItemComments => Set<WorkItemComment>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<OpenProjectSyncRun> OpenProjectSyncRuns => Set<OpenProjectSyncRun>();

    // SQLite's EF provider can't ORDER BY a raw DateTimeOffset column, so store it
    // as a sortable binary representation instead (round-trips exactly).
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SlackMessage>()
            .HasIndex(m => new { m.SlackChannelId, m.SlackTs })
            .IsUnique();

        modelBuilder.Entity<SlackMessage>()
            .HasOne(m => m.Decision)
            .WithOne(d => d.Message)
            .HasForeignKey<MessageDecision>(d => d.MessageId);

        modelBuilder.Entity<MessageDecision>()
            .HasMany(d => d.Actions)
            .WithOne(a => a.Decision)
            .HasForeignKey(a => a.DecisionId);

        modelBuilder.Entity<ActionItem>()
            .HasOne(a => a.Session)
            .WithOne(s => s.ActionItem)
            .HasForeignKey<ClaudeSession>(s => s.ActionItemId);

        modelBuilder.Entity<RepositoryProfile>()
            .HasIndex(r => r.Name)
            .IsUnique();

        modelBuilder.Entity<WorkItem>()
            .HasMany<WorkItemComment>()
            .WithOne(c => c.WorkItem)
            .HasForeignKey(c => c.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
