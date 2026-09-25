using Npgsql;
using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

public static class StartupChecks
{
    /// <summary>
    /// Scheduling and Npgsql need real time zone data. Fails loudly on an image without ICU and
    /// tzdata instead of misbehaving later (the reason for the chiseled-extra base image).
    /// </summary>
    public static bool TimeZonesAvailable(ILogger logger)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Time zone data is missing; Orvano needs an image with tzdata and ICU");
            return false;
        }
    }

    /// <summary>
    /// Waits briefly for Postgres, then refuses to start unless the database schema version equals
    /// the highest migration embedded in this binary.
    /// </summary>
    public static async Task<bool> SchemaMatchesAsync(NpgsqlDataSource db, ILogger logger, CancellationToken ct)
    {
        var version = await WaitForDatabaseAsync(db, logger, ct, conn => SchemaVersion.ReadAsync(conn, ct));
        if (version is null) return false;

        if (version < PlatformSchema.ExpectedVersion)
        {
            logger.LogCritical(
                "Database schema version is {Actual} but this build expects {Expected}. Run the migrate role with this build first.",
                version, PlatformSchema.ExpectedVersion);
            return false;
        }

        if (version > PlatformSchema.ExpectedVersion)
        {
            logger.LogCritical(
                "Database schema version is {Actual} but this build expects {Expected}. The database is newer than this Orvano version; upgrade Orvano instead.",
                version, PlatformSchema.ExpectedVersion);
            return false;
        }

        return true;
    }

    public static async Task<T?> WaitForDatabaseAsync<T>(
        NpgsqlDataSource db, ILogger logger, CancellationToken ct, Func<NpgsqlConnection, Task<T>> probe)
        where T : struct
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var conn = await db.OpenConnectionAsync(ct);
                return await probe(conn);
            }
            catch (NpgsqlException ex) when (DateTime.UtcNow < deadline)
            {
                logger.LogWarning("Database not reachable yet ({Message}); retrying", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (NpgsqlException ex)
            {
                logger.LogCritical(ex, "Database not reachable");
                return null;
            }
        }
    }
}
