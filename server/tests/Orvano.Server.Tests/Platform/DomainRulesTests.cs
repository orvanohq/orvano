using System.Text;
using Orvano.Core.Paging;
using Orvano.Platform.Contracts;
using Orvano.Platform.Domain;
using ApiKeyScope = Orvano.Contract.ApiKeyScope;

namespace Orvano.Server.Tests.Platform;

// Spec 0003, row 7 build task 2: the business rules as plain types, no database.
public class ProjectIdsTests
{
    [Fact]
    public void A_project_id_is_20_characters_of_lowercase_letters_and_digits()
    {
        for (var i = 0; i < 1000; i++)
            Assert.Matches("^[a-z0-9]{20}$", ProjectIds.New()); // AC-2
    }

    [Fact]
    public void Project_ids_do_not_repeat_and_use_the_whole_alphabet()
    {
        var ids = Enumerable.Range(0, 2000).Select(_ => ProjectIds.New()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(36, ids.SelectMany(id => id).Distinct().Count());
    }
}

public class ConsolePermissionsTests
{
    // The security model's table, row by row (AC-9).
    [Theory]
    [InlineData(nameof(ConsoleAction.View), true, true, true)]
    [InlineData(nameof(ConsoleAction.CreateProject), true, true, false)]
    [InlineData(nameof(ConsoleAction.EditProject), true, true, false)]
    [InlineData(nameof(ConsoleAction.RetryProvisioning), true, true, false)]
    [InlineData(nameof(ConsoleAction.DeleteProject), true, false, false)]
    [InlineData(nameof(ConsoleAction.RestoreProject), true, false, false)]
    [InlineData(nameof(ConsoleAction.RetryPurge), true, false, false)]
    [InlineData(nameof(ConsoleAction.CreateApiKey), true, true, false)]
    [InlineData(nameof(ConsoleAction.ManagePlatforms), true, true, false)]
    [InlineData(nameof(ConsoleAction.ManageMembers), true, false, false)]
    [InlineData(nameof(ConsoleAction.ManageOrg), true, false, false)]
    [InlineData(nameof(ConsoleAction.LeaveOrg), true, true, true)]
    public void Each_role_may_do_exactly_what_the_matrix_says(string name, bool owner, bool developer, bool viewer)
    {
        var action = Enum.Parse<ConsoleAction>(name);
        Assert.Equal(owner, ConsolePermissions.Allows(OrgRole.Owner, action));
        Assert.Equal(developer, ConsolePermissions.Allows(OrgRole.Developer, action));
        Assert.Equal(viewer, ConsolePermissions.Allows(OrgRole.Viewer, action));
    }

    [Theory]
    [InlineData(OrgRole.Owner, false, true)]
    [InlineData(OrgRole.Owner, true, true)]
    [InlineData(OrgRole.Developer, true, true)]
    [InlineData(OrgRole.Developer, false, false)]
    [InlineData(OrgRole.Viewer, true, false)]
    public void Owners_delete_any_key_developers_only_their_own_viewers_none(OrgRole role, bool createdByCaller, bool allowed)
    {
        Assert.Equal(allowed, ConsolePermissions.CanDeleteApiKey(role, createdByCaller));
    }
}

public class OwnerRuleTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void An_org_always_keeps_one_owner(int owners, bool canLoseOne)
    {
        Assert.Equal(canLoseOne, OwnerRule.CanLoseOwner(owners)); // AC-10
    }

    [Theory]
    [InlineData(2, 2, 3, nameof(AccountOrgOutcome.Unaffected))]
    [InlineData(1, 1, 0, nameof(AccountOrgOutcome.DeleteWithAccount))]
    [InlineData(1, 2, 0, nameof(AccountOrgOutcome.Blocks))]
    [InlineData(1, 1, 1, nameof(AccountOrgOutcome.Blocks))]
    public void Deleting_an_owner_account_blocks_only_while_others_or_live_projects_depend_on_it(
        int owners, int members, int liveProjects, string expected)
    {
        Assert.Equal(Enum.Parse<AccountOrgOutcome>(expected), OwnerRule.OnAccountDeleted(owners, members, liveProjects)); // AC-10
    }
}

