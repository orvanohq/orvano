using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Auth.Passwords;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 SES-1 to SES-7, LIM-1, AC-5 to AC-7: sign in, the session, and the state change check.
public sealed class ConsoleSessionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/v1/console/session";

    private readonly List<ConsoleTestHost> _hosts = [];
    private string _database = string.Empty;
    private ConsoleTestHost _host = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await postgres.CreateMigratedDatabaseAsync("session", Token);
        _host = Start();
    }

    public ValueTask DisposeAsync()
    {
        foreach (ConsoleTestHost host in _hosts)
        {
            host.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private ConsoleTestHost Start(Dictionary<string, string>? settings = null)
    {
        var host = new ConsoleTestHost(postgres, _database, settings: settings);
        _hosts.Add(host);
        return host;
    }

    private static string SignInBody(string email, string phrase) => JsonSerializer.Serialize(new { email, password = phrase });

    private Task<int> ExecuteAsync(string sql) => _host.WithDatabaseAsync(db => db.Database.ExecuteSqlRawAsync(sql, Token));

    // Sign in

    [Fact, Trait("Spec", "0017:SES-3"), Trait("Spec", "0017:SES-1"), Trait("Spec", "0006:CON-1"), Trait("Spec", "0017:AC-5")]
    public async Task Sign_in_answers_201_with_a_cookie_that_matches_CON_1_when_the_details_are_right()
    {
        await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.PostAsync(Path, SignInBody(" OWNER@example.com ", ConsoleTestHost.OwnerPhrase));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        string cookie = ConsoleTestHost.SetCookie(response)!;
        cookie.ShouldStartWith("__Host-orvano_session=");
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("expires=", Case.Insensitive);
        cookie.ShouldNotContain("domain", Case.Insensitive);
        await _host.WithDatabaseAsync(async db =>
        {
            (await db.OperatorSessions.CountAsync(Token)).ShouldBe(2); // TOK-10: every sign in makes a row
            (await db.Operators.SingleAsync(Token)).LastSignInAt.ShouldBe(_host.Clock.GetUtcNow());
            return true;
        });
    }

    [Fact, Trait("Spec", "0006:CON-1"), Trait("Spec", "0017:SES-1")]
    public async Task Sign_in_uses_the_plain_cookie_name_without_Secure_when_insecure_cookies_are_on()
    {
        ConsoleTestHost host = Start(new() { ["INSECURE_COOKIES"] = "true" });
        await host.CreateOwnerAsync();

        using HttpResponseMessage response = await host.PostAsync(Path, SignInBody(ConsoleTestHost.OwnerEmail, ConsoleTestHost.OwnerPhrase));

        string cookie = ConsoleTestHost.SetCookie(response)!;
        cookie.ShouldStartWith("orvano_session=");
        cookie.ShouldNotContain("secure", Case.Insensitive);
    }

    [Fact, Trait("Spec", "0017:SES-3"), Trait("Spec", "0006:PWD-5"), Trait("Spec", "0017:AC-5")]
    public async Task Sign_in_answers_the_same_401_with_the_same_hash_work_when_the_password_is_wrong_the_email_unknown_or_the_operator_disabled()
    {
        await _host.CreateOwnerAsync();
        var bodies = new List<string>();
        var verifies = new List<int>();

        foreach (string body in new[]
        {
            SignInBody(ConsoleTestHost.OwnerEmail, "the wrong password"),
            SignInBody("nobody@example.com", "the wrong password"),
        })
        {
            int before = _host.Hasher.Verifies;
            using HttpResponseMessage response = await _host.PostAsync(Path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("invalid_credentials");
            bodies.Add((await ConsoleTestHost.ReadJsonAsync(response)).RootElement.GetProperty("detail").GetString()!);
            verifies.Add(_host.Hasher.Verifies - before);
        }

        await ExecuteAsync("UPDATE platform.operator SET status = 'disabled'");
        int beforeDisabled = _host.Hasher.Verifies;
        using HttpResponseMessage disabled = await _host.PostAsync(Path, SignInBody(ConsoleTestHost.OwnerEmail, ConsoleTestHost.OwnerPhrase));
        disabled.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(disabled)).ShouldBe("invalid_credentials");
        bodies.Add((await ConsoleTestHost.ReadJsonAsync(disabled)).RootElement.GetProperty("detail").GetString()!);
        verifies.Add(_host.Hasher.Verifies - beforeDisabled);

        bodies.Distinct().ShouldHaveSingleItem();
        verifies.ShouldBe([1, 1, 1]);
    }

    [Fact, Trait("Spec", "0017:LIM-1"), Trait("Spec", "0017:AC-5")]
    public async Task Sign_in_answers_429_with_retry_after_on_the_sixth_try_even_with_the_right_password()
    {
        await _host.CreateOwnerAsync();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await _host.PostAsync(Path, SignInBody(ConsoleTestHost.OwnerEmail, "the wrong password"));
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        int verifies = _host.Hasher.Verifies;
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        using HttpResponseMessage response = await _host.PostAsync(Path, SignInBody(ConsoleTestHost.OwnerEmail, ConsoleTestHost.OwnerPhrase));

        response.StatusCode.ShouldBe((HttpStatusCode)429);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.RateLimited);
        response.Headers.RetryAfter?.Delta.ShouldBe(TimeSpan.FromMinutes(10));
        _host.Hasher.Verifies.ShouldBe(verifies);

        _host.Clock.Advance(TimeSpan.FromMinutes(10));
        await _host.SignInAsync();
    }

    [Fact, Trait("Spec", "0006:PWD-2"), Trait("Spec", "0017:AC-5")]
    public async Task Sign_in_rewrites_the_hash_when_the_stored_parameters_are_weaker()
    {
        await _host.CreateOwnerAsync();
        string weak = new Argon2idPasswordHasher(Argon2Cost.Floor).Hash(ConsoleTestHost.OwnerPhrase);
        await _host.WithDatabaseAsync(db => db.Operators.ExecuteUpdateAsync(s => s.SetProperty(x => x.PasswordHash, weak), Token));

        await _host.SignInAsync();

        (await _host.WithDatabaseAsync(db => db.Operators.Select(x => x.PasswordHash).SingleAsync(Token))).ShouldContain("m=65536,t=3,p=1");
    }

    [Fact, Trait("Spec", "0002:TST-4")]
    public async Task Sign_in_answers_validation_failed_when_a_field_is_missing()
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, """{"email":"a@example.com"}""");

        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:SES-3")]
    public async Task Sign_in_answers_403_when_the_origin_is_foreign()
    {
        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Post, Path, SignInBody("a@example.com", "x"), origin: "http://evil.example");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Spec", "0017:AUS-3"), Trait("Spec", "0017:AC-22")]
    public async Task Sign_in_audits_a_failure_with_no_target_and_no_email()
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, SignInBody("typed@example.com", "the wrong password"));

        AuditEvent failure = _host.Audit.Events.ShouldHaveSingleItem();
        failure.Action.ShouldBe("operator.sign_in");
        failure.Result.ShouldBe(AuditResult.Failure);
        failure.TargetId.ShouldBeNull();
        failure.Metadata.ShouldBeNull();
    }

    // The session

    [Fact, Trait("Spec", "0017:SES-5"), Trait("Spec", "0017:AC-6")]
    public async Task Session_get_answers_the_operator_memberships_and_csrf_token_when_the_session_is_live()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.GetAsync(Path, session);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("csrfToken").GetString().ShouldBe(session.CsrfToken);
        body.GetProperty("operator").GetProperty("email").GetString().ShouldBe(ConsoleTestHost.OwnerEmail);
        body.GetProperty("memberships").GetArrayLength().ShouldBe(1);
    }

    [Fact, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:AC-6")]
    public async Task Session_get_answers_401_unauthorized_when_there_is_no_cookie_or_an_unknown_one()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();
        using HttpResponseMessage none = await _host.GetAsync(Path);
        (await ConsoleTestHost.ErrorCodeAsync(none)).ShouldBe(ErrorCodes.Unauthorized);

        using HttpResponseMessage unknown = await _host.GetAsync(Path, session with { Cookie = "__Host-orvano_session=forged" });

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(unknown)).ShouldBe(ErrorCodes.Unauthorized);
    }

    [Theory, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:SES-10"), Trait("Spec", "0017:AC-6")]
    [InlineData(12 * 60 + 1)]
    [InlineData(14 * 24 * 60)]
    public async Task Session_get_answers_session_expired_and_clears_the_cookie_when_a_limit_passed(int minutes)
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        _host.Clock.Advance(TimeSpan.FromMinutes(minutes));
        using HttpResponseMessage response = await _host.GetAsync(Path, session);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.SessionExpired);
        ConsoleTestHost.SetCookie(response).ShouldNotBeNull().ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
    }

    [Fact, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:SES-10")]
    public async Task Session_stays_live_past_the_idle_limit_when_it_keeps_being_used()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        foreach (int _ in Enumerable.Range(0, 3))
        {
            _host.Clock.Advance(TimeSpan.FromHours(7));
            using HttpResponseMessage response = await _host.GetAsync(Path, session);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact, Trait("Spec", "0017:SES-4")]
    public async Task Session_writes_last_seen_at_at_most_once_a_minute()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();
        DateTimeOffset start = _host.Clock.GetUtcNow();

        _host.Clock.Advance(TimeSpan.FromSeconds(59));
        (await _host.GetAsync(Path, session)).Dispose();
        (await LastSeenAsync()).ShouldBe(start);

        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        (await _host.GetAsync(Path, session)).Dispose();
        (await LastSeenAsync()).ShouldBe(start + TimeSpan.FromMinutes(1));

        Task<DateTimeOffset> LastSeenAsync() => _host.WithDatabaseAsync(db => db.OperatorSessions.Select(x => x.LastSeenAt).SingleAsync(Token));
    }

    [Fact, Trait("Spec", "0017:SES-4"), Trait("Spec", "0017:AC-6")]
    public async Task Session_answers_401_on_the_next_request_when_its_row_is_revoked()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();
        await ExecuteAsync("UPDATE platform.operator_session SET revoked_at = now()");

        using HttpResponseMessage response = await _host.GetAsync(Path, session);

        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.SessionExpired);
    }

    [Fact, Trait("Spec", "0017:SES-4")]
    public async Task Session_answers_401_unauthorized_when_the_operator_is_disabled()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();
        await ExecuteAsync("UPDATE platform.operator SET status = 'disabled'");

        using HttpResponseMessage response = await _host.GetAsync(Path, session);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.Unauthorized);
    }

    [Fact, Trait("Spec", "0017:SES-6"), Trait("Spec", "0017:AC-6"), Trait("Spec", "0017:AC-22")]
    public async Task Sign_out_ends_the_session_at_once_and_clears_the_cookie()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Delete, Path, session: session);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        ConsoleTestHost.SetCookie(response).ShouldNotBeNull().ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        using HttpResponseMessage after = await _host.GetAsync(Path, session);
        (await ConsoleTestHost.ErrorCodeAsync(after)).ShouldBe(ErrorCodes.SessionExpired);
        _host.Audit.Events[^1].Action.ShouldBe("operator.sign_out");
    }

    // The state change check (SES-7)

    public static TheoryData<string?, string?> BadHeaders => new()
    {
        { ConsoleTestHost.PublicUrl, string.Empty },
        { ConsoleTestHost.PublicUrl, "AAAA" },
        { ConsoleTestHost.PublicUrl, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" },
        { "http://evil.example", null },
        { null, null },
    };

    [Theory, MemberData(nameof(BadHeaders)), Trait("Spec", "0017:SES-7"), Trait("Spec", "0017:AC-7")]
    public async Task State_change_answers_403_before_the_body_when_the_csrf_token_or_origin_is_wrong(string? origin, string? csrf)
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        // The body is invalid on purpose: a 400 here would mean binding ran first.
        using HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post, $"/v1/console/organizations/{session.OrganizationId}/projects", "{", session, origin, csrf ?? session.CsrfToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.Forbidden);
    }

    [Fact, Trait("Spec", "0017:SES-7"), Trait("Spec", "0017:AC-7")]
    public async Task State_change_answers_403_when_the_csrf_header_is_missing()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete, Path, session: session with { CsrfToken = null! });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Spec", "0017:SES-7")]
    public async Task State_change_passes_when_the_origin_and_token_match()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        await _host.CreateProjectAsync(session);
    }

    [Fact, Trait("Spec", "0017:SES-7")]
    public async Task Read_needs_no_csrf_token()
    {
        ConsoleSession session = await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.GetAsync(Path, session with { CsrfToken = null! });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact, Trait("Spec", "0017:ACX-4"), Trait("Spec", "0017:AC-7")]
    public async Task Public_state_changing_console_endpoints_all_require_the_console_origin()
    {
        // The host starting at all proves it (AccessEndpointCheck): each answers 403 for a foreign origin.
        foreach (string path in new[] { "/v1/console/setup/token", "/v1/console/setup/owner", Path })
        {
            using HttpResponseMessage response = await _host.SendAsync(HttpMethod.Post, path, "{}", origin: "http://evil.example");
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }
}
