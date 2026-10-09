using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Orvano.Contract;
using Orvano.Server.Tests.Infrastructure;

namespace Orvano.Server.Tests.Platform;

// Spec 0008 over HTTP against the real binary in the Test environment, where every response is also checked against
// the contract: invitations from create to accept or invited sign up, the members list, role changes, removal and
// leaving, and the leftovers of row 15 (ConsoleAccount, signupOpen, ApiKey.createdBy).
public class MembersAndInvitationsTests(PostgresFixture postgres)
{
    private const string Owner = "members-owner@x.com";
    private const string Dev = "members-dev@x.com";
    private const string Outsider = "members-outsider@x.com";
    private const string NewPassword = "new horse battery staple";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_person_signs_up_from_an_invite_link_and_joins_with_the_invited_role()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();

        using var created = await t.SendAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = " New@X.com ", role = "developer" });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var url = created.Body.GetProperty("url").GetString()!;
        Assert.StartsWith($"{OrvanoProcess.PublicUrl}/invite#", url);
        var token = url[(url.IndexOf('#') + 1)..];
        Assert.Equal(43, token.Length);
        var invitation = created.Body.GetProperty("invitation");
        Assert.Equal("New@X.com", invitation.GetProperty("email").GetString());
        Assert.Equal("pending", invitation.GetProperty("status").GetString());
        Assert.Equal(Owner, invitation.GetProperty("invitedBy").GetProperty("email").GetString());

        using var preview = await t.PreviewAsync(token);
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal(orgId, preview.Body.GetProperty("orgId").GetString());
        Assert.Equal("Acme", preview.Body.GetProperty("orgName").GetString());
        Assert.Equal("developer", preview.Body.GetProperty("role").GetString());
        Assert.Equal(Owner, preview.Body.GetProperty("invitedByName").GetString()); // no name, so the email

        using var signUp = await t.SignUpAsync("new@x.com", token);
        Assert.Equal(HttpStatusCode.Created, signUp.Status);
        Assert.False(signUp.Body.GetProperty("isInstallAdmin").GetBoolean());
        var newUserId = signUp.Body.GetProperty("id").GetString()!;

        using var members = await t.SendAsync(HttpMethod.Get, $"/v1/console/orgs/{orgId}/members");
        var joined = members.Body.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("userId").GetString() == newUserId);
        Assert.Equal("developer", joined.GetProperty("role").GetString());
        Assert.Equal("active", joined.GetProperty("status").GetString());

        using var used = await t.PreviewAsync(token);
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.InvitationNotFound), (used.Status, used.Code));
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
        // The new account also got its own personal org, as every sign up does.
        Assert.Equal(2L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_memberships WHERE user_id = @u", ("u", Guid.Parse(newUserId))));
        await t.AssertNoLeakAsync(token, "new@x.com");
    }

    [Fact]
    public async Task A_signed_in_account_accepts_once_and_a_second_accept_gets_404()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, Dev.ToUpperInvariant(), "viewer");

        using var accepted = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev);
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        Assert.False(accepted.Body.GetProperty("alreadyMember").GetBoolean());
        Assert.Equal(orgId, accepted.Body.GetProperty("org").GetProperty("id").GetString());
        Assert.Equal("viewer", accepted.Body.GetProperty("org").GetProperty("role").GetString());

        using var again = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev);
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.InvitationNotFound), (again.Status, again.Code));

        using var org = await t.SendAsync(HttpMethod.Get, $"/v1/console/orgs/{orgId}", session: Dev);
        Assert.Equal("viewer", org.Body.GetProperty("role").GetString());
        await t.AssertNoLeakAsync(token, Dev);
    }

    [Fact]
    public async Task An_existing_member_who_accepts_keeps_their_role()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, Dev, "owner");
        await t.ExecuteAsync("INSERT INTO orvano.platform_memberships (org_id, user_id, role) VALUES (@o, @u, 'viewer')",
            ("o", Guid.Parse(orgId)), ("u", await t.UserIdAsync(Dev)));

        using var accepted = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev);
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        Assert.True(accepted.Body.GetProperty("alreadyMember").GetBoolean());
        Assert.Equal("viewer", accepted.Body.GetProperty("org").GetProperty("role").GetString());
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
    }

    [Fact]
    public async Task Inviting_an_email_again_replaces_its_invitation_and_long_expired_ones_are_cleaned_up()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var first = await t.InviteAsync(orgId, "grace@x.com", "developer");
        var second = await t.InviteAsync(orgId, "GRACE@x.com", "viewer");

        using var old = await t.PreviewAsync(first);
        using var current = await t.PreviewAsync(second);
        Assert.Equal(HttpStatusCode.NotFound, old.Status);
        Assert.Equal("viewer", current.Body.GetProperty("role").GetString());
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
        Assert.Equal(1L, await t.ScalarAsync<long>(
            "SELECT count(*) FROM orvano.events WHERE type = 'platform.invitation.revoked' AND payload->>'reason' = 'replaced'"));

        await t.InviteAsync(orgId, "old31@x.com", "viewer");
        await t.InviteAsync(orgId, "old29@x.com", "viewer");
        await t.ExecuteAsync("UPDATE orvano.platform_invitations SET expires_at = now() - interval '31 days' WHERE email = 'old31@x.com'");
        await t.ExecuteAsync("UPDATE orvano.platform_invitations SET expires_at = now() - interval '29 days' WHERE email = 'old29@x.com'");
        await t.InviteAsync(orgId, "fresh@x.com", "viewer");

        var emails = await t.ScalarAsync<string[]>("SELECT array_agg(email ORDER BY email) FROM orvano.platform_invitations");
        Assert.Equal(["fresh@x.com", "GRACE@x.com", "old29@x.com"], emails); // the replacement keeps the email as last typed
        Assert.Equal(1L, await t.ScalarAsync<long>(
            "SELECT count(*) FROM orvano.events WHERE type = 'platform.invitation.revoked' AND payload->>'reason' = 'expired'"));
    }

    [Fact]
    public async Task Two_racing_creates_for_one_email_leave_one_invitation_and_no_500()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();

        // Five, the most one address may be invited in an hour (spec 0014, AC-23).
        var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            t.SendAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = "race@x.com", role = "developer" })));

        Assert.All(replies, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
        foreach (var r in replies) r.Dispose();
    }

    [Fact]
    public async Task An_expired_invitation_is_listed_as_expired_answers_410_and_can_be_resent()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, Dev, "developer");
        await t.ExecuteAsync("UPDATE orvano.platform_invitations SET expires_at = now() - interval '1 second'");

        using var list = await t.SendAsync(HttpMethod.Get, $"/v1/console/orgs/{orgId}/invitations");
        Assert.Equal("expired", Assert.Single(list.Body.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());

        using var preview = await t.PreviewAsync(token);
        using var accept = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev);
        using var mismatch = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Outsider);
        Assert.Equal((HttpStatusCode.Gone, ErrorCode.InvitationExpired), (preview.Status, preview.Code));
        Assert.Equal((HttpStatusCode.Gone, ErrorCode.InvitationExpired), (accept.Status, accept.Code));
        Assert.Equal((HttpStatusCode.Gone, ErrorCode.InvitationExpired), (mismatch.Status, mismatch.Code)); // expiry wins over the email

        var resent = await t.InviteAsync(orgId, Dev, "developer");
        using var works = await t.PreviewAsync(resent);
        Assert.Equal(HttpStatusCode.OK, works.Status);
    }

    [Fact]
    public async Task Another_email_gets_403_and_keeps_the_invitation_and_bad_tokens_answer_by_format()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, "someone@x.com", "developer");

        using var accept = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Outsider);
        using var signUp = await t.SignUpAsync("impostor@x.com", token);
        Assert.Equal((HttpStatusCode.Forbidden, ErrorCode.InvitationEmailMismatch), (accept.Status, accept.Code));
        Assert.Equal((HttpStatusCode.Forbidden, ErrorCode.InvitationEmailMismatch), (signUp.Status, signUp.Code));
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.auth_users WHERE email = 'impostor@x.com'"));

        using var empty = await t.PreviewAsync("");
        using var short42 = await t.PreviewAsync(new string('a', 42));
        using var unknown = await t.PreviewAsync(new string('a', 43));
        using var badSignUp = await t.SignUpAsync("someone@x.com", new string('a', 42));
        Assert.Equal((HttpStatusCode.BadRequest, ErrorCode.InvalidRequest), (empty.Status, empty.Code));
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.InvitationNotFound), (short42.Status, short42.Code));
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.InvitationNotFound), (unknown.Status, unknown.Code));
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.InvitationNotFound), (badSignUp.Status, badSignUp.Code));
    }

    [Fact]
    public async Task Two_racing_accepts_of_one_token_make_exactly_one_membership()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, Dev, "developer");
        await ConsoleSignIn.CookieAsync(t.Http, Dev, Ct); // sign in once, before the race

        var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev)));

        Assert.Single(replies, r => r.Status == HttpStatusCode.OK);
        Assert.All(replies.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Equal(ErrorCode.InvitationNotFound, r.Code));
        Assert.Equal(1L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_memberships WHERE org_id = @o AND role = 'developer'",
            ("o", Guid.Parse(orgId))));
        foreach (var r in replies) r.Dispose();
    }

    [Fact]
    public async Task Create_refuses_in_the_spec_order()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var url = $"/v1/console/orgs/{orgId}/invitations";
        await t.AddMemberAsync(orgId, Dev, "developer");

        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = "not-an-email", role = "viewer" }, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, Outsider);
        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = "a@x.com", role = "viewer" }, HttpStatusCode.NotFound, ErrorCode.NotFound, Outsider);
        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = "a@x.com", role = "viewer" }, HttpStatusCode.Forbidden, ErrorCode.Forbidden, Dev);
        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = Dev.ToUpperInvariant(), role = "viewer" }, HttpStatusCode.Conflict, ErrorCode.AlreadyMember);
        await t.AssertProblemAsync(HttpMethod.Get, url, null, HttpStatusCode.Forbidden, ErrorCode.Forbidden, Dev);
        await t.AssertProblemAsync(HttpMethod.Get, url, null, HttpStatusCode.NotFound, ErrorCode.NotFound, Outsider);

        await t.ExecuteAsync(
            "INSERT INTO orvano.platform_invitations (org_id, email, role, token_hash, invited_by_user_id, expires_at) " +
            "SELECT @o, 'bulk' || n || '@x.com', 'viewer', sha256(n::text::bytea), @o, now() + interval '1 day' FROM generate_series(1, 100) n",
            ("o", Guid.Parse(orgId)));
        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = "one-more@x.com", role = "viewer" }, HttpStatusCode.Conflict, ErrorCode.InvitationLimit);
        await t.InviteAsync(orgId, "bulk7@x.com", "owner"); // a replace keeps the count at 100

        await t.ExecuteAsync("UPDATE orvano.platform_orgs SET status = 'deleting', deleted_at = now(), purge_after = now() + interval '7 days' WHERE id = @o",
            ("o", Guid.Parse(orgId)));
        await t.AssertProblemAsync(HttpMethod.Post, url, new { email = "a@x.com", role = "viewer" }, HttpStatusCode.Conflict, ErrorCode.OrgNotActive);
        var invitationId = await t.ScalarAsync<Guid>("SELECT id FROM orvano.platform_invitations LIMIT 1");
        await t.AssertProblemAsync(HttpMethod.Delete, $"{url}/{invitationId}", null, HttpStatusCode.Conflict, ErrorCode.OrgNotActive);
    }

    [Fact]
    public async Task Owners_revoke_invitations_and_the_link_stops_working()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var token = await t.InviteAsync(orgId, "grace@x.com", "developer");
        var id = await t.ScalarAsync<Guid>("SELECT id FROM orvano.platform_invitations");

        using var revoked = await t.SendAsync(HttpMethod.Delete, $"/v1/console/orgs/{orgId}/invitations/{id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.Status);
        using var preview = await t.PreviewAsync(token);
        Assert.Equal(HttpStatusCode.NotFound, preview.Status);
        await t.AssertProblemAsync(HttpMethod.Delete, $"/v1/console/orgs/{orgId}/invitations/{id}", null, HttpStatusCode.NotFound, ErrorCode.NotFound);
        Assert.Equal(1L, await t.ScalarAsync<long>(
            "SELECT count(*) FROM orvano.events WHERE type = 'platform.invitation.revoked' AND payload->>'reason' = 'revoked'"));
    }

    [Fact]
    public async Task Owners_change_roles_and_remove_members_and_access_ends_on_the_next_request()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var devId = await t.AddMemberAsync(orgId, Dev, "developer");
        var members = $"/v1/console/orgs/{orgId}/members";

        using var same = await t.SendAsync(HttpMethod.Patch, $"{members}/{devId}", new { role = "developer" });
        Assert.Equal(HttpStatusCode.OK, same.Status);
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.events WHERE type = 'platform.member.role_changed'"));

        using var viewer = await t.SendAsync(HttpMethod.Patch, $"{members}/{devId}", new { role = "viewer" });
        Assert.Equal("viewer", viewer.Body.GetProperty("role").GetString());
        Assert.Equal(Dev, viewer.Body.GetProperty("email").GetString());
        using var asViewer = await t.SendAsync(HttpMethod.Get, $"/v1/console/orgs/{orgId}", session: Dev);
        Assert.Equal("viewer", asViewer.Body.GetProperty("role").GetString());
        Assert.Equal("developer>viewer", await t.ScalarAsync<string>(
            "SELECT (payload->>'from') || '>' || (payload->>'to') FROM orvano.events WHERE type = 'platform.member.role_changed'"));

        using var list = await t.SendAsync(HttpMethod.Get, members, session: Dev);
        Assert.Equal(2, list.Body.GetProperty("items").GetArrayLength());

        using var removed = await t.SendAsync(HttpMethod.Delete, $"{members}/{devId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        await t.AssertProblemAsync(HttpMethod.Get, $"/v1/console/orgs/{orgId}", null, HttpStatusCode.NotFound, ErrorCode.NotFound, Dev);
        await t.AssertProblemAsync(HttpMethod.Get, members, null, HttpStatusCode.NotFound, ErrorCode.NotFound, Dev);
        Assert.Equal("removed", await t.ScalarAsync<string>("SELECT payload->>'reason' FROM orvano.events WHERE type = 'platform.member.removed'"));
    }

    [Fact]
    public async Task The_last_owner_can_not_be_demoted_removed_or_leave_but_others_can_leave()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var ownerId = await t.UserIdAsync(Owner);
        var viewerId = await t.AddMemberAsync(orgId, Dev, "viewer");
        var members = $"/v1/console/orgs/{orgId}/members";

        await t.AssertProblemAsync(HttpMethod.Patch, $"{members}/{ownerId}", new { role = "developer" }, HttpStatusCode.Conflict, ErrorCode.LastOwner);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{ownerId}", null, HttpStatusCode.Conflict, ErrorCode.LastOwner);

        // A viewer can't touch others (even an unknown ID gets 403 first), but can leave.
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{ownerId}", null, HttpStatusCode.Forbidden, ErrorCode.Forbidden, Dev);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{Guid.NewGuid()}", null, HttpStatusCode.Forbidden, ErrorCode.Forbidden, Dev);
        await t.AssertProblemAsync(HttpMethod.Patch, $"{members}/not-a-uuid", new { role = "owner" }, HttpStatusCode.Forbidden, ErrorCode.Forbidden, Dev);
        await t.AssertProblemAsync(HttpMethod.Get, members, null, HttpStatusCode.NotFound, ErrorCode.NotFound, Outsider);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{await t.UserIdAsync(Outsider)}", null, HttpStatusCode.NotFound, ErrorCode.NotFound, Outsider);
        await t.AssertProblemAsync(HttpMethod.Patch, $"{members}/{Guid.NewGuid()}", new { role = "owner" }, HttpStatusCode.NotFound, ErrorCode.NotFound);

        using var left = await t.SendAsync(HttpMethod.Delete, $"{members}/{viewerId}", session: Dev);
        Assert.Equal(HttpStatusCode.NoContent, left.Status);
        Assert.Equal("left", await t.ScalarAsync<string>("SELECT payload->>'reason' FROM orvano.events WHERE type = 'platform.member.removed'"));

        // With a second owner, the first may step down.
        var secondOwner = await t.AddMemberAsync(orgId, Outsider, "owner");
        using var demoted = await t.SendAsync(HttpMethod.Patch, $"{members}/{ownerId}", new { role = "developer" });
        Assert.Equal(HttpStatusCode.OK, demoted.Status);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{secondOwner}", null, HttpStatusCode.Forbidden, ErrorCode.Forbidden);
    }

    [Fact]
    public async Task Two_owners_racing_to_demote_leave_exactly_one_owner()
    {
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var ownerId = await t.UserIdAsync(Owner);
        var otherId = await t.AddMemberAsync(orgId, Dev, "owner");
        await ConsoleSignIn.CookieAsync(t.Http, Dev, Ct);
        var owners = "SELECT count(*) FROM orvano.platform_memberships WHERE org_id = @o AND role = 'owner'";

        // Each demotes the other: the org lock serializes them, and the loser is no longer an owner (403).
        var each = await Task.WhenAll(
            t.SendAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{otherId}", new { role = "viewer" }),
            t.SendAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{ownerId}", new { role = "viewer" }, Dev));
        Assert.Single(each, r => r.Status == HttpStatusCode.OK);
        Assert.Single(each, r => r.Code == ErrorCode.Forbidden);
        Assert.Equal(1L, await t.ScalarAsync<long>(owners, ("o", Guid.Parse(orgId))));

        // Each demotes themselves: the loser would be the last owner (409).
        await t.ExecuteAsync("UPDATE orvano.platform_memberships SET role = 'owner' WHERE org_id = @o", ("o", Guid.Parse(orgId)));
        var selves = await Task.WhenAll(
            t.SendAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{ownerId}", new { role = "viewer" }),
            t.SendAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{otherId}", new { role = "viewer" }, Dev));
        Assert.Single(selves, r => r.Status == HttpStatusCode.OK);
        Assert.Single(selves, r => r.Code == ErrorCode.LastOwner);
        Assert.Equal(1L, await t.ScalarAsync<long>(owners, ("o", Guid.Parse(orgId))));
        foreach (var r in each.Concat(selves)) r.Dispose();
    }

    [Fact]
    public async Task Accounts_carry_the_install_admin_flag_setup_tells_whether_sign_up_is_open_and_keys_name_their_creator()
    {
        await using var t = await StartAsync();

        using var owner = await t.SendAsync(HttpMethod.Get, "/v1/console/account");
        using var dev = await t.SendAsync(HttpMethod.Get, "/v1/console/account", session: Dev);
        Assert.True(owner.Body.GetProperty("isInstallAdmin").GetBoolean());
        Assert.False(dev.Body.GetProperty("isInstallAdmin").GetBoolean());

        using var closed = await t.GetSetupAsync();
        Assert.False(closed.Body.GetProperty("signupOpen").GetBoolean());
        using var opened = await t.SendAsync(HttpMethod.Patch, "/v1/console/install/settings", new { consoleSignup = "open" });
        using var open = await t.GetSetupAsync();
        Assert.True(open.Body.GetProperty("signupOpen").GetBoolean());

        var projectId = "keysproject0001";
        var orgId = await t.CreateOrgAsync();
        await t.ExecuteAsync("INSERT INTO orvano.platform_projects (id, org_id, kind, name, status, created_by_user_id) VALUES (@p, @o, 'app', 'Keys', 'active', @u)",
            ("p", projectId), ("o", Guid.Parse(orgId)), ("u", await t.UserIdAsync(Owner)));
        using var created = await t.SendAsync(HttpMethod.Post, "/v1/console/project/keys", new { name = "Backend", scopes = new[] { "users.read" } }, project: projectId);
        Assert.Equal(Owner, created.Body.GetProperty("apiKey").GetProperty("createdBy").GetProperty("email").GetString());

        await t.ExecuteAsync("UPDATE orvano.platform_api_keys SET created_by_user_id = @gone WHERE project_id = @p", ("gone", Guid.NewGuid()), ("p", projectId));
        using var listed = await t.SendAsync(HttpMethod.Get, "/v1/console/project/keys", project: projectId);
        Assert.Equal(JsonValueKind.Null, Assert.Single(listed.Body.GetProperty("items").EnumerateArray()).GetProperty("createdBy").ValueKind);
    }

    [Fact]
    public async Task The_61st_preview_in_a_minute_from_one_IP_gets_429()
    {
        await using var t = await StartAsync();
        for (var i = 0; i < 60; i++)
        {
            using var reply = await t.PreviewAsync(new string('a', 43));
            Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        }

        using var limited = await t.PreviewAsync(new string('a', 43));
        Assert.Equal((HttpStatusCode.TooManyRequests, ErrorCode.RateLimited), (limited.Status, limited.Code));
    }

    [Fact]
    public async Task Invitations_list_oldest_first_in_pages_and_a_gone_inviter_reads_as_null()
    {
        // covers: AC-4, AC-5
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        await t.InviteAsync(orgId, "a@x.com", "viewer");
        await t.InviteAsync(orgId, "b@x.com", "developer");
        var third = await t.InviteAsync(orgId, "c@x.com", "owner");
        var url = $"/v1/console/orgs/{orgId}/invitations";

        using var first = await t.SendAsync(HttpMethod.Get, $"{url}?limit=2");
        Assert.Equal(["a@x.com", "b@x.com"], first.Body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("email").GetString()));
        var cursor = first.Body.GetProperty("nextCursor").GetString()!;

        await t.ExecuteAsync("UPDATE orvano.platform_invitations SET invited_by_user_id = @gone WHERE email = 'c@x.com'", ("gone", Guid.NewGuid()));
        using var second = await t.SendAsync(HttpMethod.Get, $"{url}?limit=2&cursor={Uri.EscapeDataString(cursor)}");
        var last = Assert.Single(second.Body.GetProperty("items").EnumerateArray());
        Assert.Equal("c@x.com", last.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("invitedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.Body.GetProperty("nextCursor").ValueKind);

        using var preview = await t.PreviewAsync(third);
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Equal(JsonValueKind.Null, preview.Body.GetProperty("invitedByName").ValueKind);
    }

    [Fact]
    public async Task Members_list_pages_oldest_first_and_skips_a_membership_whose_account_is_gone()
    {
        // covers: AC-8
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        await t.ExecuteAsync("INSERT INTO orvano.platform_memberships (org_id, user_id, role) VALUES (@o, @u, 'viewer')",
            ("o", Guid.Parse(orgId)), ("u", Guid.NewGuid()));
        var devId = await t.AddMemberAsync(orgId, Dev, "developer");
        var url = $"/v1/console/orgs/{orgId}/members?limit=1";

        using var first = await t.SendAsync(HttpMethod.Get, url);
        Assert.Equal(Owner, Assert.Single(first.Body.GetProperty("items").EnumerateArray()).GetProperty("email").GetString());

        // The second membership's account is gone: its page comes back empty, but paging goes on.
        using var second = await t.SendAsync(HttpMethod.Get, $"{url}&cursor={Uri.EscapeDataString(first.Body.GetProperty("nextCursor").GetString()!)}");
        Assert.Equal(0, second.Body.GetProperty("items").GetArrayLength());
        var cursor = second.Body.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        using var third = await t.SendAsync(HttpMethod.Get, $"{url}&cursor={Uri.EscapeDataString(cursor)}");
        var dev = Assert.Single(third.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(devId.ToString(), dev.GetProperty("userId").GetString());
        Assert.Equal(
            await t.ScalarAsync<DateTime>("SELECT created_at FROM orvano.platform_memberships WHERE org_id = @o AND user_id = @u",
                ("o", Guid.Parse(orgId)), ("u", devId)),
            dev.GetProperty("joinedAt").GetDateTimeOffset().UtcDateTime,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(JsonValueKind.Null, third.Body.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Every_membership_or_invitation_change_in_a_deleting_org_answers_409()
    {
        // covers: AC-5, AC-6, AC-7, AC-9, AC-10
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var devId = await t.AddMemberAsync(orgId, Dev, "developer");
        var forOutsider = await t.InviteAsync(orgId, Outsider, "viewer");
        var forNewcomer = await t.InviteAsync(orgId, "newcomer@x.com", "developer");
        await t.ExecuteAsync("UPDATE orvano.platform_orgs SET status = 'deleting', deleted_at = now(), purge_after = now() + interval '7 days' WHERE id = @o",
            ("o", Guid.Parse(orgId)));
        var members = $"/v1/console/orgs/{orgId}/members";

        await t.AssertProblemAsync(HttpMethod.Patch, $"{members}/{devId}", new { role = "viewer" }, HttpStatusCode.Conflict, ErrorCode.OrgNotActive);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{devId}", null, HttpStatusCode.Conflict, ErrorCode.OrgNotActive);
        await t.AssertProblemAsync(HttpMethod.Delete, $"{members}/{devId}", null, HttpStatusCode.Conflict, ErrorCode.OrgNotActive, Dev);
        await t.AssertProblemAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token = forOutsider }, HttpStatusCode.Conflict, ErrorCode.OrgNotActive, Outsider);
        using var preview = await t.PreviewAsync(forOutsider);
        using var signUp = await t.SignUpAsync("newcomer@x.com", forNewcomer);
        Assert.Equal((HttpStatusCode.Conflict, ErrorCode.OrgNotActive), (preview.Status, preview.Code));
        Assert.Equal((HttpStatusCode.Conflict, ErrorCode.OrgNotActive), (signUp.Status, signUp.Code));

        Assert.Equal("developer", await t.ScalarAsync<string>("SELECT role FROM orvano.platform_memberships WHERE user_id = @u AND org_id = @o",
            ("u", devId), ("o", Guid.Parse(orgId))));
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.auth_users WHERE email = 'newcomer@x.com'"));
        Assert.Equal(2L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_memberships WHERE org_id = @o", ("o", Guid.Parse(orgId))));
    }

    [Fact]
    public async Task Bad_bodies_answer_400_before_anything_else()
    {
        // covers: AC-1, AC-3, AC-9
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var devId = await t.AddMemberAsync(orgId, Dev, "developer");
        var invitations = $"/v1/console/orgs/{orgId}/invitations";

        // An outsider would get 404, so a 400 here proves the body is checked first.
        await t.AssertProblemAsync(HttpMethod.Post, invitations, new { email = "a@x.com", role = "admin" }, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, Outsider);
        await t.AssertProblemAsync(HttpMethod.Post, invitations, new { email = "a@x.com" }, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, Outsider);
        await t.AssertProblemAsync(HttpMethod.Post, invitations, new { email = new string('a', 309) + "@example.com", role = "viewer" },
            HttpStatusCode.BadRequest, ErrorCode.InvalidRequest);
        await t.AssertProblemAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{devId}", new { role = "admin" }, HttpStatusCode.BadRequest, ErrorCode.InvalidRequest, Outsider);
        await t.AssertProblemAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{devId}", new { role = "viewer" }, HttpStatusCode.NotFound, ErrorCode.NotFound, Outsider);
        Assert.Equal(0L, await t.ScalarAsync<long>("SELECT count(*) FROM orvano.platform_invitations"));
    }

    [Fact]
    public async Task A_removed_member_loses_the_orgs_projects_at_once_and_the_keys_they_made_stay()
    {
        // covers: AC-10, AC-14
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        const string projectId = "removedproject01";
        await t.ExecuteAsync("INSERT INTO orvano.platform_projects (id, org_id, kind, name, status, created_by_user_id) VALUES (@p, @o, 'app', 'Shop', 'active', @u)",
            ("p", projectId), ("o", Guid.Parse(orgId)), ("u", await t.UserIdAsync(Owner)));
        var devId = await t.AddMemberAsync(orgId, Dev, "developer");
        using var key = await t.SendAsync(HttpMethod.Post, "/v1/console/project/keys", new { name = "Dev key", scopes = new[] { "users.read" } }, Dev, projectId);
        Assert.Equal(HttpStatusCode.Created, key.Status);

        using var removed = await t.SendAsync(HttpMethod.Delete, $"/v1/console/orgs/{orgId}/members/{devId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);

        using var asRemoved = await t.SendAsync(HttpMethod.Get, "/v1/console/project/keys", session: Dev, project: projectId);
        Assert.Equal((HttpStatusCode.NotFound, ErrorCode.ProjectNotFound), (asRemoved.Status, asRemoved.Code));
        using var asOwner = await t.SendAsync(HttpMethod.Get, "/v1/console/project/keys", project: projectId);
        var kept = Assert.Single(asOwner.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(devId.ToString(), kept.GetProperty("createdByUserId").GetString());
        Assert.Equal(Dev, kept.GetProperty("createdBy").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Invitation_and_member_events_carry_ids_the_actor_and_role_values_only()
    {
        // covers: AC-11
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        var ownerId = await t.UserIdAsync(Owner);
        var devId = await t.UserIdAsync(Dev);
        var token = await t.InviteAsync(orgId, Dev, "developer");
        var invitationId = await t.ScalarAsync<Guid>("SELECT id FROM orvano.platform_invitations");
        using var accepted = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token }, Dev);
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        using var changed = await t.SendAsync(HttpMethod.Patch, $"/v1/console/orgs/{orgId}/members/{devId}", new { role = "viewer" });
        Assert.Equal(HttpStatusCode.OK, changed.Status);

        Assert.Equal($"developer|{invitationId}|{ownerId}", await t.ScalarAsync<string>(
            "SELECT concat_ws('|', payload->>'role', payload->>'invitationId', payload->'actor'->>'id') FROM orvano.events WHERE type = 'platform.invitation.created'"));
        Assert.Equal($"{devId}|developer|{devId}", await t.ScalarAsync<string>(
            "SELECT concat_ws('|', payload->>'userId', payload->>'role', payload->'actor'->>'id') FROM orvano.events WHERE type = 'platform.invitation.accepted'"));
        Assert.Equal("developer", await t.ScalarAsync<string>(
            "SELECT payload->>'role' FROM orvano.events WHERE type = 'platform.member.added' AND payload->>'invitationId' = @i", ("i", invitationId.ToString())));
        Assert.Equal("role", await t.ScalarAsync<string>(
            "SELECT payload->'changed'->>0 FROM orvano.events WHERE type = 'platform.member.role_changed'"));

        var fields = await t.ScalarAsync<string[]>(
            "SELECT array_agg(DISTINCT k ORDER BY k) FROM orvano.events, json_object_keys(payload::json) k " +
            "WHERE type LIKE 'platform.invitation.%' OR type LIKE 'platform.member.%'");
        Assert.Subset(new HashSet<string>(["actor", "changed", "from", "invitationId", "membershipId", "orgId", "reason", "role", "to", "userId"]), fields.ToHashSet());
        await t.AssertNoLeakAsync(token, Dev);
    }

    [Fact]
    public async Task Invitation_creates_and_accepts_are_rate_limited_per_account()
    {
        // covers: AC-1, AC-6
        await using var t = await StartAsync();
        var orgId = await t.CreateOrgAsync();
        for (var i = 0; i < 60; i++) await t.InviteAsync(orgId, $"limit{i}@x.com", "viewer");
        await t.AssertProblemAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email = "limit60@x.com", role = "viewer" },
            HttpStatusCode.TooManyRequests, ErrorCode.RateLimited);

        for (var i = 0; i < 30; i++)
        {
            using var reply = await t.SendAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token = new string('a', 43) }, Dev);
            Assert.Equal(HttpStatusCode.NotFound, reply.Status);
        }

        await t.AssertProblemAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token = new string('a', 43) },
            HttpStatusCode.TooManyRequests, ErrorCode.RateLimited, Dev);
        // The limit is the account's own: another account still gets its answer.
        await t.AssertProblemAsync(HttpMethod.Post, "/v1/console/invitations/accept", new { token = new string('a', 43) },
            HttpStatusCode.NotFound, ErrorCode.InvitationNotFound, Outsider);
    }

    private async Task<Harness> StartAsync()
    {
        var database = await postgres.NewDatabaseAsync();
        await database.MigrateAsync();
        var fixtures = Path.Combine(Path.GetTempPath(), $"orvano-fixtures-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(fixtures, ConsoleSignIn.Fixtures(Owner, Dev, Outsider), Ct);
        var api = OrvanoProcess.Start(["api"], new Dictionary<string, string>
        {
            ["ORVANO_DB_URL"] = database.AppUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Test",
            ["ORVANO_TEST_FIXTURES"] = fixtures,
        }, listen: true);
        try
        {
            await api.WaitUntilListeningAsync();
            return new Harness(api, database);
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    private sealed record Reply(HttpStatusCode Status, JsonDocument? Document) : IDisposable
    {
        public JsonElement Body => Document!.RootElement;

        public string? Code => Document?.RootElement.TryGetProperty("code", out var code) == true ? code.GetString() : null;

        public void Dispose() => Document?.Dispose();
    }

    private sealed class Harness(OrvanoProcess api, TestDatabase database) : IAsyncDisposable
    {
        public HttpClient Http { get; } = api.Http();

        public async Task<Reply> SendAsync(HttpMethod method, string url, object? body = null, string session = Owner, string? project = null)
        {
            using var request = new HttpRequestMessage(method, url);
            await ConsoleSignIn.AuthorizeAsync(Http, request, session, Ct);
            if (project is not null) request.Headers.Add("X-Orvano-Project", project);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await ReadAsync(request);
        }

        public Task<Reply> PreviewAsync(string token) => SendOpenAsync(HttpMethod.Post, "/v1/console/invitations/preview", new { token });

        public Task<Reply> SignUpAsync(string email, string inviteToken) =>
            SendOpenAsync(HttpMethod.Post, "/v1/console/account", new { email, password = NewPassword, inviteToken });

        public Task<Reply> GetSetupAsync() => SendOpenAsync(HttpMethod.Get, "/v1/console/install/setup", null);

        public async Task<string> CreateOrgAsync()
        {
            using var org = await SendAsync(HttpMethod.Post, "/v1/console/orgs", new { name = "Acme" });
            Assert.Equal(HttpStatusCode.Created, org.Status);
            return org.Body.GetProperty("id").GetString()!;
        }

        /// <summary>Creates an invitation as the owner and returns its token, from the url's fragment.</summary>
        public async Task<string> InviteAsync(string orgId, string email, string role)
        {
            using var created = await SendAsync(HttpMethod.Post, $"/v1/console/orgs/{orgId}/invitations", new { email, role });
            Assert.True(created.Status == HttpStatusCode.Created, $"invite {email}: {created.Status} {created.Document?.RootElement}");
            var url = created.Body.GetProperty("url").GetString()!;
            return url[(url.IndexOf('#') + 1)..];
        }

        /// <summary>Adds a fixture account to the org directly and returns its user ID.</summary>
        public async Task<Guid> AddMemberAsync(string orgId, string email, string role)
        {
            var userId = await UserIdAsync(email);
            await ExecuteAsync("INSERT INTO orvano.platform_memberships (org_id, user_id, role) VALUES (@o, @u, @r)",
                ("o", Guid.Parse(orgId)), ("u", userId), ("r", role));
            return userId;
        }

        public Task<Guid> UserIdAsync(string email) =>
            ScalarAsync<Guid>("SELECT id FROM orvano.auth_users WHERE project_id = 'console' AND email = @e", ("e", email));

        public async Task AssertProblemAsync(HttpMethod method, string url, object? body, HttpStatusCode status, string code, string session = Owner)
        {
            using var reply = await SendAsync(method, url, body, session);
            Assert.True(status == reply.Status && code == reply.Code, $"{method} {url} as {session}: expected {status} {code}, got {reply.Status}: {reply.Document?.RootElement}");
        }

        /// <summary>No event payload, problem body, or api log line holds the token or the invited email (AC-1, AC-11).</summary>
        public async Task AssertNoLeakAsync(string token, string email)
        {
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM orvano.events WHERE payload::text ILIKE '%' || @t || '%' OR payload::text ILIKE '%' || @e || '%'", ("t", token), ("e", email)));
            Assert.DoesNotContain(token, api.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(email, api.Output, StringComparison.OrdinalIgnoreCase);
        }

        public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
            TestDatabase.ScalarAsync<T>(database.Superuser, sql, parameters);

        public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) =>
            TestDatabase.ExecuteAsync(database.Superuser, sql, parameters);

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await api.DisposeAsync();
        }

        private async Task<Reply> SendOpenAsync(HttpMethod method, string url, object? body)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            if (body is not null) request.Content = JsonContent.Create(body);
            return await ReadAsync(request);
        }

        private async Task<Reply> ReadAsync(HttpRequestMessage request)
        {
            using var response = await Http.SendAsync(request, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            return new Reply(response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text) : null);
        }
    }
}