public class ApiKeySecretTests
{
    [Fact]
    public void A_secret_is_the_marker_plus_43_base64url_characters_with_a_12_character_prefix()
    {
        var secret = ApiKeySecret.New();

        Assert.Matches("^orv_sk_[A-Za-z0-9_-]{43}$", secret.Value); // AC-11
        Assert.Equal(secret.Value[..12], secret.Prefix);
        Assert.StartsWith("orv_sk_", secret.Prefix);
    }

    [Fact]
    public void Only_the_SHA_256_of_the_secret_is_kept()
    {
        var secret = ApiKeySecret.New();

        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(secret.Value)), secret.Hash);
        Assert.DoesNotContain(secret.Value, secret.ToString());
    }

    [Fact]
    public void A_presented_secret_round_trips()
    {
        var secret = ApiKeySecret.New();

        Assert.True(ApiKeySecret.TryParse(secret.Value, out var parsed));
        Assert.Equal(secret.Hash, parsed.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orv_sk_short")]
    [InlineData("orv_pk_Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5a")]
    [InlineData("orv_sk_Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4y!5a")]
    public void A_malformed_secret_is_never_looked_up(string? presented)
    {
        Assert.False(ApiKeySecret.TryParse(presented, out _));
    }
}

public class ApiKeyScopesTests
{
    [Fact]
    public void Scopes_are_stored_as_sorted_unique_wire_values()
    {
        Assert.True(ApiKeyScopes.TryNormalize([ApiKeyScope.UsersWrite, ApiKeyScope.UsersRead, ApiKeyScope.UsersWrite], out var scopes));

        Assert.Equal(["users.read", "users.write"], scopes);
    }

    [Fact]
    public void An_empty_or_unknown_scope_is_refused()
    {
        Assert.False(ApiKeyScopes.TryNormalize([], out _)); // AC-12
        Assert.False(ApiKeyScopes.TryNormalize(null, out _));
        Assert.False(ApiKeyScopes.TryNormalize([ApiKeyScope.UsersRead, ApiKeyScope.Unknown], out _));
    }

    [Fact]
    public void Stored_scopes_that_left_the_catalog_are_ignored()
    {
        Assert.Equal([ApiKeyScope.UsersRead], ApiKeyScopes.Parse(["users.read", "gone.write"]));
    }
}

public class WebOriginPatternTests
{
    [Theory]
    [InlineData("app.example.com", "app.example.com")]
    [InlineData(" App.Example.COM ", "app.example.com")]
    [InlineData("*.example.com", "*.example.com")]
    [InlineData("localhost", "localhost")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("intranet", "intranet")]
    public void Accepts_hostnames_wildcards_localhost_and_IPv4(string raw, string stored)
    {
        Assert.True(WebOriginPattern.TryParse(raw, out var pattern, out _));
        Assert.Equal(stored, pattern.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("*.com")]
    [InlineData("a.*.example.com")]
    [InlineData("*example.com")]
    [InlineData("https://app.example.com")]
    [InlineData("app.example.com:3000")]
    [InlineData("app.example.com/path")]
    [InlineData("-bad.example.com")]
    [InlineData("1.2.3")]
    [InlineData("256.1.1.1")]
    [InlineData("*.127.0.0.1")]
    public void Refuses_anything_else(string raw)
    {
        Assert.False(WebOriginPattern.TryParse(raw, out _, out var problem)); // AC-13
        Assert.NotEmpty(problem);
    }

    [Theory]
    [InlineData("app.example.com", "https://app.example.com", true)]
    [InlineData("app.example.com", "http://app.example.com:8080", true)]
    [InlineData("app.example.com", "https://APP.example.com", true)]
    [InlineData("app.example.com", "https://other.example.com", false)]
    [InlineData("*.example.com", "https://a.example.com", true)]
    [InlineData("*.example.com", "https://example.com", false)]
    [InlineData("*.example.com", "https://a.b.example.com", false)]
    [InlineData("*.example.com", "https://aexample.com", false)]
    [InlineData("localhost", "http://localhost:5173", true)]
    [InlineData("localhost", "http://localhost", true)]
    [InlineData("127.0.0.1", "http://127.0.0.1:3000", true)]
    [InlineData("localhost", "null", false)]
    [InlineData("localhost", "file://localhost", false)]
    [InlineData("localhost", "not an origin", false)]
    public void Matches_a_browser_origin_by_host_on_any_scheme_and_port(string pattern, string origin, bool matches)
    {
        Assert.True(WebOriginPattern.TryParse(pattern, out var parsed, out _));

        Assert.Equal(matches, parsed.Matches(origin)); // AC-13
    }
}

public class PlatformIdentifiersTests
{
    [Theory]
    [InlineData(nameof(PlatformType.Android), "com.example.app", true)]
    [InlineData(nameof(PlatformType.Android), "com.example_2.App", true)]
    [InlineData(nameof(PlatformType.Android), "example", false)]
    [InlineData(nameof(PlatformType.Android), "com.1example.app", false)]
    [InlineData(nameof(PlatformType.Ios), "com.example.app", true)]
    [InlineData(nameof(PlatformType.Ios), "com.example-inc.App", true)]
    [InlineData(nameof(PlatformType.Ios), "com.example_inc.app", false)]
    [InlineData(nameof(PlatformType.Macos), "app", false)]
    [InlineData(nameof(PlatformType.Windows), "Example.Desktop", true)]
    [InlineData(nameof(PlatformType.Linux), "example app", false)]
    [InlineData(nameof(PlatformType.Linux), "", false)]
    [InlineData(nameof(PlatformType.Web), "*.example.com", true)]
    [InlineData(nameof(PlatformType.Web), "*", false)]
    public void Each_type_has_its_own_identifier_rule(string type, string identifier, bool valid)
    {
        Assert.Equal(valid, PlatformIdentifiers.TryNormalize(Enum.Parse<PlatformType>(type), identifier, out _, out _)); // AC-13
    }

    [Fact]
    public void An_identifier_longer_than_255_characters_is_refused()
    {
        Assert.False(PlatformIdentifiers.TryNormalize(PlatformType.Windows, new string('a', 256), out _, out _));
        Assert.True(PlatformIdentifiers.TryNormalize(PlatformType.Windows, new string('a', 255), out _, out _));
    }

    [Fact]
    public void Native_identifiers_are_stored_as_typed()
    {
        Assert.True(PlatformIdentifiers.TryNormalize(PlatformType.Ios, "com.Example.App", out var stored, out _));
        Assert.Equal("com.Example.App", stored);
    }
}

public class NamesTests
{
    [Theory]
    [InlineData("Ada Lovelace", "ada@example.com", "Ada Lovelace's org")]
    [InlineData(null, "ada@example.com", "ada's org")]
    [InlineData("   ", "ada@example.com", "ada's org")]
    public void A_personal_org_is_named_from_the_account(string? name, string email, string expected)
    {
        Assert.Equal(expected, PersonalOrgName.For(name, email)); // AC-8
    }

    [Fact]
    public void A_personal_org_name_is_cut_to_100_characters_without_splitting_a_character()
    {
        var name = new string('a', 98) + "😀";

        var org = PersonalOrgName.For(name, "x@example.com");

        Assert.True(org.Length <= 100);
        Assert.False(char.IsHighSurrogate(org[^1]));
    }

    [Theory]
    [InlineData("  Acme  ", true, "Acme")]
    [InlineData("", false, "")]
    [InlineData("   ", false, "")]
    [InlineData(null, false, "")]
    [InlineData("a\u0000b", false, "a\u0000b")]
    public void Names_are_trimmed_and_1_to_100_characters(string? raw, bool valid, string normalized)
    {
        Assert.Equal(valid, Names.TryNormalize(raw, out var name));
        Assert.Equal(normalized, name);
        Assert.False(Names.TryNormalize(new string('a', 101), out _));
        Assert.True(Names.TryNormalize(new string('a', 100), out _));
    }
}

public class PageCursorTests
{
    [Fact]
    public void A_cursor_round_trips_its_position()
    {
        var position = new PagePosition(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero).AddTicks(1230), "abc:def");

        Assert.True(PageCursor.TryDecode(PageCursor.Encode(position), out var decoded));
        Assert.Equal(position, decoded);
    }

    [Theory]
    [InlineData("not a cursor")]
    [InlineData("")]
    [InlineData("MTIz")] // "123", no id
    [InlineData("OjEyMw")] // ":123", no time
    [InlineData("LTE6YWJj")] // "-1:abc"
    public void Anything_the_server_did_not_issue_is_refused(string cursor)
    {
        Assert.False(PageCursor.TryDecode(cursor, out _));
    }

    [Theory]
    [InlineData(null, 25)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(0, null)]
    [InlineData(101, null)]
    public void The_limit_defaults_to_25_and_is_1_to_100(int? limit, int? expected)
    {
        Assert.Equal(expected, PageCursor.Limit(limit));
    }
}

// Spec 0008: invite tokens, the invitation rules, and Platform's copy of the email rule.
public class InvitationRulesTests
{
    [Fact]
    public void An_invite_token_is_43_base64url_characters_and_its_url_carries_it_in_the_fragment()
    {
        var token = InviteToken.New();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", token.Value); // AC-1
        Assert.Equal(32, token.Hash.Length);
        Assert.Equal($"https://orvano.example.com/invite#{token.Value}", token.Url("https://orvano.example.com"));
        Assert.Equal($"https://orvano.example.com/invite#{token.Value}", token.Url("https://orvano.example.com/"));
        Assert.DoesNotContain(token.Value, token.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 42
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 44
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")] // padding
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")] // base64, not base64url
    public void A_token_that_is_not_exactly_43_base64url_characters_never_parses(string? presented) =>
        Assert.False(InviteToken.TryParse(presented, out _)); // AC-5

    [Fact]
    public void A_parsed_token_hashes_like_the_one_that_was_issued()
    {
        var issued = InviteToken.New();

        Assert.True(InviteToken.TryParse(issued.Value, out var parsed));
        Assert.Equal(issued.Hash, parsed.Hash);
    }

    [Fact]
    public void An_invitation_lasts_7_days_is_expired_at_its_expiry_and_is_cleaned_up_30_days_later()
    {
        var now = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var expires = InvitationRules.ExpiresAt(now);

        Assert.Equal(now.AddDays(7), expires);
        Assert.Equal(InvitationState.Pending, InvitationRules.StateAt(expires, expires.AddTicks(-1)));
        Assert.Equal(InvitationState.Expired, InvitationRules.StateAt(expires, expires)); // AC-4: at or before now
        Assert.Equal(now.AddDays(-30), InvitationRules.CleanupBefore(now));
    }

    [Fact]
    public void An_org_holds_at_most_100_invitations()
    {
        Assert.True(InvitationRules.FitsCap(99));
        Assert.False(InvitationRules.FitsCap(100));
    }

    [Theory]
    [InlineData(" ada@example.com ", true, "ada@example.com")]
    [InlineData("ada@example", true, "ada@example")]
    [InlineData("ada", false, "ada")]
    [InlineData("a b@example.com", false, "a b@example.com")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void Invite_emails_follow_the_sign_up_email_rule(string? email, bool valid, string trimmed)
    {
        Assert.Equal(valid, InviteEmail.TryNormalize(email, out var result));
        Assert.Equal(trimmed, result);
    }

    [Fact]
    public void An_invite_email_is_at_most_320_characters()
    {
        Assert.True(InviteEmail.TryNormalize(new string('a', 310) + "@x.example", out _));
        Assert.False(InviteEmail.TryNormalize(new string('a', 311) + "@x.example", out _));
    }
}
