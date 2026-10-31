using System.Net;

using Microsoft.EntityFrameworkCore;

using Orvano.Modules.Platform.Data;
using Orvano.Modules.Platform.Setup;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Audit;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Platform;

// Spec 0017 OWN-1 to OWN-4, AC-1 to AC-3: the owner form on a real database.
public sealed class ConsoleOwnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/v1/console/setup/owner";

    private ConsoleTestHost _host = null!;

    public async ValueTask InitializeAsync() =>
        _host = new ConsoleTestHost(postgres, await postgres.CreateMigratedDatabaseAsync("owner", TestContext.Current.CancellationToken));

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact, Trait("Spec", "0017:OWN-3"), Trait("Spec", "0017:OWN-4"), Trait("Spec", "0017:AC-1")]
    public async Task Owner_form_makes_every_row_and_closes_setup_in_one_answer()
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody(await _host.IssueSetupTokenAsync()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        ConsoleTestHost.SetCookie(response).ShouldNotBeNull().ShouldStartWith("__Host-orvano_session=");
        System.Text.Json.JsonElement body = (await ConsoleTestHost.ReadJsonAsync(response)).RootElement;
        body.GetProperty("operator").GetProperty("name").GetString().ShouldBe("Ada Owner");
        body.GetProperty("memberships")[0].GetProperty("organizationName").GetString().ShouldBe("Acme");
        body.GetProperty("memberships")[0].GetProperty("role").GetString().ShouldBe("owner");
        body.GetProperty("memberships")[0].GetProperty("allowedActions").EnumerateArray().Select(x => x.GetString()).ShouldContain("projects.create");

        await _host.WithDatabaseAsync(async db =>
        {
            Instance instance = await db.Instances.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            instance.SetupCompletedAt.ShouldBe(_host.Clock.GetUtcNow());
            instance.SetupTokenHash.ShouldBeNull();
            instance.SetupTokenExpiresAt.ShouldBeNull();
            Operator owner = await db.Operators.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
            owner.Email.ShouldBe(ConsoleTestHost.OwnerEmail);
            owner.EmailNormalized.ShouldBe(ConsoleTestHost.OwnerEmail);
            owner.PasswordHash.ShouldStartWith("$argon2id$");
            owner.PasswordHash.ShouldNotContain(ConsoleTestHost.OwnerPhrase);
            owner.PasswordChangedAt.ShouldBe(_host.Clock.GetUtcNow());
            (await db.Organizations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Name.ShouldBe("Acme");
            (await db.Memberships.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).OperatorId.ShouldBe(owner.Id);
            (await db.OperatorSessions.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).OperatorId.ShouldBe(owner.Id);
            return true;
        });
    }

    [Fact, Trait("Spec", "0017:AC-1"), Trait("Spec", "0016:FRS-1")]
    public async Task Config_and_setup_token_show_setup_closed_when_the_owner_exists()
    {
        await _host.CreateOwnerAsync();

        using HttpResponseMessage config = await _host.GetAsync("/v1/console/config");
        (await ConsoleTestHost.ReadJsonAsync(config)).RootElement.GetProperty("setupRequired").GetBoolean().ShouldBeFalse();
        (await PlatformSetup.IssueLinkAsync(_host.Services, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Theory, Trait("Spec", "0017:OWN-2"), Trait("Spec", "0017:AC-2")]
    [InlineData("orv_setup_wrong")]
    [InlineData("not even the form of a token")]
    public async Task Owner_form_answers_401_and_costs_no_hash_when_the_token_is_wrong(string token)
    {
        await _host.IssueSetupTokenAsync();
        int hashes = _host.Hasher.Hashes; // the startup self test hashes once
        using HttpResponseMessage response = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody(token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("setup_token_invalid");
        _host.Hasher.Hashes.ShouldBe(hashes);
        (await _host.WithDatabaseAsync(db => db.Operators.CountAsync(TestContext.Current.CancellationToken))).ShouldBe(0);
    }

    [Fact, Trait("Spec", "0017:OWN-2"), Trait("Spec", "0017:AC-2")]
    public async Task Owner_form_answers_401_when_the_token_expired_or_was_replaced()
    {
        string first = await _host.IssueSetupTokenAsync();
        string second = await _host.IssueSetupTokenAsync();
        using HttpResponseMessage replaced = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody(first));
        (await ConsoleTestHost.ErrorCodeAsync(replaced)).ShouldBe("setup_token_invalid");

        _host.Clock.Advance(TimeSpan.FromHours(24));

        using HttpResponseMessage expired = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody(second));
        (await ConsoleTestHost.ErrorCodeAsync(expired)).ShouldBe("setup_token_invalid");
    }

    [Fact, Trait("Spec", "0017:AC-2")]
    public async Task Owner_form_answers_409_when_setup_is_complete()
    {
        await _host.CreateOwnerAsync();

        using HttpResponseMessage response = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody("orv_setup_any"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe("setup_complete");
    }

    [Fact, Trait("Spec", "0017:OWN-3"), Trait("Spec", "0017:AC-3")]
    public async Task Two_owner_forms_at_once_give_one_201_and_one_409()
    {
        string token = await _host.IssueSetupTokenAsync();

        HttpResponseMessage[] answers = await Task.WhenAll(
            _host.PostAsync(Path, ConsoleTestHost.OwnerBody(token, email: "a@example.com")),
            _host.PostAsync(Path, ConsoleTestHost.OwnerBody(token, email: "b@example.com")));

        answers.Select(x => x.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
        (await ConsoleTestHost.ErrorCodeAsync(answers.Single(x => x.StatusCode == HttpStatusCode.Conflict))).ShouldBe("setup_complete");
        (await _host.WithDatabaseAsync(db => db.Operators.CountAsync(TestContext.Current.CancellationToken))).ShouldBe(1);
        (await _host.WithDatabaseAsync(db => db.Organizations.CountAsync(TestContext.Current.CancellationToken))).ShouldBe(1);
        foreach (HttpResponseMessage answer in answers)
        {
            answer.Dispose();
        }
    }

    [Theory, Trait("Spec", "0017:PSW-1"), Trait("Spec", "0017:AC-4")]
    [InlineData("short", FieldCodes.TooShort)]
    [InlineData("sunshine", FieldCodes.PasswordTooCommon)]
    public async Task Owner_form_answers_the_policy_code_and_writes_nothing_when_the_password_is_refused(string phrase, string code)
    {
        string token = await _host.IssueSetupTokenAsync();
        int hashes = _host.Hasher.Hashes;
        using HttpResponseMessage response = await _host.PostAsync(Path, ConsoleTestHost.OwnerBody(token, phrase));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
        (await ConsoleTestHost.FieldCodeAsync(response)).ShouldBe(code);
        _host.Hasher.Hashes.ShouldBe(hashes);
    }

    [Fact, Trait("Spec", "0002:TST-4")]
    public async Task Owner_form_answers_validation_failed_when_a_field_is_missing()
    {
        using HttpResponseMessage response = await _host.PostAsync(Path, """{"token":"x"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ConsoleTestHost.ErrorCodeAsync(response)).ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact, Trait("Spec", "0017:OWN-1")]
    public async Task Owner_form_answers_403_when_the_origin_is_foreign()
    {
        using HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post, Path, ConsoleTestHost.OwnerBody(await _host.IssueSetupTokenAsync()), origin: "http://evil.example");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Spec", "0017:OWN-4"), Trait("Spec", "0017:AUS-3"), Trait("Spec", "0017:AC-22")]
    public async Task Owner_form_emits_its_audit_events_after_commit_with_no_secret()
    {
        await _host.CreateOwnerAsync();

        _host.Audit.Events.Select(x => x.Action).ShouldBe(["operator.created", "organization.created", "operator.sign_in"]);
        _host.Audit.Events.ShouldAllBe(x => x.ActorKind == AuditActorKind.Operator && x.Result == AuditResult.Success);
    }

    [Fact, Trait("Spec", "0017:AUS-3"), Trait("Spec", "0017:AC-22")]
    public async Task Owner_form_still_answers_201_when_the_audit_sink_fails()
    {
        _host.Audit.Fails = true;

        await _host.CreateOwnerAsync();

        _host.Logs.Lines.ShouldContain(line => line.Contains("was not recorded", StringComparison.Ordinal));
    }
}
