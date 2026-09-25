using Microsoft.Extensions.Logging;
using Npgsql;

namespace Orvano.Core.Migrations;

/// <summary>
/// Applies platform migrations forward only, one transaction per file, under advisory lock
/// (ORVA, 1) so concurrent runs on the same database wait for each other.
/// </summary>
public sealed class MigrationRunner(NpgsqlDataSource adminDb, ILogger<MigrationRunner> logger)
{
    /// <summary>Checks the applied migrations against <paramref name="migrations"/>, then applies the pending ones in order.</summary>
    /// <returns>How many migrations were applied.</returns>
    /// <exception cref="InvalidOperationException">The database has a migration this build does not know, or an applied migration's file changed.</exception>
    public async Task<int> RunAsync(IReadOnlyList<PlatformMigration> migrations, CancellationToken ct)
    {
        await using var conn = await adminDb.OpenConnectionAsync(ct);
        await ExecuteLockAsync(conn, "pg_advisory_lock", ct);
        try
        {
            var applied = await ReadAppliedAsync(conn, ct);
            Verify(migrations, applied);

            var pending = migrations.Where(m => !applied.ContainsKey(m.Version)).ToList();
            foreach (var migration in pending)
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await using (var cmd = new NpgsqlCommand(migration.Sql, conn, tx))
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var record = new NpgsqlCommand(
                    "INSERT INTO orvano.schema_migrations (version, name, sha256) VALUES (@version, @name, @sha256)", conn, tx))
                {
                    record.Parameters.AddWithValue("version", migration.Version);
                    record.Parameters.AddWithValue("name", migration.Name);
                    record.Parameters.AddWithValue("sha256", migration.Sha256);
                    await record.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                logger.LogInformation("Applied platform migration {Version:D4}_{Name}", migration.Version, migration.Name);
            }

            logger.LogInformation(
                "Platform schema is at version {Version}; {Count} migration(s) applied this run",
                migrations.Count == 0 ? 0 : migrations[^1].Version, pending.Count);
            return pending.Count;
        }
        finally
        {
            await ExecuteLockAsync(conn, "pg_advisory_unlock", CancellationToken.None);
        }
    }

    private static void Verify(IReadOnlyList<PlatformMigration> migrations, Dictionary<int, string> applied)
    {
        var embedded = migrations.ToDictionary(m => m.Version);
        foreach (var (version, sha) in applied.OrderBy(a => a.Key))
        {
            if (!embedded.TryGetValue(version, out var migration))
                throw new InvalidOperationException(
                    $"The database has platform migration {version:D4}, which this build does not know. " +
                    "The database is newer than this Orvano version; upgrade Orvano instead.");

            if (migration.Sha256 != sha)
                throw new InvalidOperationException(
                    $"Platform migration {version:D4}_{migration.Name} changed after it was applied. " +
                    "Applied migrations are immutable; add a new migration file instead.");
        }
    }

    private static async Task<Dictionary<int, string>> ReadAppliedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var applied = new Dictionary<int, string>();
        if (await SchemaVersion.ReadAsync(conn, ct) == 0) return applied;

        await using var cmd = new NpgsqlCommand("SELECT version, sha256 FROM orvano.schema_migrations", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) applied[reader.GetInt32(0)] = reader.GetString(1);
        return applied;
    }

    private static async Task ExecuteLockAsync(NpgsqlConnection conn, string function, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"SELECT {function}(@class, @object)", conn);
        cmd.Parameters.AddWithValue("class", AdvisoryLocks.Class);
        cmd.Parameters.AddWithValue("object", AdvisoryLocks.Migrations);
        await cmd.ExecuteScalarAsync(ct);
    }
}
