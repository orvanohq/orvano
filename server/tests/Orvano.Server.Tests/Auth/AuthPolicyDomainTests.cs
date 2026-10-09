using System.Net;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0014's settings rules as plain domain code: AC-1's bounds on the merged result, the domain list entries of
// AC-8, and the CIDR rule.
public class AuthPolicyDomainTests
{
    [Fact]
    public void Defaults_meet_every_bound()
    {
        Assert.Null(AuthPolicyRules.Validate(AuthPolicies.Defaults));
        var (change, error) = AuthPolicyRules.Apply(AuthPolicies.Defaults, new AuthPoliciesUpdate());
        Assert.Null(error);
        Assert.Empty(change!.Changed);
    }

    [Theory]
    [InlineData("passwordMinLength", 7)]
    [InlineData("passwordMinLength", 65)]
    [InlineData("accessTokenSeconds", 299)]
    [InlineData("accessTokenSeconds", 3601)]
    [InlineData("sessionIdleSeconds", 3599)]
    [InlineData("sessionAbsoluteSeconds", 31536001)]
    [InlineData("maxSessionsPerUser", 0)]
    [InlineData("maxSessionsPerUser", 1001)]
    [InlineData("signInFailedPerEmailIp.limit", 2)]
    [InlineData("signInFailedPerEmailIp.windowMinutes", 1441)]
    [InlineData("signInFailedPerIp", 9)]
    [InlineData("signUpPerIp", 0)]
    [InlineData("anonymousPerIp", 10001)]
    [InlineData("emailSendPerIp", 9)]
    public void A_value_outside_its_bound_is_refused_and_named(string field, int value)
    {
        var update = field switch
        {
            "passwordMinLength" => new AuthPoliciesUpdate { PasswordMinLength = value },
            "accessTokenSeconds" => new AuthPoliciesUpdate { AccessTokenSeconds = value },
            "sessionIdleSeconds" => new AuthPoliciesUpdate { SessionIdleSeconds = value },
            "sessionAbsoluteSeconds" => new AuthPoliciesUpdate { SessionAbsoluteSeconds = value },
            "maxSessionsPerUser" => new AuthPoliciesUpdate { MaxSessionsPerUser = Patch<int?>.To(value) },
            "signInFailedPerEmailIp.limit" => new AuthPoliciesUpdate { SignInFailedPerEmailIpLimit = value },
            "signInFailedPerEmailIp.windowMinutes" => new AuthPoliciesUpdate { SignInFailedPerEmailIpWindowMinutes = value },
            "signInFailedPerIp" => new AuthPoliciesUpdate { SignInFailedPerIp = value },
            "signUpPerIp" => new AuthPoliciesUpdate { SignUpPerIp = value },
            "anonymousPerIp" => new AuthPoliciesUpdate { AnonymousPerIp = value },
            "emailSendPerIp" => new AuthPoliciesUpdate { EmailSendPerIp = value },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var (change, error) = AuthPolicyRules.Apply(AuthPolicies.Defaults, update);
        Assert.Null(change);
        Assert.StartsWith(field + " ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_update_is_checked_on_the_merged_result()
    {
        // Idle alone at 90 days is in its own bound, but above an absolute lifetime of 30 days stored earlier.
        var stored = AuthPolicies.Defaults with { SessionAbsoluteSeconds = 30 * 86400, SessionIdleSeconds = 86400 };
        var (change, error) = AuthPolicyRules.Apply(stored, new AuthPoliciesUpdate { SessionIdleSeconds = 90 * 86400 });
        Assert.Null(change);
        Assert.Equal("sessionIdleSeconds must be at most sessionAbsoluteSeconds.", error);

        // The failed sign in limit merges per subfield: the window stays when only the limit is sent.
        var (merged, _) = AuthPolicyRules.Apply(AuthPolicies.Defaults, new AuthPoliciesUpdate { SignInFailedPerEmailIpLimit = 5 });
        Assert.Equal(new SignInFailedLimit(5, 15), merged!.Next.SignInFailedPerEmailIp);
        Assert.Equal(["signInFailedPerEmailIp"], merged.Changed);
    }

    [Fact]
    public void Max_sessions_left_out_keeps_and_null_clears()
    {
        var stored = AuthPolicies.Defaults with { MaxSessionsPerUser = 5 };
        Assert.Equal(5, AuthPolicyRules.Apply(stored, new AuthPoliciesUpdate()).Change!.Next.MaxSessionsPerUser);
        var cleared = AuthPolicyRules.Apply(stored, new AuthPoliciesUpdate { MaxSessionsPerUser = Patch<int?>.To(null) }).Change!;
        Assert.Null(cleared.Next.MaxSessionsPerUser);
        Assert.Equal(["maxSessionsPerUser"], cleared.Changed);
    }

    [Fact]
    public void Domain_entries_are_normalized_and_bad_ones_named_by_position()
    {
        var (change, error) = AuthPolicyRules.Apply(AuthPolicies.Defaults,
            new AuthPoliciesUpdate { BlockedEmailDomains = ["Example.COM.", "bücher.example", "example.com", " mail.acme.io "] });
        Assert.Null(error);
        Assert.Equal(["example.com", "xn--bcher-kva.example", "mail.acme.io"], change!.Next.BlockedEmailDomains);

        (_, error) = AuthPolicyRules.Apply(AuthPolicies.Defaults,
            new AuthPoliciesUpdate { AllowedEmailDomains = ["ok.example", ".example.com", "*.example.com", "a@example.com", "localhost", "10.0.0.1", "-x.example", ""] });
        Assert.StartsWith("allowedEmailDomains has bad entries at positions 1, 2, 3, 4, 5, 6, 7:", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_domain_in_both_lists_and_more_than_500_entries_are_refused()
    {
        var (_, both) = AuthPolicyRules.Apply(AuthPolicies.Defaults,
            new AuthPoliciesUpdate { BlockedEmailDomains = ["example.com"], AllowedEmailDomains = ["acme.io", "EXAMPLE.com"] });
        Assert.StartsWith("allowedEmailDomains has bad entries at positions 1:", both, StringComparison.Ordinal);

        var many = Enumerable.Range(0, 501).Select(i => $"d{i}.example").ToList();
        var (_, tooMany) = AuthPolicyRules.Apply(AuthPolicies.Defaults, new AuthPoliciesUpdate { BlockedEmailDomains = many });
        Assert.Equal("blockedEmailDomains can hold at most 500 domains.", tooMany);
    }

    [Theory]
    [InlineData("203.0.113.0/24", "203.0.113.0/24")]
    [InlineData("203.0.113.7", "203.0.113.7/32")]
    [InlineData("10.0.0.0/12", "10.0.0.0/12")]
    [InlineData("2001:db8::/48", "2001:db8::/48")]
    [InlineData("::ffff:203.0.113.0/120", "203.0.113.0/24")]
    public void A_cidr_parses_to_its_canonical_form(string input, string canonical)
    {
        Assert.True(AuthPolicyRules.TryParseCidr(input, out var network));
        Assert.Equal(canonical, network.ToString());
    }

    [Theory]
    [InlineData("10.0.0.0/11")]
    [InlineData("2001:db8::/47")]
    [InlineData("203.0.113.1/24")]
    [InlineData("203.0.113.0/33")]
    [InlineData("example.com")]
    [InlineData("10.0.0.0/x")]
    [InlineData("fe80::1%eth0/128")]
    [InlineData("")]
    public void A_broad_masked_or_malformed_cidr_is_refused(string input)
    {
        Assert.False(AuthPolicyRules.TryParseCidr(input, out _));
    }

    [Fact]
    public void More_than_20_cidrs_and_bad_ones_by_position_are_refused()
    {
        var (_, bad) = AuthPolicyRules.Apply(AuthPolicies.Defaults, new AuthPoliciesUpdate { TrustedServerCidrs = ["203.0.113.0/24", "8.0.0.0/8"] });
        Assert.StartsWith("trustedServerCidrs has bad entries at positions 1:", bad, StringComparison.Ordinal);

        var many = Enumerable.Range(0, 21).Select(i => $"203.0.113.{i}/32").ToList();
        var (_, tooMany) = AuthPolicyRules.Apply(AuthPolicies.Defaults, new AuthPoliciesUpdate { TrustedServerCidrs = many });
        Assert.Equal("trustedServerCidrs can hold at most 20 ranges.", tooMany);
    }

    [Theory]
    [InlineData("a@Example.COM", "example.com")]
    [InlineData("a@b@bücher.example.", "xn--bcher-kva.example")]
    public void An_email_domain_is_taken_after_the_last_at_sign(string email, string domain)
    {
        Assert.True(EmailDomains.TryGetDomain(email, out var found));
        Assert.Equal(domain, found);
    }

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("mail.example.com", "example.com", true)]
    [InlineData("badexample.com", "example.com", false)]
    [InlineData("example.com", "mail.example.com", false)]
    public void A_domain_matches_an_entry_or_its_subdomains(string domain, string entry, bool matches)
    {
        Assert.Equal(matches, EmailDomains.Matches(domain, entry));
    }

    [Fact]
    public void Password_length_counts_code_points_after_nfkc()
    {
        // Four emoji are eight UTF-16 units but four code points.
        Assert.Equal(4, PasswordPolicy.CodePoints("😀😀😀😀"));
        Assert.True(PasswordPolicy.TryNormalize("ﬁﬁﬁﬁ", out var normalized)); // each ligature is two letters under NFKC
        Assert.Equal(8, PasswordPolicy.CodePoints(normalized));
    }

    [Fact]
    public void Changed_fields_name_only_what_differs()
    {
        var after = AuthPolicies.Defaults with { PasswordMinLength = 12, TrustedServerCidrs = [IPNetwork.Parse("203.0.113.0/24")] };
        Assert.Equal(["passwordMinLength", "trustedServerCidrs"], AuthPolicyRules.ChangedFields(AuthPolicies.Defaults, after));
    }
}
