using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 RST-1 to RST-3, LIM-3, AC-18, AC-22: `orvano admin reset-operator` and the link it prints. Most tests call what
// the command calls; the last ones run the real process, for its exit codes and its standard output.
public sealed class OperatorResetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/v1/console/password-resets";
    private const string NewPassword = "a brand new passphrase";

    private string _database = string.Empty;
    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync("reset", Token);
        _host = new ConsoleTestHost(postgres, _database);
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string TokenOf(string link) => link[(link.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..];

    private async Task<string> IssueAsync(string email = ConsoleTestHost.OwnerEmail) =>
        TokenOf((await PlatformSetup.IssueResetLinkAsync(_host.Services, email, Token))!);

    private Task<HttpResponseMessage> RedeemAsync(string token, string password = NewPassword) =>
        _host.PostAsync(Path, JsonSerializer.Serialize(new { token, password }));

    private Task<List<OperatorResetToken>> RowsAsync() =>
        _host.WithDatabaseAsync(db => db.OperatorResetTokens.AsNoTracking().ToListAsync(Token));

    private static async Task ShouldBeInvalidAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("reset_token_invalid");
    }

    [Fact, Trait("Spec", "0017:RST-1"), Trait("Spec", "0006:TOK-9"), Trait("Spec", "0017:AC-18")]
    public async Task Issue_stores_only_the_hash_and_gives_a_link_that_lives_30_minutes()
    {
        string link = (await PlatformSetup.IssueResetLinkAsync(_host.Services, " OWNER@example.com ", Token))!;

        link.ShouldStartWith($"{ConsoleTestHost.PublicUrl}/console/reset#token=orv_reset_");
        OperatorResetToken row = (await RowsAsync()).ShouldHaveSingleItem();
        row.OperatorId.ShouldBe(_owner.OperatorId);
        row.ExpiresAt.ShouldBe(_host.Clock.GetUtcNow().AddMinutes(30));
        row.UsedAt.ShouldBeNull();
        row.TokenHash.ShouldBe(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(TokenOf(link))));
    }

    [Fact, Trait("Spec", "0017:RST-1")]
    public async Task Issue_returns_nothing_and_stores_nothing_when_the_email_is_unknown()
    {
        (await PlatformSetup.IssueResetLinkAsync(_host.Services, "nobody@example.com", Token)).ShouldBeNull();

        (await RowsAsync()).ShouldBeEmpty();
    }

    [Fact, Trait("Spec", "0017:RST-3"), Trait("Spec", "0006:TOK-6"), Trait("Spec", "0017:AC-18"), Trait("Spec", "0017:AC-22")]
    public async Task Redeem_sets_the_password_and_ends_every_session()
    {
        ConsoleSession other = await _host.SignInAsync();
        string token = await IssueAsync();

        using HttpResponseMessage response = await RedeemAsync(token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (ConsoleSession ended in new[] { _owner, other })
        {
            using HttpResponseMessage refused = await _host.GetAsync("/v1/console/session", ended);
            (await ConsoleTestHost.ErrorCodeAsync(refused)).ShouldBe("session_expired");
        }

        await _host.SignInAsync(password: NewPassword);
        (await RowsAsync()).ShouldHaveSingleItem().UsedAt.ShouldBe(_host.Clock.GetUtcNow());
        _host.Audit.Events.ShouldContain(x => x.Action == "operator.password_reset" && x.TargetId == _owner.OperatorId);
        _host.Audit.Events.Single(x => x.Action == "session.revoked").Metadata!["count"].ShouldBe("2");
    }

    [Fact, Trait("Spec", "0017:RST-3"), Trait("Spec", "0017:AC-18")]
    public async Task Redeem_answers_reset_token_invalid_when_the_link_was_used()
    {
        string token = await IssueAsync();
        (await RedeemAsync(token)).Dispose();

        using HttpResponseMessage response = await RedeemAsync(token, "another fine passphrase");

        await ShouldBeInvalidAsync(response);
        await _host.SignInAsync(password: NewPassword);
    }

    [Fact, Trait("Spec", "0017:RST-3"), Trait("Spec", "0017:AC-18")]
    public async Task Redeem_answers_reset_token_invalid_when_the_link_expired()
    {
        string token = await IssueAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(30));

        using HttpResponseMessage response = await RedeemAsync(token);

        await ShouldBeInvalidAsync(response);
        await _host.SignInAsync();
    }

    [Fact, Trait("Spec", "0017:RST-1"), Trait("Spec", "0017:RST-3"), Trait("Spec", "0017:AC-18")]
    public async Task Redeem_answers_reset_token_invalid_when_the_link_was_replaced()
    {
        string first = await IssueAsync();
        string second = await IssueAsync();

        using HttpResponseMessage replaced = await RedeemAsync(first);
        using HttpResponseMessage newest = await RedeemAsync(second);

        await ShouldBeInvalidAsync(replaced);
        newest.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0017:RST-3")]
    public async Task Redeem_answers_reset_token_invalid_when_the_token_is_wrong()
    {
        using HttpResponseMessage response = await RedeemAsync("orv_reset_not-a-real-token");

        await ShouldBeInvalidAsync(response);
    }

    [Theory, Trait("Spec", "0017:RST-3"), Trait("Spec", "0017:PSW-1")]
    [InlineData("short", FieldCodes.TooShort)]
    [InlineData("sunshine", FieldCodes.PasswordTooCommon)]
    public async Task Redeem_leaves_the_link_usable_when_the_policy_refuses_the_password(string password, string code)
    {
        string token = await IssueAsync();

        using HttpResponseMessage refused = await RedeemAsync(token, password);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.FieldCodeAsync(refused)).ShouldBe(code);
        (await RowsAsync()).ShouldHaveSingleItem().UsedAt.ShouldBeNull();
        using HttpResponseMessage response = await RedeemAsync(token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0017:LIM-3")]
    public async Task Redeem_clears_the_operator_s_throttle()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            (await _host.PostAsync("/v1/console/session", JsonSerializer.Serialize(new { email = ConsoleTestHost.OwnerEmail, password = "not it" }))).Dispose();
        }

        (await RedeemAsync(await IssueAsync())).Dispose();

        await _host.SignInAsync(password: NewPassword);
    }

    [Fact, Trait("Spec", "0017:RST-3"), Trait("Spec", "0006:CON-2")]
    public async Task Redeem_answers_403_with_a_foreign_origin()
    {
        string token = await IssueAsync();

        using HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post, Path, JsonSerializer.Serialize(new { token, password = NewPassword }), origin: "https://evil.example.com");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RowsAsync()).ShouldHaveSingleItem().UsedAt.ShouldBeNull();
    }

    private Task<OrvanoCli.Result> RunAsync(params string[] args)
    {
        var environment = postgres.DatabaseEnvironment.ToDictionary();
        environment["ORVANO_DATABASE__NAME"] = _database;
        environment["ORVANO_PUBLIC_URL"] = ConsoleTestHost.PublicUrl;
        return OrvanoCli.RunAsync(["admin", "reset-operator", .. args], environment, Token);
    }

    [Fact, Trait("Spec", "0017:RST-1"), Trait("Spec", "0017:AC-18")]
    public async Task Command_prints_only_the_link_on_standard_output_and_exits_0()
    {
        OrvanoCli.Result result = await RunAsync("--email", ConsoleTestHost.OwnerEmail);

        result.ExitCode.ShouldBe(0, result.Error);
        string link = result.Output.Trim();
        link.ShouldStartWith($"{ConsoleTestHost.PublicUrl}/console/reset#token=orv_reset_");
        link.ShouldNotContain('\n');
        result.Error.ShouldContain("sessions end when the link is used");
        using HttpResponseMessage response = await RedeemAsync(TokenOf(link));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact, Trait("Spec", "0017:RST-1")]
    public async Task Command_exits_1_when_the_email_is_unknown()
    {
        OrvanoCli.Result result = await RunAsync("--email", "nobody@example.com");

        result.ExitCode.ShouldBe(1);
        result.Output.ShouldBeEmpty();
        result.Error.ShouldContain("No operator has that email");
    }
}
