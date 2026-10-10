using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Platform;
using Orvano.Platform.Contracts;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Platform;

/// <summary>
/// The Platform module wired the way the worker wires it, over a fresh migrated database: its services, and a way to
/// run its queued jobs through the registered handlers without a job loop, so each test decides when work happens.
/// </summary>
public sealed class PlatformHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly WorkRegistry _work = new();

    private readonly bool _ownsDatabase;

    private PlatformHarness(TestDatabase database, ServiceProvider services, bool ownsDatabase = true)
    {
        Database = database;
        _services = services;
        _ownsDatabase = ownsDatabase;
        new PlatformModule().RegisterWork(_work);
    }

    public TestDatabase Database { get; }

    public IServiceProvider Services => _services;

    public static async Task<PlatformHarness> StartAsync(PostgresFixture postgres, int graceDays = 7, string? setupToken = null)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        return Build(database, graceDays, setupToken, ownsDatabase: true);
    }

    /// <summary>
    /// The module over a database someone else owns and migrated, such as the api a test started, to run its queued
    /// jobs (the fixture projects' provisioning) the way the worker would. Disposing it leaves the database alone.
    /// </summary>
    public static PlatformHarness Over(TestDatabase database) => Build(database, graceDays: 7, setupToken: null, ownsDatabase: false);

    private static PlatformHarness Build(TestDatabase database, int graceDays, string? setupToken, bool ownsDatabase)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ORVANO_DELETE_GRACE_DAYS"] = graceDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ORVANO_SETUP_TOKEN"] = setupToken,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton(OrvanoDb.App, (_, _) => database.Track(NpgsqlDataSource.Create(database.AppUrl)));
        services.AddKeyedSingleton(OrvanoDb.Admin, (_, _) => database.Track(NpgsqlDataSource.Create(database.AdminUrl)));
        new PlatformModule().ConfigureServices(services, config);
        return new PlatformHarness(database, services.BuildServiceProvider(), ownsDatabase);
    }

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>
    /// Runs every due queued job of <paramref name="kind"/> once, as attempt <paramref name="attempt"/> (null: its next
    /// attempt), and records success or failure the way the job loop does. Returns how many ran.
    /// </summary>
    public async Task<int> RunJobsAsync(string kind, int? attempt = null)
    {
        var handler = _work.HandlerFor(kind) ?? throw new InvalidOperationException($"No handler for {kind}.");
        var jobs = new List<ClaimedJob>();
        await using (var cmd = Database.Superuser.CreateCommand(
            """
            UPDATE orvano.jobs SET status = 'running', attempts = attempts + 1
            WHERE kind = @kind AND status = 'queued' AND run_at <= now()
            RETURNING id, queue, kind, project_id, payload::text, attempts, max_attempts
            """))
        {
            cmd.Parameters.AddWithValue("kind", kind);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                jobs.Add(new ClaimedJob(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), attempt ?? reader.GetInt32(5), reader.GetInt32(6)));
            }
        }

        foreach (var job in jobs)
        {
            await using var scope = _services.CreateAsyncScope();
            string status;
            try
            {
                await handler(new JobContext(job, scope.ServiceProvider), Ct);
                status = "succeeded";
            }
            catch (Exception)
            {
                status = "failed";
            }

            await TestDatabase.ExecuteAsync(Database.Superuser, "UPDATE orvano.jobs SET status = @status WHERE id = @id",
                ("status", status), ("id", job.Id));
        }

        return jobs.Count;
    }

    /// <summary>Signs up a console account the way Auth will: admit, insert the user, then the Platform hooks, in one transaction.</summary>
    public async Task<(Guid UserId, SignupAdmission Admission)> SignUpAsync(string email, string? name = null, string? setupToken = null)
    {
        var userId = Guid.CreateVersion7();
        await using var conn = await Get<NpgsqlDataSource>(OrvanoDb.App).OpenConnectionAsync(Ct);
        await using var tx = await conn.BeginTransactionAsync(Ct);
        var admission = await Get<IConsoleSignupPolicy>().AdmitAsync(tx, email, null, setupToken, Ct);
        if (admission is SignupAdmission.Admitted admitted)
        {
            await Get<IConsoleAccountCreated>().OnCreatedAsync(tx, userId, name, email, admitted, Ct);
            await tx.CommitAsync(Ct);
        }

        return (userId, admission);
    }

    /// <summary>Adds a member directly, as row 15's invitations will.</summary>
    public Task AddMemberAsync(Guid orgId, Guid userId, OrgRole role) =>
        TestDatabase.ExecuteAsync(Database.Superuser,
            "INSERT INTO orvano.platform_memberships (org_id, user_id, role) VALUES (@org, @user, @role)",
            ("org", orgId), ("user", userId), ("role", role.ToString().ToLowerInvariant()));

    public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        TestDatabase.ScalarAsync<T>(Database.Superuser, sql, parameters);

    /// <summary>Waits until some session of this database is blocked on a row lock, so a race runs in a known order.</summary>
    public async Task WaitForLockWaitAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            if (await ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") > 0) return;
            await Task.Delay(25, Ct);
        }

        throw new TimeoutException("No session waited on a lock within 5 seconds.");
    }

    public T Get<T>(string key) where T : notnull => _services.GetRequiredKeyedService<T>(key);

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_ownsDatabase) await Database.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
