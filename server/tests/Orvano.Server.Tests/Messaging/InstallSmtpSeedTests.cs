using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Messaging;

// Spec 0011, AC-4: ORVANO_INSTALL_SMTP_URL seeds the install SMTP once at api startup, against the real binary, real
// Postgres, and a real Mailpit.
public class InstallSmtpSeedTests(PostgresFixture postgres, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string From = "orvano@local.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_seed_writes_the_install_row_and_an_invite_arrives_through_it()
    {
        var admin = $"admin-{Guid.NewGuid():N}@console.test";
        var invited = $"invited-{Guid.NewGuid():N}@console.test";
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var seed = Seed($"smtp://{mailpit.Host}:{mailpit.SmtpPort}");
        await using var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>(seed)
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = await FixturesAsync(admin),
        }, listen: true);
        await using var worker = OrvanoProcess.Start(["worker"], new Dictionary<string, string>(seed)
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_DB_ADMIN_URL"] = database.AdminUrl,
        }, listen: true);
        await api.WaitUntilListeningAsync();
        await worker.WaitUntilListeningAsync();

        Assert.Equal(Guid.Empty, await TestDatabase.ScalarAsync<Guid>(
            database.Superuser, "SELECT updated_by_user_id FROM orvano.messaging_smtp_settings WHERE project_id = 'console'"));
        Assert.Equal("none", await TestDatabase.ScalarAsync<string>(
            database.Superuser, "SELECT security FROM orvano.messaging_smtp_settings WHERE project_id = 'console'"));
        Assert.Contains("Seeded the install SMTP settings from ORVANO_INSTALL_SMTP_URL", api.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(mailpit.Host + ":" + mailpit.SmtpPort, api.Output, StringComparison.Ordinal);

        using var http = api.Http();
        var org = await PostAsync(http, "/v1/console/orgs", new { name = "Acme" }, admin);
        var invite = await PostAsync(http, $"/v1/console/orgs/{org.GetProperty("id").GetString()}/invitations", new { email = invited, role = "viewer" }, admin);
        Assert.True(invite.GetProperty("emailed").GetBoolean());

        var message = await mailpit.WaitForMessageToAsync(invited, Ct);
        Assert.Equal(From, message.GetProperty("From").GetProperty("Address").GetString());
    }

    [Fact]
    public async Task The_seed_never_overwrites_a_saved_row_and_seals_a_password()
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var seed = Seed("smtp+starttls://mailer:s%40cret-seed@smtp.example.com");

        // No row yet: seeded with the decoded user and a sealed password, port from the scheme.
        await using (var first = OrvanoProcess.Start(["api"], new Dictionary<string, string>(seed) { ["ORVANO_DB_URL"] = database.AppUrl, ["ORVANO_SETUP_TOKEN"] = OrvanoProcess.SetupToken }, listen: true))
        {
            await first.WaitUntilListeningAsync();
            Assert.DoesNotContain("s@cret-seed", first.Output, StringComparison.Ordinal);
        }

        await using (var conn = await database.Superuser.OpenConnectionAsync(Ct))
        {
            await using var read = new NpgsqlCommand(
                "SELECT host, port, security, username, password_ciphertext FROM orvano.messaging_smtp_settings WHERE project_id = 'console'", conn);
            await using var row = await read.ExecuteReaderAsync(Ct);
            Assert.True(await row.ReadAsync(Ct));
            Assert.Equal("smtp.example.com", row.GetString(0));
            Assert.Equal(587, row.GetInt32(1));
            Assert.Equal("starttls", row.GetString(2));
            Assert.Equal("mailer", row.GetString(3));
            var sealedPassword = (byte[])row[4];
            Assert.DoesNotContain("s@cret-seed", Encoding.UTF8.GetString(sealedPassword), StringComparison.Ordinal);
        }

        // A row changed in the console wins over the setting on every later start.
        await TestDatabase.ExecuteAsync(database.Superuser,
            "UPDATE orvano.messaging_smtp_settings SET host = 'saved.example.com', updated_by_user_id = gen_random_uuid() WHERE project_id = 'console'");
        await using (var second = OrvanoProcess.Start(["api"], new Dictionary<string, string>(seed) { ["ORVANO_DB_URL"] = database.AppUrl, ["ORVANO_SETUP_TOKEN"] = OrvanoProcess.SetupToken }, listen: true))
        {
            await second.WaitUntilListeningAsync();
            Assert.DoesNotContain("Seeded the install SMTP settings", second.Output, StringComparison.Ordinal);
        }

        Assert.Equal("saved.example.com", await TestDatabase.ScalarAsync<string>(
            database.Superuser, "SELECT host FROM orvano.messaging_smtp_settings WHERE project_id = 'console'"));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    [InlineData("realtime")]
    public async Task An_invalid_seed_stops_every_role_without_printing_the_value(string role)
    {
        await using var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        // smtp:// with a user would send it unencrypted.
        var env = new Dictionary<string, string>(Seed("smtp://mailer:leaked-password@smtp.example.com:25"))
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ORVANO_DB_ADMIN_URL"] = database.AdminUrl,
        };

        await using var orvano = OrvanoProcess.Start([role], env, listen: true);
        await orvano.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, orvano.ExitCode);
        Assert.Contains("ORVANO_INSTALL_SMTP_URL can't carry a user or password with smtp://", orvano.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("leaked-password", orvano.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp.example.com", orvano.Output, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Seed(string url) => new()
    {
        ["ORVANO_INSTALL_SMTP_URL"] = url,
        ["ORVANO_INSTALL_SMTP_FROM"] = From,
    };

    private static async Task<string> FixturesAsync(string admin)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(path, ConsoleSignIn.Fixtures([admin]), Ct);
        return path;
    }

    private static async Task<JsonElement> PostAsync(HttpClient http, string url, object body, string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        await ConsoleSignIn.AuthorizeAsync(http, request, session, Ct);
        using var response = await http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST {url}: {response.StatusCode} {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
