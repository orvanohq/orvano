using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Auth;
using Orvano.Auth.Jobs;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Jobs;
using Orvano.Core.Modules;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-35: the hourly retention sweep deletes MFA tickets and WebAuthn challenges past their expiry and
// authenticator apps never confirmed within 15 minutes; a project purge deletes the project's rows in all six new
// tables and leaves every other project's.
public class MfaJobsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retention_deletes_expired_tickets_and_challenges_and_stale_pending_apps_and_keeps_the_rest()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var (ada, bob, cy) = (await UserAsync(api, "ada@x.com"), await UserAsync(api, "bob@x.com"), await UserAsync(api, "cy@x.com"));
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            """
            INSERT INTO orvano.auth_mfa_tickets (project_id, user_id, ticket_hash, method, expires_at)
            VALUES (@p, @ada, sha256('old'::bytea), 'password', now() - interval '1 second'),
                   (@p, @ada, sha256('new'::bytea), 'password', now() + interval '5 minutes');
            INSERT INTO orvano.auth_webauthn_challenges (project_id, purpose, user_id, challenge_hash, expires_at)
            VALUES (@p, 'sign_in', NULL, sha256('c1'::bytea), now() - interval '1 second'),
                   (@p, 'register', @ada, sha256('c2'::bytea), now() - interval '1 second'),
                   (@p, 'sign_in', NULL, sha256('c3'::bytea), now() + interval '5 minutes');
            INSERT INTO orvano.auth_totp_factors (user_id, project_id, secret_ciphertext, created_at, confirmed_at)
            VALUES (@ada, @p, '\x00'::bytea, now() - interval '16 minutes', NULL),
                   (@bob, @p, '\x00'::bytea, now(), NULL),
                   (@cy, @p, '\x00'::bytea, now() - interval '1 day', now() - interval '1 day');
            """, ("p", AuthApi.Project), ("ada", ada), ("bob", bob), ("cy", cy));
        await using var app = NpgsqlDataSource.Create(api.Database.AppUrl);

        var (_, _, tokens) = await AuthRetention.RunAsync(app, Ct);

        Assert.Equal(1 + 2 + 1, tokens);
        Assert.Equal(1L, await CountAsync(api, "auth_mfa_tickets"));
        Assert.Equal(1L, await CountAsync(api, "auth_mfa_tickets WHERE ticket_hash = sha256('new'::bytea)"));
        Assert.Equal(1L, await CountAsync(api, "auth_webauthn_challenges"));
        Assert.Equal(1L, await CountAsync(api, "auth_webauthn_challenges WHERE challenge_hash = sha256('c3'::bytea)"));
        var kept = await TestDatabase.ScalarAsync<Guid[]>(api.Database.Superuser, "SELECT array_agg(user_id) FROM orvano.auth_totp_factors");
        Assert.Equal(new[] { bob, cy }.Order(), kept.Order());
    }

    [Fact]
    public async Task A_purge_deletes_the_projects_second_factors_passkeys_and_settings_and_no_one_elses()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        foreach (var project in new[] { AuthApi.Project, AuthApi.OtherProject })
        {
            var user = await UserAsync(api, "ada@x.com", project);
            await TestDatabase.ExecuteAsync(api.Database.Superuser,
                """
                INSERT INTO orvano.auth_method_settings (project_id, passkeys_enabled, rp_id) VALUES (@p, true, 'example.com');
                INSERT INTO orvano.auth_totp_factors (user_id, project_id, secret_ciphertext, confirmed_at) VALUES (@u, @p, '\x00'::bytea, now());
                INSERT INTO orvano.auth_recovery_codes (project_id, user_id, code_mac, mac_key_id) VALUES (@p, @u, '\x00'::bytea, 'k');
                INSERT INTO orvano.auth_passkeys (project_id, user_id, credential_id, public_key, sign_count, name, backup_eligible, backed_up, rp_id)
                VALUES (@p, @u, '\x01'::bytea, '\x02'::bytea, 0, 'Passkey', false, false, 'example.com');
                INSERT INTO orvano.auth_mfa_tickets (project_id, user_id, ticket_hash, method, expires_at)
                VALUES (@p, @u, sha256(@p::bytea), 'password', now() + interval '5 minutes');
                INSERT INTO orvano.auth_webauthn_challenges (project_id, purpose, user_id, challenge_hash, expires_at)
                VALUES (@p, 'sign_in', NULL, sha256(@p::bytea), now() + interval '5 minutes');
                """, ("p", project), ("u", user));
        }

        await RunPurgeAsync(api, AuthApi.Project);

        foreach (var table in new[]
                 {
                     "auth_method_settings", "auth_totp_factors", "auth_recovery_codes", "auth_passkeys", "auth_mfa_tickets",
                     "auth_webauthn_challenges",
                 })
        {
            Assert.Equal(0L, await CountAsync(api, $"{table} WHERE project_id = '{AuthApi.Project}'"));
            Assert.Equal(1L, await CountAsync(api, $"{table} WHERE project_id = '{AuthApi.OtherProject}'"));
        }
    }

    private static async Task<Guid> UserAsync(AuthApi api, string email, string project = AuthApi.Project)
    {
        using var signUp = await api.SignUpAsync(email, project: project);
        return Guid.Parse(AuthApi.UserId(signUp));
    }

    private static Task<long> CountAsync(AuthApi api, string tableAndWhere) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser, $"SELECT count(*) FROM orvano.{tableAndWhere}");

    /// <summary>Runs the purge the Auth module queues for Platform's project purge event, twice, as it must be idempotent.</summary>
    private static async Task RunPurgeAsync(AuthApi api, string projectId)
    {
        var work = new WorkRegistry();
        new AuthModule().RegisterWork(work);
        var purged = new OutboxEvent(1, projectId, AuthJobs.ProjectPurgedEvent, projectId, "{}", DateTimeOffset.UtcNow);
        var job = Assert.Single(work.ConsumersFor(purged).Single(c => c.Name == "auth.purge_users").Consumer(purged));
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddKeyedSingleton<NpgsqlDataSource>(OrvanoDb.App, (_, _) => NpgsqlDataSource.Create(api.Database.AppUrl))
            .BuildServiceProvider();
        var handler = work.HandlerFor(job.Kind)!;
        for (var run = 0; run < 2; run++)
            await handler(new JobContext(new ClaimedJob(1, job.Queue, job.Kind, projectId, job.PayloadJson, 1, 10), services), Ct);
    }
}
