using Npgsql;
using Orvano.Core.Migrations;

namespace Orvano.Server.Hosting;

internal static class StartupChecks
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
    /// <c>ORVANO_TEST_FIXTURES</c> seeds known projects, users, and keys for the shared scenarios
    /// (spec 0001). It is refused outside the <c>Test</c> environment, and the file must exist.
    /// </summary>
    public static bool TestFixturesAllowed(IHostEnvironment environment, IConfiguration config, ILogger logger)
    {
        var path = config[TestFixtures.Setting];
        if (string.IsNullOrEmpty(path)) return true;

        if (!environment.IsEnvironment(OrvanoEnvironments.Test))
        {
            logger.LogCritical(
                "{Setting} is set but the environment is {Environment}; it is only allowed in {Test}",
                TestFixtures.Setting, environment.EnvironmentName, OrvanoEnvironments.Test);
            return false;
        }

        if (!File.Exists(path))
        {
            logger.LogCritical("{Setting} points at {Path}, which does not exist", TestFixtures.Setting, path);
            return false;
        }

        return true;
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
