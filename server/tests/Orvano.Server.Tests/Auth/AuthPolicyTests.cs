using System.Net;
using System.Text.Json;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Auth;

// Spec 0014 build task 1 over HTTP against the real binary: consoleAuthPolicies (defaults, partial updates checked on
// the merged result, roles, the event), and the minimum password length on every path that sets a new password, never
// at sign in. AC-1, AC-3, AC-4, AC-37, AC-40.
public class AuthPolicyTests(PostgresFixture postgres)
{
    public const string PoliciesUrl = "/v1/console/project/auth/policies";
    private const string Redirect = "https://app.example.com/auth?next=%2Fhome";

    [Fact]
    public async Task A_project_without_a_row_reads_the_defaults()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var read = await api.AsConsoleAsync(HttpMethod.Get, PoliciesUrl);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        var body = read.Body;
        Assert.True(body.GetProperty("signUpsEnabled").GetBoolean());
        Assert.False(body.GetProperty("requireVerifiedEmail").GetBoolean());
        Assert.Equal(8, body.GetProperty("passwordMinLength").GetInt32());
        Assert.True(body.GetProperty("passwordCommonCheck").GetBoolean());
        Assert.False(body.GetProperty("passwordBreachedCheck").GetBoolean());
        Assert.Equal(900, body.GetProperty("accessTokenSeconds").GetInt32());
        Assert.Equal(2592000, body.GetProperty("sessionIdleSeconds").GetInt32());
        Assert.Equal(31536000, body.GetProperty("sessionAbsoluteSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maxSessionsPerUser").ValueKind);
        Assert.Equal(10, body.GetProperty("signInFailedPerEmailIp").GetProperty("limit").GetInt32());
        Assert.Equal(15, body.GetProperty("signInFailedPerEmailIp").GetProperty("windowMinutes").GetInt32());
        Assert.Equal(100, body.GetProperty("signInFailedPerIp").GetInt32());
        Assert.Equal(60, body.GetProperty("signUpPerIp").GetInt32());
        Assert.Equal(30, body.GetProperty("anonymousPerIp").GetInt32());
        Assert.Equal(300, body.GetProperty("emailSendPerIp").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.False(body.GetProperty("smtpAvailable").GetBoolean());
        Assert.Equal(8, body.GetProperty("defaults").GetProperty("passwordMinLength").GetInt32());
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_policies"));
    }

    [Fact]
    public async Task An_update_is_partial_checked_on_the_merged_result_and_records_the_changed_fields()
    {
        await using var api = await AuthApi.StartAsync(postgres);

        using var saved = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new
        {
            passwordMinLength = 12,
            blockedEmailDomains = new[] { "Example.COM", "bücher.example" },
            trustedServerCidrs = new[] { "203.0.113.0/24" },
            maxSessionsPerUser = 3,
            signInFailedPerEmailIp = new { limit = 5 },
        });
        Assert.True(saved.Status == HttpStatusCode.OK, saved.Document?.RootElement.ToString());
        Assert.Equal(12, saved.Body.GetProperty("passwordMinLength").GetInt32());
        Assert.Equal(["example.com", "xn--bcher-kva.example"], saved.Body.GetProperty("blockedEmailDomains").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal("203.0.113.0/24", saved.Body.GetProperty("trustedServerCidrs")[0].GetString());
        Assert.Equal(5, saved.Body.GetProperty("signInFailedPerEmailIp").GetProperty("limit").GetInt32());
        Assert.Equal(15, saved.Body.GetProperty("signInFailedPerEmailIp").GetProperty("windowMinutes").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, saved.Body.GetProperty("updatedAt").ValueKind);

        // Left out keeps; null clears the session cap.
        using var cleared = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { maxSessionsPerUser = (int?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.Status);
        Assert.Equal(JsonValueKind.Null, cleared.Body.GetProperty("maxSessionsPerUser").ValueKind);
        Assert.Equal(12, cleared.Body.GetProperty("passwordMinLength").GetInt32());

        // Events carry the changed field names only, never a domain or CIDR (AC-39).
        Assert.Equal(
            "blockedEmailDomains,maxSessionsPerUser,passwordMinLength,signInFailedPerEmailIp,trustedServerCidrs|maxSessionsPerUser",
            await TestDatabase.ScalarAsync<string>(api.Database.Superuser,
                """
                SELECT string_agg(fields, '|' ORDER BY id) FROM (
                    SELECT e.id, string_agg(field, ',' ORDER BY field) AS fields
                    FROM orvano.events e, jsonb_array_elements_text(e.payload->'changed') AS field
                    WHERE e.type = 'auth.policies.updated'
                    GROUP BY e.id) changes
                """));
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser,
            "SELECT count(*) FROM orvano.events WHERE type = 'auth.policies.updated' AND (payload::text LIKE '%example%' OR payload::text LIKE '%203.0.113%')"));

