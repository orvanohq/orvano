using System.Net;
using Orvano.Server.Tests.Infrastructure;
using static Orvano.Server.Tests.Auth.MfaTests;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 3 over HTTP against the real binary: failed attempts count per account plus limit IP, the
// limit IP from a trusted app server's X-Orvano-Client-IP, the per account ceilings of 6 digit codes, the editable
// limits, API key failures, console invite emails, and the spec 0013 review fixes. AC-16 to AC-24. The test talks to
// the server from 127.0.0.1, which each test lists as a trusted app server, so every request can name its visitor.
public class AbuseLimitTests(PostgresFixture postgres)
{
    private const string PoliciesUrl = AuthPolicyTests.PoliciesUrl;
    private const string Right = "correct horse battery";
    private const string Wrong = "wrong horse battery";

    [Fact]
    public async Task Failed_sign_ins_lock_one_email_from_one_address_only_and_a_right_password_never_counts()
    {
        await using var api = await StartTrustedAsync();
        using (await api.SignUpAsync("ada@x.com", Right)) { }

        // Five wrong, five right, five wrong: only the ten wrong ones count, so the eleventh attempt from A is refused.
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(api, "ada@x.com", Wrong, "198.51.100.1")).Status);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Created, (await SignInAsync(api, "ada@x.com", Right, "198.51.100.1")).Status);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(api, "ada@x.com", Wrong, "198.51.100.1")).Status);

        using var locked = await SignInAsync(api, "ada@x.com", Right, "198.51.100.1");
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (locked.Status, locked.Code));
        Assert.NotNull(locked.Headers.RetryAfter);
        // An unknown email answers the same way, so the limit says nothing about which accounts exist.
        for (var i = 0; i < 10; i++) using (await SignInAsync(api, "nobody@x.com", Wrong, "198.51.100.1")) { }
        using var unknown = await SignInAsync(api, "nobody@x.com", Right, "198.51.100.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, unknown.Status);

        // Ada on her own network is not locked out.
        using var owner = await SignInAsync(api, "ada@x.com", Right, "203.0.113.9");
        Assert.Equal(HttpStatusCode.Created, owner.Status);
    }

    [Fact]
    public async Task Failures_across_emails_throttle_their_address_only_and_parallel_guesses_cannot_pass_together()
    {
        await using var api = await StartTrustedAsync();
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { signInFailedPerIp = 10 })) { }
        using (await api.SignUpAsync("ada@x.com", Right)) { }

        for (var i = 0; i < 10; i++) using (await SignInAsync(api, $"guess{i}@x.com", Wrong, "198.51.100.3")) { }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(api, "ada@x.com", Right, "198.51.100.3")).Status);
        Assert.Equal(HttpStatusCode.Created, (await SignInAsync(api, "ada@x.com", Right, "198.51.100.4")).Status);

        // 50 wrong passwords at once for one email and address: reservations let at most 10 reach the hash.
        var replies = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => SignInAsync(api, "ada@x.com", Wrong, "198.51.100.5")));
        Assert.InRange(replies.Count(r => r.Status == HttpStatusCode.Unauthorized), 1, 10);
        Assert.All(replies, r => Assert.Contains(r.Status, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests }));
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task An_ipv6_64_is_one_address_and_a_header_from_an_unlisted_server_is_ignored()
    {
        await using var api = await StartTrustedAsync();
        using (await api.SignUpAsync("ada@x.com", Right)) { }

        for (var i = 0; i < 10; i++) using (await SignInAsync(api, "ada@x.com", Wrong, $"2001:db8:1:2::{i + 1:x}")) { }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(api, "ada@x.com", Right, "2001:db8:1:2:ffff::1")).Status);
        Assert.Equal(HttpStatusCode.Created, (await SignInAsync(api, "ada@x.com", Right, "2001:db8:1:3::1")).Status);
        // An IPv4 mapped address is its IPv4 address.
        for (var i = 0; i < 10; i++) using (await SignInAsync(api, "ada@x.com", Wrong, "::ffff:198.51.100.20")) { }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(api, "ada@x.com", Right, "198.51.100.20")).Status);

        // Without the listing, every forged address lands in the server's own bucket.
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { trustedServerCidrs = Array.Empty<string>() })) { }
        for (var i = 0; i < 10; i++) using (await SignInAsync(api, "ada@x.com", Wrong, $"192.0.2.{i + 1}")) { }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(api, "ada@x.com", Right, "192.0.2.200")).Status);
        // A list or a port is not one address, so it is ignored even from a listed server.
        Assert.False(global::Orvano.Core.Http.LimitIp.TryParseOne("198.51.100.1, 10.0.0.1", out _));
        Assert.False(global::Orvano.Core.Http.LimitIp.TryParseOne("198.51.100.1:443", out _));
        Assert.False(global::Orvano.Core.Http.LimitIp.TryParseOne("[2001:db8::1]:443", out _));
        Assert.False(global::Orvano.Core.Http.LimitIp.TryParseOne("fe80::1%eth0", out _));
    }

    [Fact]
    public async Task Wrong_factors_lock_step_two_per_address_and_wrong_totp_codes_hit_a_ceiling_that_leaves_recovery_codes()
    {
        await using var api = await StartTrustedAsync();
        var ada = await EnrollAsync(api, "ada@x.com");

        // Someone with Ada's password spends ten wrong codes from A; Ada at B still passes step two.
        for (var i = 0; i < 10; i++)
        {
            using var stepOne = await SignInAsync(api, "ada@x.com", Right, "198.51.100.1");
            using (await StepTwoFromAsync(api, Ticket(stepOne), "198.51.100.1", totpCode: WrongCode(ada.Secret))) { }
        }

        using (var stepOne = await SignInAsync(api, "ada@x.com", Right, "198.51.100.1"))
            Assert.Equal(HttpStatusCode.TooManyRequests, (await StepTwoFromAsync(api, Ticket(stepOne), "198.51.100.1", totpCode: CodeAt(ada.Secret, 0))).Status);
        using (var stepOne = await SignInAsync(api, "ada@x.com", Right, "203.0.113.9"))
            Assert.Equal(HttpStatusCode.Created, (await StepTwoFromAsync(api, Ticket(stepOne), "203.0.113.9", totpCode: CodeAt(ada.Secret, 1))).Status);

        // 60 wrong TOTP codes from many addresses within the hour: TOTP is refused everywhere, a recovery code still works.
        for (var i = 0; i < 50; i++)
        {
            var from = $"198.51.{100 + (i / 9)}.{(i % 9) + 10}";
            using var stepOne = await SignInAsync(api, "ada@x.com", Right, from);
            using (await StepTwoFromAsync(api, Ticket(stepOne), from, totpCode: WrongCode(ada.Secret))) { }
        }

        using (var stepOne = await SignInAsync(api, "ada@x.com", Right, "203.0.113.50"))
        {
            using var totp = await StepTwoFromAsync(api, Ticket(stepOne), "203.0.113.50", totpCode: CodeAt(ada.Secret, 0));
            Assert.Equal(HttpStatusCode.TooManyRequests, totp.Status);
            using var recovery = await StepTwoFromAsync(api, Ticket(stepOne), "203.0.113.50", recoveryCode: ada.RecoveryCodes[0]);
            Assert.Equal(HttpStatusCode.Created, recovery.Status);
        }
    }

    [Fact]
    public async Task Wrong_email_codes_count_per_address_and_the_tenth_deletes_the_code()
    {
        await using var api = await StartTrustedAsync(smtp: true);
        using (await api.SignUpAsync("ada@x.com", Right)) { }
        var code = await PasswordlessTests.EmailCodeAsync(api, "ada@x.com");
        var wrong = ((int.Parse(code, System.Globalization.CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await CodeFromAsync(api, "ada@x.com", wrong, "198.51.100.1")).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CodeFromAsync(api, "ada@x.com", code, "198.51.100.1")).Status);
        // The code survived five wrong guesses from elsewhere, so Ada's right one works from her network.
        Assert.Equal(HttpStatusCode.Created, (await CodeFromAsync(api, "ada@x.com", code, "203.0.113.9")).Status);

        // A second code within the minute must come from another address (one send a minute per address, AC-20).
        using (var send = await api.SendAsync(HttpMethod.Post, "/v1/account/email-code", new { email = "ada@x.com" }, headers: From("203.0.113.10")))
            Assert.Equal(HttpStatusCode.Accepted, send.Status);
        var next = (await api.LatestEmailAsync("ada@x.com"))!.Code!;
        var miss = ((int.Parse(next, System.Globalization.CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < 4; i++) using (await CodeFromAsync(api, "ada@x.com", miss, "198.51.100.2")) { }
        for (var i = 0; i < 4; i++) using (await CodeFromAsync(api, "ada@x.com", miss, "198.51.100.3")) { }
        Assert.Equal(1L, await CodeRowsAsync(api));
        using (await CodeFromAsync(api, "ada@x.com", miss, "198.51.100.4")) { }
        Assert.Equal(1L, await CodeRowsAsync(api));
        using (await CodeFromAsync(api, "ada@x.com", miss, "198.51.100.5")) { }
        Assert.Equal(0L, await CodeRowsAsync(api)); // the tenth wrong guess
    }

    [Fact]
    public async Task Sends_count_per_recipient_and_address_up_to_a_total_per_inbox()
    {
        await using var api = await StartTrustedAsync(email: true, smtp: true);
        using (await api.SignUpAsync("ada@x.com", Right)) { }

        // A stranger spends A's allowance for Ada's inbox; the owner still gets a reset from B at once.
        Assert.Equal(HttpStatusCode.Accepted, (await RecoveryFromAsync(api, "ada@x.com", "198.51.100.1")).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RecoveryFromAsync(api, "ada@x.com", "198.51.100.1")).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await RecoveryFromAsync(api, "ada@x.com", "203.0.113.9")).Status);
        Assert.Equal(2L, await QueuedAsync(api, "recovery"));

        // 20 an hour reach one inbox whatever the address, then no more.
        for (var i = 0; i < 18; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await RecoveryFromAsync(api, "ada@x.com", $"192.0.2.{i + 1}")).Status);
        Assert.Equal(20L, await QueuedAsync(api, "recovery"));
        using var over = await RecoveryFromAsync(api, "ada@x.com", "192.0.2.100");
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (over.Status, over.Code));
        Assert.Equal(20L, await QueuedAsync(api, "recovery"));
    }

    [Fact]
    public async Task An_editable_limit_uses_the_projects_value_and_a_change_starts_fresh_counters()
    {
        await using var api = await StartTrustedAsync();
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { signUpPerIp = 2 })) { }

        Assert.Equal(HttpStatusCode.Created, (await SignUpFromAsync(api, "a@x.com", "198.51.100.1")).Status);
        Assert.Equal(HttpStatusCode.Created, (await SignUpFromAsync(api, "b@x.com", "198.51.100.1")).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignUpFromAsync(api, "c@x.com", "198.51.100.1")).Status);
        // Another project keeps its own limit and counters.
        using (var other = await api.SendAsync(HttpMethod.Post, "/v1/account", new { email = "c@x.com", password = Right }, project: AuthApi.OtherProject))
            Assert.Equal(HttpStatusCode.Created, other.Status);

        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { signUpPerIp = 3 })) { }
        Assert.Equal(HttpStatusCode.Created, (await SignUpFromAsync(api, "c@x.com", "198.51.100.1")).Status);
    }

    [Fact]
    public async Task Failing_api_keys_from_one_address_get_429_while_a_valid_key_always_passes()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        for (var i = 0; i < 60; i++)
        {
            using var bad = await api.AsServerAsync(HttpMethod.Get, "/v1/users", key: "orv_sk_" + new string('x', 43));
            Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);
        }

        using var limited = await api.AsServerAsync(HttpMethod.Get, "/v1/users", key: "orv_sk_" + new string('y', 43));
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (limited.Status, limited.Code));
        using var valid = await api.AsServerAsync(HttpMethod.Get, "/v1/users");
        Assert.Equal(HttpStatusCode.OK, valid.Status);
    }

    [Fact]
    public async Task The_sixth_invitation_email_to_one_address_in_an_hour_is_refused()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        var orgId = await TestDatabase.ScalarAsync<Guid>(api.Database.Superuser, "SELECT org_id FROM orvano.platform_projects WHERE id = @p", ("p", AuthApi.Project));
        for (var i = 0; i < 5; i++)
        {
            using var invited = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = "Grace@x.com", role = "viewer" }, project: null);
            Assert.Equal(HttpStatusCode.Created, invited.Status);
        }

        using var refused = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = "grace@x.com", role = "viewer" }, project: null);
        Assert.Equal((HttpStatusCode.TooManyRequests, "rate_limited"), (refused.Status, refused.Code));
        using var other = await api.AsConsoleAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = "linus@x.com", role = "viewer" }, project: null);
        Assert.Equal(HttpStatusCode.Created, other.Status);
    }

    [Fact]
    public async Task Refused_enrollment_checks_never_spend_the_enroll_limit()
    {
        await using var api = await StartTrustedAsync();
        var ada = await EnrollAsync(api, "ada@x.com");
        using var stepOne = await SignInAsync(api, "ada@x.com", Right, "198.51.100.1");
        using var signedIn = await StepTwoFromAsync(api, Ticket(stepOne), "198.51.100.1", totpCode: CodeAt(ada.Secret, 1));
        var bearer = AuthApi.AccessToken(signedIn);

        // A second session whose strong check is an hour old: every MFA change from it is refused.
        using var weak = await SignInAsync(api, "ada@x.com", Right, "198.51.100.2");
        using var weakSession = await StepTwoFromAsync(api, Ticket(weak), "198.51.100.2", recoveryCode: ada.RecoveryCodes[0]);
        await TestDatabase.ExecuteAsync(api.Database.Superuser,
            "UPDATE orvano.auth_sessions SET strong_auth_at = now() - interval '1 hour' WHERE id = @id", ("id", Guid.Parse(Sid(weakSession))));
        var weakBearer = AuthApi.AccessToken(weakSession);
        for (var i = 0; i < 12; i++)
        {
            using var refused = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/recovery-codes", bearer: weakBearer);
            Assert.Equal("mfa_verification_required", refused.Code);
        }

        // The ten enrollments are all still there for the strong session.
        using var codes = await api.SendAsync(HttpMethod.Post, "/v1/account/mfa/recovery-codes", bearer: bearer);
        Assert.Equal(HttpStatusCode.Created, codes.Status);
    }

    private async Task<AuthApi> StartTrustedAsync(bool email = false, bool smtp = false)
    {
        var api = await AuthApi.StartAsync(postgres, email: email, smtp: smtp);
        using var trusted = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { trustedServerCidrs = new[] { "127.0.0.1/32", "::1/128" } });
        Assert.Equal(HttpStatusCode.OK, trusted.Status);
        return api;
    }

    private static Dictionary<string, string> From(string ip) => new() { ["X-Orvano-Client-IP"] = ip };

    private static Task<Reply> SignInAsync(AuthApi api, string email, string password, string ip) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/sessions/password", new { email, password }, headers: From(ip));

    private static Task<Reply> SignUpFromAsync(AuthApi api, string email, string ip) =>
        api.SendAsync(HttpMethod.Post, "/v1/account", new { email, password = Right }, headers: From(ip));

    private static Task<Reply> StepTwoFromAsync(AuthApi api, string ticket, string ip, string? totpCode = null, string? recoveryCode = null) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/sessions/mfa", new Dictionary<string, string?>
        {
            ["ticket"] = ticket,
            ["totpCode"] = totpCode,
            ["recoveryCode"] = recoveryCode,
        }.Where(p => p.Value is not null).ToDictionary(), headers: From(ip));

    private static Task<Reply> CodeFromAsync(AuthApi api, string email, string code, string ip) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/sessions/email-code", new { email, code }, headers: From(ip));

    private static Task<Reply> RecoveryFromAsync(AuthApi api, string email, string ip) =>
        api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email, redirectUrl = "https://app.example.com/auth" }, headers: From(ip));

    private static Task<long> CodeRowsAsync(AuthApi api) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_email_tokens WHERE kind = 'email_code'");

    private static Task<long> QueuedAsync(AuthApi api, string template) =>
        TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.messaging_emails WHERE template = @t", ("t", template));
}
