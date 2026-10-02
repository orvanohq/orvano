using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orvano.Auth;
using Orvano.Auth.Application;
using Orvano.Core.Data;
using Orvano.Core.Http;
using Orvano.Core.RateLimiting;
using Orvano.Core.Secrets;
using Orvano.Messaging;
using Orvano.Messaging.Application;
using Orvano.Messaging.Contracts;
using Orvano.Platform;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 AC-9 in process on real Postgres: the SMTP settings vanish between the availability check and the queue
// call, which no HTTP test can time. The real binary seeds the database; the use case runs here on the real modules.
public class RecoveryQueueRefusalTests(PostgresFixture postgres)
{
    private const string Redirect = "https://app.example.com/auth";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Settings_that_change_after_the_check_roll_back_log_a_warning_and_still_answer_success()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        using var signUp = await api.SignUpAsync("ada@x.com");
        var logs = new ListLoggerProvider();
        await using var services = Services(api.Database, logs);
        var recovery = services.GetRequiredService<RecoveryService>();

        var outcome = await recovery.RequestAsync(AuthApi.Project, "ada@x.com", Redirect, "ip:203.0.113.9", Ct);

        Assert.True(outcome.Succeeded);
        // The transaction rolled back: no token, no email, no event.
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens"));
        Assert.Equal(0L, await api.QueuedEmailCountAsync());
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.email_token.created'"));
        // The warning names the project and the kind, never the email.
        var warning = Assert.Single(logs.Logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(AuthApi.Project, warning.FullText, StringComparison.Ordinal);
        Assert.Contains("recovery", warning.FullText, StringComparison.Ordinal);
        Assert.Contains("email_not_configured", warning.FullText, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@x.com", logs.FullText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Platform, Messaging, and Auth modules as the api wires them, with the SMTP seam around the queue.</summary>
    private static ServiceProvider Services(TestDatabase database, ListLoggerProvider logs)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddKeyedSingleton(OrvanoDb.App, (_, _) => database.Track(NpgsqlDataSource.Create(database.AppUrl)));
        services.AddKeyedSingleton(OrvanoDb.Admin, (_, _) => database.Track(NpgsqlDataSource.Create(database.AdminUrl)));
        services.AddSingleton(new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys)));
        services.AddSingleton(PublicUrl.Parse(OrvanoProcess.PublicUrl));
        services.AddHybridCache();
        services.AddSingleton<RateLimits>();
        var platform = new PlatformModule();
        platform.ConfigureServices(services, config);
        platform.ConfigureApiServices(services, config);
        var messaging = new MessagingModule();
        messaging.ConfigureServices(services, config);
        messaging.ConfigureApiServices(services, config);
        var auth = new AuthModule();
        auth.ConfigureServices(services, config);
        auth.ConfigureApiServices(services, config);
        services.AddSingleton<AuthEmailQueue>();
        services.AddSingleton<IEmailQueue>(sp => new SmtpRemovedAfterCheck(sp.GetRequiredService<AuthEmailQueue>(), database));
        return services.BuildServiceProvider();
    }

    /// <summary>The real queue, except that the install's SMTP row is deleted once the availability check has passed.</summary>
    private sealed class SmtpRemovedAfterCheck(IEmailQueue inner, TestDatabase database) : IEmailQueue
    {
        public async Task<EmailAvailability> CheckAvailabilityAsync(string projectId, CancellationToken ct)
        {
            var availability = await inner.CheckAvailabilityAsync(projectId, ct);
            await TestDatabase.ExecuteAsync(database.Superuser, "DELETE FROM orvano.messaging_smtp_settings");
            return availability;
        }

        public Task<EmailQueueResult> QueueAuthEmailAsync(NpgsqlTransaction tx, AuthEmail email, CancellationToken ct) =>
            inner.QueueAuthEmailAsync(tx, email, ct);
    }
}