        foreach (var bad in new object[]
        {
            new { passwordMinLength = 7 },
            new { sessionIdleSeconds = 7776000, sessionAbsoluteSeconds = 86400 },
            new { blockedEmailDomains = new[] { "ok.example", "*.example.com" } },
            new { allowedEmailDomains = new[] { "EXAMPLE.com" } },
            new { trustedServerCidrs = new[] { "203.0.113.1/24" } },
            new { trustedServerCidrs = new[] { "8.0.0.0/8" } },
            new { signInFailedPerEmailIp = new { windowMinutes = 0 } },
        })
        {
            using var refused = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, bad);
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("invalid_request", refused.Code);
        }

        using var named = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { blockedEmailDomains = new[] { "ok.example", "*.example.com" } });
        Assert.StartsWith("blockedEmailDomains has bad entries at positions 1:", named.Body.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var wrongType = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordMinLength = "twelve" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.Status);
    }

    [Fact]
    public async Task Viewers_read_owners_and_developers_write_and_the_console_project_has_no_page()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        await api.GrantAsync("viewer@x.com", "viewer");
        await api.GrantAsync("developer@x.com", "developer");

        using var viewerRead = await api.AsConsoleAsync(HttpMethod.Get, PoliciesUrl, account: "viewer@x.com");
        Assert.Equal(HttpStatusCode.OK, viewerRead.Status);
        using var viewerWrite = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordMinLength = 12 }, account: "viewer@x.com");
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.Status);
        Assert.Equal("forbidden", viewerWrite.Code);
        using var developerWrite = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordMinLength = 12 }, account: "developer@x.com");
        Assert.Equal(HttpStatusCode.OK, developerWrite.Status);

        using var console = await api.AsConsoleAsync(HttpMethod.Get, PoliciesUrl, project: "console");
        Assert.Equal(HttpStatusCode.NotFound, console.Status);
        using var stranger = await api.AsConsoleAsync(HttpMethod.Get, PoliciesUrl, account: "stranger@x.com");
        Assert.Equal(HttpStatusCode.NotFound, stranger.Status);
    }

    [Fact]
    public async Task Requiring_verified_emails_needs_smtp()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using var refused = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { requireVerifiedEmail = true });
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("email_not_configured", refused.Code);

        await using var withSmtp = await AuthApi.StartAsync(postgres, smtp: true);
        using var saved = await withSmtp.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { requireVerifiedEmail = true });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.True(saved.Body.GetProperty("smtpAvailable").GetBoolean());
    }

    [Fact]
    public async Task The_minimum_length_applies_to_every_new_password_and_never_at_sign_in()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        const string Eleven = "eleven char";
        const string Twelve = "twelve chars";

        // Ada's 11 character password predates the rule.
        using (var ada = await api.SignUpAsync("ada@x.com", Eleven)) Assert.Equal(HttpStatusCode.Created, ada.Status);
        using (var raised = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordMinLength = 12 }))
            Assert.Equal(HttpStatusCode.OK, raised.Status);

        using var signUp = await api.SignUpAsync("bob@x.com", Eleven);
        AssertTooShort(signUp, 12);
        using var server = await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "cy@x.com", password = Eleven });
        AssertTooShort(server, 12);
        using var console = await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/users", new { email = "di@x.com", password = Eleven });
        AssertTooShort(console, 12);
        // Under the 8 character floor, the detail still names the project's minimum.
        using var floor = await api.SignUpAsync("ed@x.com", "short");
        AssertTooShort(floor, 12);

        // Ada still signs in with her old password, but a new one must meet the rule.
        using var signIn = await api.SignInAsync("ada@x.com", Eleven);
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        var access = AuthApi.AccessToken(signIn);
        using var change = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = Eleven, newPassword = "eleven chr2" }, bearer: access);
        AssertTooShort(change, 12);
        using var changed = await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = Eleven, newPassword = Twelve }, bearer: access);
        Assert.Equal(HttpStatusCode.NoContent, changed.Status);

        using (var request = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "ada@x.com", redirectUrl = Redirect }))
            Assert.Equal(HttpStatusCode.Accepted, request.Status);
        var token = (await api.LatestEmailAsync("ada@x.com"))!.Token!;
        using var weakReset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = Eleven });
        AssertTooShort(weakReset, 12);
        using var reset = await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = "twelve chars again" });
        Assert.Equal(HttpStatusCode.Created, reset.Status);

        using var ok = await api.SignUpAsync("fay@x.com", Twelve);
        Assert.Equal(HttpStatusCode.Created, ok.Status);
    }

    [Fact]
    public async Task The_console_project_keeps_the_default_minimum_whatever_an_app_project_sets()
    {
        await using var api = await AuthApi.StartAsync(postgres);
        using (var raised = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordMinLength = 20 }))
            Assert.Equal(HttpStatusCode.OK, raised.Status);

        using var other = await api.SignUpAsync("ada@x.com", "eight ch", project: AuthApi.OtherProject);
        Assert.Equal(HttpStatusCode.Created, other.Status);
        Assert.Equal(0L, await TestDatabase.ScalarAsync<long>(api.Database.Superuser, "SELECT count(*) FROM orvano.auth_policies WHERE project_id = 'console'"));
    }

    [Fact]
    public async Task Common_passwords_are_refused_on_every_new_password_until_the_check_is_off()
    {
        await using var api = await AuthApi.StartAsync(postgres, email: true, smtp: true);
        const string Common = "password123";

        // Ada's password predates the rule; the list is checked lowercased after NFKC.
        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordCommonCheck = false })) { }
        using (var ada = await api.SignUpAsync("ada@x.com", Common)) Assert.Equal(HttpStatusCode.Created, ada.Status);
        using (var on = await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordCommonCheck = true }))
            Assert.Equal(HttpStatusCode.OK, on.Status);

        AssertCode(await api.SignUpAsync("bob@x.com", Common), "password_too_common");
        AssertCode(await api.SignUpAsync("bob@x.com", "PassWord123"), "password_too_common");
        AssertCode(await api.AsServerAsync(HttpMethod.Post, "/v1/users", new { email = "cy@x.com", password = Common }), "password_too_common");
        AssertCode(await api.AsConsoleAsync(HttpMethod.Post, "/v1/console/project/users", new { email = "di@x.com", password = Common }), "password_too_common");

        using var signIn = await api.SignInAsync("ada@x.com", Common);
        Assert.Equal(HttpStatusCode.Created, signIn.Status);
        AssertCode(await api.SendAsync(HttpMethod.Put, "/v1/account/password", new { currentPassword = Common, newPassword = "iloveyou123" },
            bearer: AuthApi.AccessToken(signIn)), "password_too_common");
        using (await api.SendAsync(HttpMethod.Post, "/v1/account/recovery", new { email = "ada@x.com", redirectUrl = Redirect })) { }
        var token = (await api.LatestEmailAsync("ada@x.com"))!.Token!;
        AssertCode(await api.SendAsync(HttpMethod.Post, "/v1/account/recovery/confirm", new { token, password = Common }), "password_too_common");

        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordCommonCheck = false })) { }
        using var off = await api.SignUpAsync("bob@x.com", Common);
        Assert.Equal(HttpStatusCode.Created, off.Status);
    }

    [Fact]
    public async Task A_console_account_is_held_to_the_common_list_too()
    {
        const string SetupToken = "ost_authPolicyTestsSetupToken000000000000000000";
        await using var api = await AuthApi.StartAsync(postgres, new Dictionary<string, string> { ["ORVANO_SETUP_TOKEN"] = SetupToken }, fixtures: false);
        using var common = await api.SendAsync(HttpMethod.Post, "/v1/console/account", new { email = "ada@x.com", password = "password123", setupToken = SetupToken },
            project: null, headers: new Dictionary<string, string> { ["Sec-Fetch-Site"] = "same-origin" });
        AssertCode(common, "password_too_common");
    }

    [Fact]
    public async Task The_breached_check_refuses_a_listed_password_sends_only_a_padded_prefix_and_fails_open()
    {
        await using var api = await AuthApi.StartAsync(postgres, pwned: true);

        // Off by default: the fake is never asked.
        using (var before = await api.SignUpAsync("ada@x.com", Orvano.Server.Modules.FakePwnedPasswords.Breached)) Assert.Equal(HttpStatusCode.Created, before.Status);
        Assert.Empty(await RangeRequestsAsync(api));

        using (await api.AsConsoleAsync(HttpMethod.Patch, PoliciesUrl, new { passwordBreachedCheck = true })) { }
        AssertCode(await api.SignUpAsync("bob@x.com", Orvano.Server.Modules.FakePwnedPasswords.Breached), "password_breached");
        using (var clean = await api.SignUpAsync("cy@x.com", "a fine horse battery staple")) Assert.Equal(HttpStatusCode.Created, clean.Status);

        // A slow or broken range API passes the password: the check fails open.
        var started = System.Diagnostics.Stopwatch.StartNew();
        using (var slow = await api.SignUpAsync("di@x.com", Orvano.Server.Modules.FakePwnedPasswords.Slow)) Assert.Equal(HttpStatusCode.Created, slow.Status);
        Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4));
        using (var broken = await api.SignUpAsync("ed@x.com", Orvano.Server.Modules.FakePwnedPasswords.Broken)) Assert.Equal(HttpStatusCode.Created, broken.Status);

        // Only 5 hex characters left the server, each time with padding asked for.
        var requests = await RangeRequestsAsync(api);
        Assert.Equal(4, requests.Count);
        Assert.All(requests, r =>
        {
            Assert.Matches("^[0-9A-F]{5}$", r.Prefix);
            Assert.Equal("true", r.AddPadding);
        });
    }

    private static async Task<List<(string Prefix, string? AddPadding)>> RangeRequestsAsync(AuthApi api)
    {
        using var reply = await api.SendAsync(HttpMethod.Get, "/v1/test/pwned/requests", project: null);
        return [.. reply.Body.EnumerateArray().Select(r => (r.GetProperty("prefix").GetString()!, r.GetProperty("addPadding").GetString()))];
    }

    private static void AssertCode(Reply reply, string code)
    {
        using (reply)
        {
            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
            Assert.Equal(code, reply.Code);
        }
    }

    private static void AssertTooShort(Reply reply, int minimum)
    {
        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal("invalid_password", reply.Code);
        Assert.Equal($"The password must be {minimum} to 256 characters.", reply.Body.GetProperty("detail").GetString());
    }
}
