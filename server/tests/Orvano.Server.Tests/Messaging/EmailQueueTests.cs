using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orvano.Core.Data;
using Orvano.Core.Events;
using Orvano.Core.Http;
using Orvano.Core.Jobs;
using Orvano.Core.Secrets;
using Orvano.Messaging;
using Orvano.Messaging.Application;
using Orvano.Messaging.Domain;
using Orvano.Messaging.Jobs;
using Orvano.Platform;
using Orvano.Platform.Contracts;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0009, slice 2, on real Postgres and a real SMTP server (Mailpit): an invite email is queued in the caller's
// transaction, sealed, sent by the worker's handler with retries, and ends as a row that keeps only a masked address.
public class EmailQueueTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Console = "console";
    private const string Url = "https://orvano.test/invite#Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5a";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_committed_invite_email_is_sealed_sent_and_leaves_only_a_masked_row()
    {
        await using var t = await StartAsync();
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        var to = NewEmail();

        Assert.True(await t.QueueInviteAsync(to, commit: true));

        var id = await t.ScalarAsync<Guid>("SELECT id FROM orvano.messaging_emails");
        Assert.Equal(("queued", $"{to[0]}***@invited.test", "console_invitation", 0), await t.RowAsync(id));
        var blob = await t.ScalarAsync<byte[]>("SELECT content_ciphertext FROM orvano.messaging_emails");
        Assert.DoesNotContain(to, Encoding.Latin1.GetString(blob), StringComparison.Ordinal);
        // AC-26: the job carries the ID and nothing else.
        Assert.Equal($$"""{"emailId": "{{id}}"}""", await t.ScalarAsync<string>("SELECT payload::text FROM orvano.jobs WHERE kind = 'messaging.email.send'"));
        Assert.Equal((6, "messaging", Console), await t.JobAsync());

        Assert.Null(await t.RunSendAsync());

        var message = await mailpit.WaitForMessageToAsync(to, Ct);
        Assert.Equal("Grace Hopper invited you to join Acme on Orvano", message.GetProperty("Subject").GetString());
        Assert.Equal("orvano@install.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Contains(Url, message.GetProperty("Text").GetString(), StringComparison.Ordinal);
        Assert.Contains(Url, message.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        var headers = await mailpit.HeadersAsync(message.GetProperty("ID").GetString()!, Ct);
        Assert.Equal($"<{id}@localhost>", headers.GetProperty("Message-Id")[0].GetString());
        Assert.Equal("auto-generated", headers.GetProperty("Auto-Submitted")[0].GetString());

        // AC-15: final, with the content gone and the source recorded.
        Assert.Equal(("sent", $"{to[0]}***@invited.test", "console_invitation", 1), await t.RowAsync(id));
        Assert.Equal("install", await t.ScalarAsync<string>("SELECT smtp_source FROM orvano.messaging_emails"));
        Assert.True(await t.ScalarAsync<bool>("SELECT content_ciphertext IS NULL AND completed_at IS NOT NULL FROM orvano.messaging_emails"));

        // AC-26: no address, link, or host in the jobs or the events.
        var stored = await t.ScalarAsync<string>(
            "SELECT (SELECT string_agg(payload::text, '') FROM orvano.jobs) || (SELECT string_agg(payload::text || coalesce(subject, ''), '') FROM orvano.events)");
        Assert.DoesNotContain(to, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("invited.test", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("orvano.test", stored, StringComparison.Ordinal);
        Assert.DoesNotContain(mailpit.Host, stored, StringComparison.Ordinal);
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'messaging.email.sent' AND project_id = 'console'"));

        // A duplicate run (a lease that expired mid send) finds a final row and does nothing.
        await t.ExecuteAsync("UPDATE orvano.jobs SET status = 'queued'");
        Assert.Null(await t.RunSendAsync());
        Assert.Equal(1, await mailpit.CountToAsync(to, Ct));
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'messaging.email.sent'"));
    }

    [Fact]
    public async Task A_rolled_back_change_leaves_no_row_and_no_job_and_no_SMTP_queues_nothing()
    {
        await using var t = await StartAsync();

        // AC-23: no install SMTP, so the invitation is not emailed and nothing is written.
        Assert.False(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT (SELECT count(*) FROM orvano.messaging_emails) + (SELECT count(*) FROM orvano.jobs)"));

        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: false));
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT (SELECT count(*) FROM orvano.messaging_emails) + (SELECT count(*) FROM orvano.jobs)"));
    }

    // AC-19: the install cap, per project, and when the next slot frees.
    [Fact]
    public async Task The_install_cap_refuses_the_next_email_and_says_when_a_slot_frees()
    {
        await using var t = await StartAsync(hourlyLimit: 2);
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        var queue = t.Get<EmailQueue>();

        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.False(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.Equal(2L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.messaging_emails"));

        // Two rows, limit two: the oldest one's hour ending frees a slot.
        await t.ExecuteAsync("UPDATE orvano.messaging_emails SET created_at = now() - interval '50 minutes' WHERE id = (SELECT min(id::text)::uuid FROM orvano.messaging_emails)");
        var refused = Assert.IsType<QueueRefusal.RateLimited>(await t.InTransactionAsync(tx => queue.AdmitAsync(tx, Console, Ct)));
        Assert.InRange(refused.RetryAfter.TotalMinutes, 9.5, 10);

        // Another project without its own SMTP has its own count; one with its own SMTP has no cap.
        Assert.Null(await t.InTransactionAsync(tx => queue.AdmitAsync(tx, "shop", Ct)));
        await t.SetSmtpAsync("busy", "smtp.example.com", 587);
        for (var i = 0; i < 3; i++)
            await t.InTransactionAsync(tx => queue.InsertAsync(tx, "busy", "console_invitation", NewEmail(), new EmailContent("s", "<p>h</p>", "t"), Ct));
        Assert.Null(await t.InTransactionAsync(tx => queue.AdmitAsync(tx, "busy", Ct)));

        // An hour old row no longer counts.
        await t.ExecuteAsync("UPDATE orvano.messaging_emails SET created_at = now() - interval '61 minutes' WHERE project_id = 'console'");
        Assert.Null(await t.InTransactionAsync(tx => queue.AdmitAsync(tx, Console, Ct)));
        Assert.IsType<QueueRefusal.NotConfigured>(await t.WithoutSmtpAsync(Console, tx => queue.AdmitAsync(tx, "shop", Ct)));
    }

    // AC-15, AC-16: a retry asks for the spec's delay, reads the settings again, and the 6th failure is final.
    [Fact]
    public async Task An_unreachable_server_is_retried_on_schedule_and_fixed_settings_rescue_the_email()
    {
        await using var t = await StartAsync();
        await t.SetSmtpAsync(Console, "127.0.0.1", OrvanoProcess.FreePort());
        var rescued = NewEmail();
        Assert.True(await t.QueueInviteAsync(rescued, commit: true));
        var id = await t.ScalarAsync<Guid>("SELECT id FROM orvano.messaging_emails");

        var first = Assert.IsType<JobRetryException>(await t.RunSendAsync());
        Assert.Equal(TimeSpan.FromSeconds(30), first.Delay);
        Assert.Contains("smtp_unreachable", first.Message, StringComparison.Ordinal);
        var third = Assert.IsType<JobRetryException>(await t.RunSendAsync(attempt: 3));
        Assert.Equal(TimeSpan.FromMinutes(2), third.Delay);
        Assert.Equal(("queued", $"{rescued[0]}***@invited.test", "console_invitation", 3), await t.RowAsync(id));

        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        Assert.Null(await t.RunSendAsync(attempt: 4));
        Assert.Equal("sent", (await t.RowAsync(id)).Status);
        await mailpit.WaitForMessageToAsync(rescued, Ct);

        // The 6th failed attempt fails the row with that attempt's code, and the job itself succeeds.
        await t.SetSmtpAsync(Console, "127.0.0.1", OrvanoProcess.FreePort());
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.Null(await t.RunSendAsync(attempt: 6));
        Assert.Equal("smtp_unreachable", await t.ScalarAsync<string>("SELECT error_code FROM orvano.messaging_emails WHERE status = 'failed'"));
        Assert.True(await t.ScalarAsync<bool>("SELECT content_ciphertext IS NULL AND smtp_source IS NULL FROM orvano.messaging_emails WHERE status = 'failed'"));
        var failed = await t.ScalarAsync<string>("SELECT payload::text FROM orvano.events WHERE type = 'messaging.email.failed'");
        Assert.Contains("\"errorCode\": \"smtp_unreachable\"", failed, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"system\"", failed, StringComparison.Ordinal);
    }

    // AC-15 to AC-17: the reasons an email fails at once, none of which sends anything.
    [Fact]
    public async Task Stale_unreadable_unconfigured_and_inactive_emails_fail_at_once()
    {
        await using var t = await StartAsync(allowPrivateHosts: false);
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        var queue = t.Get<EmailQueue>();

        var stale = NewEmail();
        Assert.True(await t.QueueInviteAsync(stale, commit: true));
        await t.ExecuteAsync("UPDATE orvano.messaging_emails SET created_at = now() - interval '31 minutes'");
        Assert.Null(await t.RunSendAsync());
        Assert.Equal("email_expired", await t.OnlyErrorAsync());

        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        await t.ExecuteAsync("UPDATE orvano.messaging_emails SET content_ciphertext = decode('0102030405', 'hex') WHERE status = 'queued'");
        Assert.Null(await t.RunSendAsync());
        Assert.Equal("email_unreadable", await t.OnlyErrorAsync());

        // No platform project called 'ghost' exists, so it is not active.
        await t.InTransactionAsync(tx => queue.InsertAsync(tx, "ghost", "console_invitation", NewEmail(), new EmailContent("s", "<p>h</p>", "t"), Ct));
        Assert.Null(await t.RunSendAsync());
        Assert.Equal("project_not_active", await t.OnlyErrorAsync());

        // A project's own SMTP on a private address is refused when the connection is made (AC-3); the install's is not.
        await t.ExecuteAsync("INSERT INTO orvano.platform_projects (id, org_id, kind, name, status) SELECT 'shop', NULL, 'system', 'Shop', 'active'");
        await t.SetSmtpAsync("shop", "127.0.0.1", mailpit.SmtpPort);
        await t.InTransactionAsync(tx => queue.InsertAsync(tx, "shop", "console_invitation", NewEmail(), new EmailContent("s", "<p>h</p>", "t"), Ct));
        Assert.Null(await t.RunSendAsync());
        Assert.Equal("smtp_host_not_allowed", await t.OnlyErrorAsync());

        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        await t.ExecuteAsync("DELETE FROM orvano.messaging_smtp_settings");
        Assert.Null(await t.RunSendAsync());
        Assert.Equal("email_not_configured", await t.OnlyErrorAsync());

        // A job whose row was purged completes as a no op.
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        await t.ExecuteAsync("DELETE FROM orvano.messaging_emails WHERE status = 'queued'");
        Assert.Null(await t.RunSendAsync());

        Assert.Equal(0, await mailpit.CountToAsync(stale, Ct));
        Assert.Equal(5L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'messaging.email.failed'"));
    }

    // AC-17, AC-20: a row the reaper left queued is failed, and rows older than 30 days are deleted.
    [Fact]
    public async Task Retention_fails_stuck_emails_and_deletes_old_ones()
    {
        await using var t = await StartAsync();
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        await t.ExecuteAsync("UPDATE orvano.messaging_emails SET created_at = now() - interval '31 minutes' WHERE id = (SELECT min(id::text)::uuid FROM orvano.messaging_emails)");
        await t.ExecuteAsync(
            """
            INSERT INTO orvano.messaging_emails (id, project_id, template, recipient_masked, status, completed_at, created_at)
            VALUES (gen_random_uuid(), 'console', 'console_invitation', 'o***@x.test', 'sent', now() - interval '31 days', now() - interval '31 days'),
                   (gen_random_uuid(), 'console', 'console_invitation', 'k***@x.test', 'sent', now() - interval '29 days', now() - interval '29 days')
            """);

        Assert.Equal((1, 1), await MessagingRetention.RunAsync(t.Database.App, Ct));
        Assert.Equal((0, 0), await MessagingRetention.RunAsync(t.Database.App, Ct));

        Assert.Equal(["failed", "queued", "sent"], await t.ScalarAsync<string[]>("SELECT array_agg(status ORDER BY status) FROM orvano.messaging_emails"));
        Assert.Equal("email_expired", await t.OnlyErrorAsync());
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'messaging.email.failed'"));
    }

    // AC-27: a purged project loses its rows in all three tables, and running the job again changes nothing.
    [Fact]
    public async Task A_purged_project_loses_its_emails_templates_and_settings()
    {
        await using var t = await StartAsync();
        await t.SetSmtpAsync(Console, mailpit.Host, mailpit.SmtpPort);
        await t.SetSmtpAsync("shop", "smtp.example.com", 587);
        var queue = t.Get<EmailQueue>();
        await t.InTransactionAsync(tx => queue.InsertAsync(tx, "shop", "console_invitation", NewEmail(), new EmailContent("s", "<p>h</p>", "t"), Ct));
        Assert.True(await t.QueueInviteAsync(NewEmail(), commit: true));
        await t.ExecuteAsync(
            "INSERT INTO orvano.messaging_email_templates (project_id, kind, locale, subject, html, updated_by_user_id) VALUES ('shop', 'recovery', 'en', 's', '<p>h</p>', gen_random_uuid())");

        var jobs = MessagingJobs.OnProjectPurged(new OutboxEvent(1, "shop", MessagingJobs.ProjectPurgedEvent, "shop", "{}", DateTimeOffset.UtcNow)).ToList();
        var job = Assert.Single(jobs);
        Assert.Equal((MessagingJobs.PurgeProject, MessagingJobs.Queue, "shop"), (job.Kind, job.Queue, job.ProjectId));
        Assert.Empty(MessagingJobs.OnProjectPurged(new OutboxEvent(2, null, MessagingJobs.ProjectPurgedEvent, null, "{}", DateTimeOffset.UtcNow)));

        for (var run = 0; run < 2; run++)
        {
            await using var scope = t.Services.CreateAsyncScope();
            await MessagingJobs.PurgeProjectAsync(new JobContext(new ClaimedJob(1, job.Queue, job.Kind, "shop", job.PayloadJson, 1, 10), scope.ServiceProvider), Ct);
        }

        Assert.Equal(0L, await t.ScalarAsync<long>(
            """
            SELECT (SELECT count(*) FROM orvano.messaging_emails WHERE project_id = 'shop')
                 + (SELECT count(*) FROM orvano.messaging_email_templates WHERE project_id = 'shop')
                 + (SELECT count(*) FROM orvano.messaging_smtp_settings WHERE project_id = 'shop')
            """));
        Assert.Equal(2L, await t.ScalarAsync<long>("SELECT (SELECT count(*) FROM orvano.messaging_emails) + (SELECT count(*) FROM orvano.messaging_smtp_settings)"));
    }

    // Unique per test, so one Mailpit serves every test of the class.
    private static string NewEmail() => $"{Guid.NewGuid():N}@invited.test";

    private async Task<Harness> StartAsync(int hourlyLimit = 200, bool allowPrivateHosts = true)
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ORVANO_SMTP_ALLOW_PRIVATE_HOSTS"] = allowPrivateHosts ? "true" : "false",
                ["ORVANO_EMAIL_INSTALL_HOURLY_LIMIT"] = hourlyLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton(OrvanoDb.App, (_, _) => database.Track(NpgsqlDataSource.Create(database.AppUrl)));
        services.AddKeyedSingleton(OrvanoDb.Admin, (_, _) => database.Track(NpgsqlDataSource.Create(database.AdminUrl)));
        services.AddSingleton(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        services.AddSingleton(PublicUrl.Parse(OrvanoProcess.PublicUrl));
        new PlatformModule().ConfigureServices(services, config);
        var messaging = new MessagingModule();
        messaging.ConfigureServices(services, config);
        messaging.ConfigureApiServices(services, config);
        return new Harness(database, services.BuildServiceProvider());
    }

    /// <summary>The Messaging module wired as the api and the worker wire it, over a fresh migrated database.</summary>
    private sealed class Harness(TestDatabase database, ServiceProvider services) : IAsyncDisposable
    {
        public TestDatabase Database => database;

        public IServiceProvider Services => services;

        public T Get<T>() where T : notnull => services.GetRequiredService<T>();

        public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
            TestDatabase.ScalarAsync<T>(database.Superuser, sql, parameters);

        public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) =>
            TestDatabase.ExecuteAsync(database.Superuser, sql, parameters);

        public async Task<T> InTransactionAsync<T>(Func<NpgsqlTransaction, Task<T>> work, bool commit = true)
        {
            await using var conn = await database.App.OpenConnectionAsync(Ct);
            await using var tx = await conn.BeginTransactionAsync(Ct);
            var result = await work(tx);
            if (commit) await tx.CommitAsync(Ct);
            else await tx.RollbackAsync(Ct);
            return result;
        }

        /// <summary>Runs <paramref name="work"/> while <paramref name="projectId"/> has no SMTP row, then puts it back.</summary>
        public async Task<T> WithoutSmtpAsync<T>(string projectId, Func<NpgsqlTransaction, Task<T>> work)
        {
            await using var conn = await database.App.OpenConnectionAsync(Ct);
            await using var tx = await conn.BeginTransactionAsync(Ct);
            await using (var delete = new NpgsqlCommand("DELETE FROM orvano.messaging_smtp_settings WHERE project_id = @p", conn, tx))
            {
                delete.Parameters.AddWithValue("p", projectId);
                await delete.ExecuteNonQueryAsync(Ct);
            }

            var result = await work(tx);
            await tx.RollbackAsync(Ct);
            return result;
        }

        /// <summary>Queues the invite email through Platform's contract, as <c>InvitationService</c> does.</summary>
        public Task<bool> QueueInviteAsync(string to, bool commit) => InTransactionAsync(
            tx => Get<IConsoleInvitationMailer>().QueueAsync(
                tx, new InvitationEmail(to, "Acme", "Grace Hopper", "grace@console.test", OrgRole.Developer, Url, DateTimeOffset.UtcNow.AddDays(7)), Ct),
            commit);

        /// <summary>Saves SMTP settings with no sign in for <paramref name="projectId"/>, replacing any.</summary>
        public Task SetSmtpAsync(string projectId, string host, int port) => ExecuteAsync(
            """
            INSERT INTO orvano.messaging_smtp_settings (project_id, host, port, security, from_email, updated_by_user_id)
            VALUES (@project, @host, @port, 'none', 'orvano@install.test', gen_random_uuid())
            ON CONFLICT (project_id) DO UPDATE SET host = excluded.host, port = excluded.port
            """, ("project", projectId), ("host", host), ("port", port));

        public async Task<(string Status, string Recipient, string Template, int Attempts)> RowAsync(Guid id)
        {
            await using var cmd = database.Superuser.CreateCommand(
                "SELECT status, recipient_masked, template, attempts FROM orvano.messaging_emails WHERE id = @id");
            cmd.Parameters.AddWithValue("id", id);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            Assert.True(await reader.ReadAsync(Ct));
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
        }

        public async Task<(int MaxAttempts, string Queue, string ProjectId)> JobAsync()
        {
            await using var cmd = database.Superuser.CreateCommand(
                "SELECT max_attempts, queue, project_id FROM orvano.jobs WHERE kind = 'messaging.email.send'");
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            Assert.True(await reader.ReadAsync(Ct));
            return (reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
        }

        /// <summary>The error code of the one failed row that has not been looked at yet, which is then removed.</summary>
        public async Task<string> OnlyErrorAsync()
        {
            var code = await ScalarAsync<string>("SELECT error_code FROM orvano.messaging_emails WHERE status = 'failed'");
            Assert.True(await ScalarAsync<bool>("SELECT content_ciphertext IS NULL AND completed_at IS NOT NULL FROM orvano.messaging_emails WHERE status = 'failed'"));
            await ExecuteAsync("DELETE FROM orvano.messaging_emails WHERE status = 'failed'");
            return code;
        }

        /// <summary>
        /// Runs the one queued send job through the real handler as attempt <paramref name="attempt"/> (null: its
        /// next), and returns the retry it asked for, or null when it completed.
        /// </summary>
        public async Task<JobRetryException?> RunSendAsync(int? attempt = null)
        {
            ClaimedJob job;
            await using (var cmd = database.Superuser.CreateCommand(
                """
                UPDATE orvano.jobs SET status = 'running', attempts = attempts + 1
                WHERE id = (SELECT max(id) FROM orvano.jobs WHERE kind = 'messaging.email.send' AND status = 'queued')
                RETURNING id, queue, kind, project_id, payload::text, attempts, max_attempts
                """))
            {
                await using var reader = await cmd.ExecuteReaderAsync(Ct);
                Assert.True(await reader.ReadAsync(Ct), "No queued send job.");
                job = new ClaimedJob(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), attempt ?? reader.GetInt32(5), reader.GetInt32(6));
            }

            await using var scope = services.CreateAsyncScope();
            try
            {
                await EmailSendJob.RunAsync(new JobContext(job, scope.ServiceProvider), Ct);
                await ExecuteAsync("UPDATE orvano.jobs SET status = 'succeeded' WHERE id = @id", ("id", job.Id));
                return null;
            }
            catch (JobRetryException retry)
            {
                await ExecuteAsync("UPDATE orvano.jobs SET status = 'queued' WHERE id = @id", ("id", job.Id));
                return retry;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
