using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Orvano.Auth.Application;
using Orvano.Core.Http;
using Orvano.Core.Secrets;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 AC-6, AC-7, AC-21: issuing and checking access tokens against real signing keys, with a clock the test
// moves, so expiry and the 30 second leeway are exact.
public class AccessTokenTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_token_verifies_until_its_expiry_plus_30_seconds_then_counts_as_expired()
    {
        await using var database = await Migrated();
        var (tokens, clock) = Build(database);
        var user = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var issued = await tokens.IssueAsync("shop", user, session, emailVerified: false, Ct);

        clock.Advance(TimeSpan.FromSeconds(900 + 30));
        var atLeeway = await tokens.ValidateAsync(issued.Token, "shop", Ct);
        clock.Advance(TimeSpan.FromSeconds(1));
        var past = await tokens.ValidateAsync(issued.Token, "shop", Ct);

        Assert.Equal(new TokenIdentity(user, session, Start.AddSeconds(900)), atLeeway.Identity);
        Assert.Null(past.Identity);
        Assert.Equal(TokenRejection.Expired, past.Rejection);
    }

    [Fact]
    public async Task A_token_of_one_project_is_invalid_for_another()
    {
        await using var database = await Migrated();
        var (tokens, _) = Build(database);
        var issued = await tokens.IssueAsync("shop", Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct);
        await tokens.IssueAsync("blog", Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct);

        var check = await tokens.ValidateAsync(issued.Token, "blog", Ct);

        Assert.Equal(TokenRejection.Invalid, check.Rejection);
        Assert.Null(check.Identity);
    }

    [Fact]
    public async Task Unsigned_and_hmac_tokens_are_refused_even_with_valid_claims()
    {
        await using var database = await Migrated();
        var (tokens, _) = Build(database);
        var issued = await tokens.IssueAsync("shop", Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct);
        var parts = issued.Token.Split('.');
        var kid = System.Text.Json.JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement.GetProperty("kid").GetString();

        var none = Encode($$"""{"alg":"none","typ":"JWT","kid":"{{kid}}"}""") + "." + parts[1] + ".";
        var hsHeader = Encode($$"""{"alg":"HS256","typ":"JWT","kid":"{{kid}}"}""") + "." + parts[1];
        var hs = hsHeader + "." + Base64Url.EncodeToString(HMACSHA256.HashData("secret"u8, Encoding.ASCII.GetBytes(hsHeader)));
        var tampered = parts[0] + "." + parts[1] + "." + parts[2][..^2] + (parts[2][^2..] == "AA" ? "AB" : "AA");

        foreach (var forged in new[] { none, hs, tampered, "", "a.b", "a.b.c" })
            Assert.Equal(TokenRejection.Invalid, (await tokens.ValidateAsync(forged, "shop", Ct)).Rejection);
    }

    [Fact]
    public async Task Tokens_from_before_a_restart_still_verify_with_the_stored_key()
    {
        await using var database = await Migrated();
        var (first, _) = Build(database);
        var issued = await first.IssueAsync("shop", Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct);

        var (second, _) = Build(database);

        Assert.NotNull((await second.ValidateAsync(issued.Token, "shop", Ct)).Identity);
    }

    [Fact]
    public async Task Racing_first_issues_end_with_one_active_key()
    {
        await using var database = await Migrated();

        var issued = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var (tokens, _) = Build(database);
            return (tokens, await tokens.IssueAsync("shop", Guid.CreateVersion7(), Guid.CreateVersion7(), emailVerified: false, Ct));
        }));

        Assert.Equal(1L, await TestDatabase.ScalarAsync<long>(database.Superuser, "SELECT count(*) FROM orvano.auth_signing_keys"));
        foreach (var (tokens, token) in issued) Assert.NotNull((await tokens.ValidateAsync(token.Token, "shop", Ct)).Identity);
    }

    private async Task<TestDatabase> Migrated()
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        return database;
    }

    private static (AccessTokens Tokens, ManualClock Clock) Build(TestDatabase database)
    {
        var clock = new ManualClock(Start);
        var secrets = new SecretBox(MasterKeys.Parse(OrvanoProcess.MasterKeys));
        var keys = new SigningKeys(database.App, secrets, clock);
        return (new AccessTokens(keys, PublicUrl.Parse("https://orvano.example.com"), clock), clock);
    }

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
