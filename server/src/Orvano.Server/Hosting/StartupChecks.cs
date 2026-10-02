using Npgsql;
using Orvano.Auth.Domain;
using Orvano.Core.Migrations;
using Orvano.Platform.Contracts;

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
    /// Loads libsodium (NSec's native library) and computes the dummy password hash once, before the api serves anything
    /// (spec 0004). This is also the image smoke test: an image whose native library fails to load on its architecture
    /// refuses to start instead of failing on the first sign in.
    /// </summary>
    public static bool PasswordHashingAvailable(IServiceProvider services, ILogger logger)
    {
        try
        {
            services.GetRequiredService<PasswordHasher>();
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or InvalidOperationException)
        {
            logger.LogCritical(ex, "Password hashing is unavailable: libsodium could not be loaded");
            return false;
        }
    }

    /// <summary>
    /// <c>ORVANO_TEST_FIXTURES</c> seeds known data for the shared scenarios (spec 0001). It is
    /// refused outside the <c>Test</c> environment, and the file must exist and parse.
    /// </summary>
    public static bool TestFixturesUsable(TestFixtures fixtures, ILogger logger)
    {
        if (fixtures.Problem is null) return true;
        logger.LogCritical("Test fixtures refused: {Problem}", fixtures.Problem);
        return false;
    }

    /// <summary><c>ORVANO_TEST_MAILPIT_URL</c> is refused outside <c>Test</c> and must be an absolute http or https URL (spec 0010).</summary>
    public static bool TestMailpitUsable(TestMailpit mailpit, ILogger logger)
    {
        if (mailpit.Problem is null) return true;
        logger.LogCritical("Test Mailpit setting refused: {Problem}", mailpit.Problem);
        return false;
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

    /// <summary>
    /// In <c>Production</c>, the api refuses to start while no install admin exists and no
    /// <c>ORVANO_SETUP_TOKEN</c> is set, or a stranger could claim the install first (spec 0006, AC-21).
    /// <c>Development</c> and <c>Test</c> leave the first sign up open. A malformed token is refused earlier, in
    /// every environment, when the Platform module reads it.
    /// </summary>
    public static async Task<bool> FirstAdminProtectedAsync(
        IHostEnvironment environment, IConfiguration config, IInstallSetupState setup, ILogger logger, CancellationToken ct)
    {
        if (!environment.IsProduction() || !string.IsNullOrEmpty(config["ORVANO_SETUP_TOKEN"])) return true;
        if (!await setup.IsSetupRequiredAsync(ct)) return true;

        logger.LogCritical(
            "ORVANO_SETUP_TOKEN is not set and this install has no admin yet, so anyone could create the first admin. Run the installer, or set ORVANO_SETUP_TOKEN as .env.example shows.");
        return false;
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
