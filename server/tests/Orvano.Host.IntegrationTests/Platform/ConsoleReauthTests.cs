using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 SES-8, LIM-1, AC-8: the recent password check.
public sealed class ConsoleReauthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Reauth = "/v1/console/session/reauth";

    private static readonly TimeSpan JustTooOld = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);

    private ConsoleTestHost _host = null!;
    private ConsoleSession _owner = null!;
    private Guid _projectId;
    private Guid _keyId;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("reauth", Token));
        _owner = await _host.CreateOwnerAsync();
        _projectId = await _host.CreateProjectAsync(_owner);
        await _host.CreateKeyAsync(_owner, _projectId);
        _keyId = await _host.WithDatabaseAsync(db => db.ApiKeys.IgnoreQueryFilters().Select(x => x.Id).SingleAsync(Token));
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    private (HttpMethod Method, string Path)[] Protected =>
    [
        (HttpMethod.Post, $"/v1/console/projects/{_projectId}/keys"),
        (HttpMethod.Post, $"/v1/console/projects/{_projectId}/keys/{_keyId}/revoke"),
        (HttpMethod.Post, $"/v1/console/projects/{_projectId}/keys/{_keyId}/regenerate"),
        (HttpMethod.Delete, $"/v1/console/projects/{_projectId}"),
    ];

    private Task<HttpResponseMessage> ReauthAsync(string password) =>
        _host.PostAsync(Reauth, JsonSerializer.Serialize(new { password }), _owner);

    [Fact, Trait("Spec", "0017:SES-8"), Trait("Spec", "0006:CON-7"), Trait("Spec", "0017:AC-8")]
    public async Task Every_protected_route_answers_403_reauth_required_before_the_body_when_the_last_check_is_too_old()
    {
        _host.Clock.Advance(JustTooOld);

        foreach ((HttpMethod method, string path) in Protected)
        {
            // The body is invalid on purpose: a 400 here would mean binding ran first.
            using HttpResponseMessage response = await _host.SendAsync(method, path, "{", _owner);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("reauth_required", path);
        }
    }

    [Fact, Trait("Spec", "0017:SES-8"), Trait("Spec", "0017:AC-8")]
    public async Task Key_create_passes_when_the_last_check_is_exactly_10_minutes_old()
    {
        _host.Clock.Advance(TimeSpan.FromMinutes(10));

        await _host.CreateKeyAsync(_owner, _projectId);
    }

    [Fact, Trait("Spec", "0017:SES-7"), Trait("Spec", "0017:SES-8")]
    public async Task Origin_and_csrf_answer_before_the_recent_check()
    {
        _host.Clock.Advance(JustTooOld);

        using HttpResponseMessage origin = await _host.SendAsync(HttpMethod.Post, Protected[0].Path, "{}", _owner, "http://evil.example");
        using HttpResponseMessage csrf = await _host.SendAsync(HttpMethod.Post, Protected[0].Path, "{}", _owner with { CsrfToken = null! });

        (await ConsoleTestHost.ErrorCodeAsync(origin)).ShouldBe(ErrorCodes.Forbidden);
        (await ConsoleTestHost.ErrorCodeAsync(csrf)).ShouldBe(ErrorCodes.Forbidden);
    }

    [Fact, Trait("Spec", "0017:SES-8"), Trait("Spec", "0017:AC-8"), Trait("Spec", "0017:AC-22")]
    public async Task Reauth_answers_204_sets_the_last_check_and_lets_the_retry_through_when_the_password_is_right()
    {
        _host.Clock.Advance(JustTooOld);
        using HttpResponseMessage refused = await _host.PostAsync(Protected[0].Path, """{"type":"publishable","name":"x"}""", _owner);
        (await ConsoleTestHost.ErrorCodeAsync(refused)).ShouldBe("reauth_required");

        using HttpResponseMessage response = await ReauthAsync(ConsoleTestHost.OwnerPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await _host.CreateKeyAsync(_owner, _projectId);
        (await _host.WithDatabaseAsync(db => db.OperatorSessions.Select(x => x.LastReauthAt).MaxAsync(Token))).ShouldBe(_host.Clock.GetUtcNow());
        _host.Audit.Events.ShouldContain(x => x.Action == "operator.reauth" && x.Result == AuditResult.Success && x.TargetId == _owner.OperatorId);
    }

    [Fact, Trait("Spec", "0017:SES-8"), Trait("Spec", "0017:AC-8"), Trait("Spec", "0017:AC-22")]
    public async Task Reauth_fails_on_the_field_and_leaves_the_last_check_when_the_password_is_wrong()
    {
        DateTimeOffset before = await _host.WithDatabaseAsync(db => db.OperatorSessions.Select(x => x.LastReauthAt).MaxAsync(Token));
        _host.Clock.Advance(JustTooOld);

        using HttpResponseMessage response = await ReauthAsync("the wrong password");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        JsonElement error = body.GetProperty("errors")[0];
        error.GetProperty("field").GetString().ShouldBe("password");
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.InvalidPassword);
        (await _host.WithDatabaseAsync(db => db.OperatorSessions.Select(x => x.LastReauthAt).MaxAsync(Token))).ShouldBe(before);
        _host.Audit.Events.ShouldContain(x => x.Action == "operator.reauth" && x.Result == AuditResult.Failure);
    }

    [Fact, Trait("Spec", "0017:LIM-1"), Trait("Spec", "0017:AC-8")]
    public async Task Reauth_answers_429_and_sign_in_too_after_5_wrong_passwords()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage failed = await ReauthAsync("the wrong password");
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        int verifies = _host.Hasher.Verifies;

        using HttpResponseMessage response = await ReauthAsync(ConsoleTestHost.OwnerPassword);

        response.StatusCode.ShouldBe((HttpStatusCode)429);
        response.Headers.RetryAfter?.Delta.ShouldBe(TimeSpan.FromMinutes(15));
        _host.Hasher.Verifies.ShouldBe(verifies);
        using HttpResponseMessage signIn = await _host.PostAsync(
            "/v1/console/session", JsonSerializer.Serialize(new { email = ConsoleTestHost.OwnerEmail, password = ConsoleTestHost.OwnerPassword }));
        signIn.StatusCode.ShouldBe((HttpStatusCode)429);
    }

    [Fact, Trait("Spec", "0002:TST-4")]
    public async Task Reauth_answers_validation_failed_when_the_password_is_missing()
    {
        using HttpResponseMessage response = await _host.PostAsync(Reauth, "{}", _owner);

        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }
}
