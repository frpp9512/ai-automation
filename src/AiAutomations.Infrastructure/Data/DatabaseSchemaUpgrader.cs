using Microsoft.EntityFrameworkCore;

namespace AiAutomations.Infrastructure.Data;

/// <summary>
/// Hand-rolled, additive-only schema patches for databases that already exist (created by an
/// earlier EnsureCreated() call before these columns/tables existed). EnsureCreated() only ever
/// creates a schema from scratch - it never alters an existing one - so new columns/tables need
/// this until the schema settles enough to justify switching to real EF Core migrations.
/// Every patch here must be safe to run repeatedly and to skip when already applied.
/// </summary>
public static class DatabaseSchemaUpgrader
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await ColumnExistsAsync(connection, "WorkItems", "PercentComplete", cancellationToken))
            {
                await ExecuteAsync(connection, "ALTER TABLE WorkItems ADD COLUMN PercentComplete INTEGER NOT NULL DEFAULT 0", cancellationToken);
            }

            if (!await ColumnExistsAsync(connection, "Actions", "FailureReason", cancellationToken))
            {
                await ExecuteAsync(connection, "ALTER TABLE Actions ADD COLUMN FailureReason TEXT NULL", cancellationToken);
            }

            if (!await ColumnExistsAsync(connection, "Messages", "SlackUserName", cancellationToken))
            {
                await ExecuteAsync(connection, "ALTER TABLE Messages ADD COLUMN SlackUserName TEXT NULL", cancellationToken);
            }

            if (!await TableExistsAsync(connection, "WorkItemComments", cancellationToken))
            {
                await ExecuteAsync(connection, """
                    CREATE TABLE WorkItemComments (
                        Id TEXT NOT NULL PRIMARY KEY,
                        WorkItemId TEXT NOT NULL,
                        Text TEXT NOT NULL,
                        IsAutomated INTEGER NOT NULL,
                        CreatedAtUtc INTEGER NOT NULL,
                        FOREIGN KEY (WorkItemId) REFERENCES WorkItems (Id) ON DELETE CASCADE
                    )
                    """, cancellationToken);
            }

            if (!await TableExistsAsync(connection, "AiUsageRecords", cancellationToken))
            {
                await ExecuteAsync(connection, """
                    CREATE TABLE AiUsageRecords (
                        Id TEXT NOT NULL PRIMARY KEY,
                        Provider TEXT NOT NULL,
                        Model TEXT NOT NULL,
                        Purpose TEXT NOT NULL,
                        InputTokens INTEGER NOT NULL,
                        OutputTokens INTEGER NOT NULL,
                        EstimatedCostUsd TEXT NULL,
                        CreatedAtUtc INTEGER NOT NULL
                    )
                    """, cancellationToken);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(System.Data.Common.DbConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(reader.GetOrdinal("name")), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        var param = command.CreateParameter();
        param.ParameterName = "$name";
        param.Value = table;
        command.Parameters.Add(param);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
