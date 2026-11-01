using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 ACC-1, ACC-2, AC-8, AC-19, AC-22: the account and its password.
public sealed class ConsoleAccountTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Account = "/v1/console/account";
    private const string Password = "/v1/console/account/password";
    private const string NewPassword = "a brand new passphrase";

    private static readonly TimeSpan JustTooOld = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);

    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("account", Token));
        _owner = await _host.CreateOwnerAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task<HttpResponseMessage> PatchAsync(string body, string? ifMatch = null, ConsoleSession? session = null) =>
        _host.SendAsync(HttpMethod.Patch, Account, body, session ?? _owner, ifMatch: ifMatch);

    private Task<HttpResponseMessage> ChangeAsync(string current, string next, ConsoleSession? session = null) =>
        _host.PostAsync(Password, JsonSerializer.Serialize(new { currentPassword = current, newPassword = next }), session ?? _owner);

    private Task<Operator> OperatorAsync() =>
        _host.WithDatabaseAsync(db => db.Operators.AsNoTracking().SingleAsync(x => x.Id == _owner.OperatorId, Token));

    private static async Task<(ConsoleSession Session, JsonElement Body)> ReadRotatedAsync(HttpResponseMessage response, ConsoleSession before)
    {
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        string cookie = ConsoleTestHost.SetCookie(response)!.Split(';')[0];
        return (before with { Cookie = cookie, CsrfToken = body.GetProperty("csrfToken").GetString()! }, body);
    }

    // PATCH

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0017:AC-19"), Trait("Spec", "0017:AC-22")]
    public async Task Patch_changes_the_name_and_language_and_answers_the_account_with_its_version()
    {
        using HttpResponseMessage response = await PatchAsync("""{"name":"  Grace  ","locale":"ar"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Grace");
        body.GetProperty("locale").GetString().ShouldBe("ar");
        body.GetProperty("email").GetString().ShouldBe(ConsoleTestHost.OwnerEmail);
        response.Headers.ETag!.Tag.ShouldBe($"\"{body.GetProperty("version").GetString()}\"");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        _host.Audit.Events[^1].Action.ShouldBe("operator.updated");
        _host.Audit.Events[^1].TargetId.ShouldBe(_owner.OperatorId);
    }

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_clears_the_language_with_null_and_leaves_it_when_left_out()
    {
        (await PatchAsync("""{"locale":"en"}""")).Dispose();

        (await PatchAsync("""{"name":"Grace"}""")).Dispose();
        (await OperatorAsync()).Locale.ShouldBe("en");

        using HttpResponseMessage response = await PatchAsync("""{"locale":null}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OperatorAsync()).Locale.ShouldBeNull();
    }

    [Theory, Trait("Spec", "0017:ACC-1")]
    [InlineData("""{"locale":"fr"}""", FieldCodes.InvalidValue)]
    [InlineData("""{"name":"   "}""", FieldCodes.Required)]
    [InlineData("""{"name":null}""", FieldCodes.Required)]
    [InlineData("""{"email":"not an email"}""", FieldCodes.InvalidEmail)]
    public async Task Patch_fails_on_the_field_when_a_value_is_refused(string body, string code)
    {
        using HttpResponseMessage response = await PatchAsync(body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.FieldCodeAsync(response)).ShouldBe(code);
    }

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0017:SES-8"), Trait("Spec", "0017:AC-8"), Trait("Spec", "0017:AC-19")]
    public async Task Patch_changes_the_email_and_sign_in_follows_it_when_the_check_is_recent()
    {
        using HttpResponseMessage response = await PatchAsync("""{"email":" Grace@Example.com "}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Operator account = await OperatorAsync();
        account.Email.ShouldBe("Grace@Example.com");
        account.EmailNormalized.ShouldBe("grace@example.com");
        await _host.SignInAsync("grace@example.com");
    }

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0017:SES-8"), Trait("Spec", "0017:AC-8")]
    public async Task Patch_answers_reauth_required_only_when_the_email_changes_and_the_check_is_old()
    {
        _host.Clock.Advance(JustTooOld);

        using HttpResponseMessage changed = await PatchAsync("""{"email":"grace@example.com"}""");
        using HttpResponseMessage same = await PatchAsync($$"""{"email":"{{ConsoleTestHost.OwnerEmail}}","name":"Grace"}""");

        changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ConsoleTestHost.ErrorCodeAsync(changed)).ShouldBe("reauth_required");
        same.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OperatorAsync()).Email.ShouldBe(ConsoleTestHost.OwnerEmail);
    }

    [Fact, Trait("Spec", "0017:ACC-1")]
    public async Task Patch_fails_with_not_unique_when_another_operator_has_the_email()
    {
        await _host.AddOperatorAsync("taken@example.com");

        using HttpResponseMessage response = await PatchAsync("""{"email":"TAKEN@example.com"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.FieldCodeAsync(response)).ShouldBe(FieldCodes.NotUnique);
    }

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0014:CNC-3")]
    public async Task Patch_answers_412_when_the_version_is_stale()
    {
        string before = (await OperatorAsync()).Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        (await PatchAsync("""{"name":"First"}""")).Dispose();

        using HttpResponseMessage response = await PatchAsync("""{"name":"Second"}""", before);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.VersionConflict);
        (await OperatorAsync()).Name.ShouldBe("First");
    }

    // Password

    [Fact, Trait("Spec", "0017:ACC-2"), Trait("Spec", "0006:TOK-6"), Trait("Spec", "0006:TOK-9"), Trait("Spec", "0017:AC-19"), Trait("Spec", "0017:AC-22")]
    public async Task Password_change_ends_every_other_session_and_replaces_this_one()
    {
        ConsoleSession other = await _host.SignInAsync();
        await _host.WithDatabaseAsync(db =>
        {
            db.OperatorResetTokens.Add(new OperatorResetToken
            {
                OperatorId = _owner.OperatorId,
                TokenHash = new byte[32],
                ExpiresAt = _host.Clock.GetUtcNow().AddMinutes(30),
                CreatedAt = _host.Clock.GetUtcNow(),
            });
            return db.SaveChangesAsync(Token);
        });
        _host.Clock.Advance(JustTooOld);

        using HttpResponseMessage response = await ChangeAsync(ConsoleTestHost.OwnerPassword, NewPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (ConsoleSession rotated, JsonElement body) = await ReadRotatedAsync(response, _owner);
        rotated.Cookie.ShouldNotBe(_owner.Cookie);
        rotated.CsrfToken.ShouldNotBe(_owner.CsrfToken);
        body.GetProperty("operator").GetProperty("id").GetGuid().ShouldBe(_owner.OperatorId);

        foreach (ConsoleSession ended in new[] { _owner, other })
        {
            using HttpResponseMessage refused = await _host.GetAsync("/v1/console/session", ended);
            refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ConsoleTestHost.ErrorCodeAsync(refused)).ShouldBe("session_expired");
        }

        using (HttpResponseMessage current = await _host.GetAsync("/v1/console/session", rotated))
        {
            current.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // SES-8: a password change counts as a check.
        using (HttpResponseMessage recent = await PatchAsync("""{"email":"grace@example.com"}""", session: rotated))
        {
            recent.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await _host.SignInAsync("grace@example.com", NewPassword);
        (await _host.WithDatabaseAsync(db => db.OperatorResetTokens.CountAsync(Token))).ShouldBe(0);
        (await OperatorAsync()).PasswordChangedAt.ShouldBe(_host.Clock.GetUtcNow());
        AuditEvent revoked = _host.Audit.Events.Single(x => x.Action == "session.revoked");
        revoked.Metadata!["count"].ShouldBe("2");
        _host.Audit.Events.Single(x => x.Action == "operator.password_changed").Result.ShouldBe(AuditResult.Success);
    }

    [Fact, Trait("Spec", "0017:ACC-2"), Trait("Spec", "0017:LIM-1"), Trait("Spec", "0017:AC-19")]
    public async Task Password_change_fails_on_the_field_and_counts_toward_the_throttle_when_the_current_password_is_wrong()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage wrong = await ChangeAsync("not it", NewPassword);
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            JsonElement error = (await ConsoleTestHost.ReadJsonAsync(wrong)).RootElement.GetProperty("errors")[0];
            error.GetProperty("field").GetString().ShouldBe("currentPassword");
            error.GetProperty("code").GetString().ShouldBe(FieldCodes.InvalidPassword);
        }

        using HttpResponseMessage response = await ChangeAsync(ConsoleTestHost.OwnerPassword, NewPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        _host.Audit.Events.ShouldContain(x => x.Action == "operator.password_changed" && x.Result == AuditResult.Failure);
    }

    [Theory, Trait("Spec", "0017:ACC-2"), Trait("Spec", "0017:PSW-1")]
    [InlineData("short", FieldCodes.TooShort)]
    [InlineData("sunshine", FieldCodes.PasswordTooCommon)]
    public async Task Password_change_fails_on_the_new_password_and_changes_nothing_when_the_policy_refuses_it(string next, string code)
    {
        int hashes = _host.Hasher.Hashes;

        using HttpResponseMessage response = await ChangeAsync(ConsoleTestHost.OwnerPassword, next);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement error = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe("newPassword");
        error.GetProperty("code").GetString().ShouldBe(code);
        _host.Hasher.Hashes.ShouldBe(hashes);
        await _host.SignInAsync();
    }

    [Fact, Trait("Spec", "0017:ACC-1"), Trait("Spec", "0017:ACX-1")]
    public async Task Account_routes_answer_401_with_no_session()
    {
        using HttpResponseMessage patch = await _host.SendAsync(HttpMethod.Patch, Account, """{"name":"Mallory"}""");
        using HttpResponseMessage password = await _host.PostAsync(Password, """{"currentPassword":"a","newPassword":"b"}""");

        patch.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        password.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
