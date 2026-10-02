using System.Security.Cryptography;
using System.Text;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0010 domain rules: AC-1 (token and code formats), AC-2 (lifetimes), AC-6 (redirect shape and the emailed
// link), AC-17 and AC-19 (the freshness rule).
public class EmailTokenDomainTests
{
    [Fact]
    public void A_link_token_is_orv_el_plus_43_base64url_characters_hashed_whole()
    {
        var token = LinkToken.New();

        Assert.StartsWith("orv_el_", token.Value, StringComparison.Ordinal);
        Assert.Equal(50, token.Value.Length);
        Assert.True(token.Value[7..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token.Value)), token.Hash);
        Assert.NotEqual(token.Value, LinkToken.New().Value);
        Assert.True(LinkToken.TryParse(token.Value, out var parsed));
        Assert.Equal(token.Hash, parsed.Hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orv_el_")]
    [InlineData("orv_rt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("orv_el_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 42 characters
    [InlineData("orv_el_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 characters
    [InlineData("orv_el_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")] // not base64url
    [InlineData("orv_el_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")] // non canonical last character
    [InlineData("ORV_EL_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_malformed_link_token_never_parses(string? value) => Assert.False(LinkToken.TryParse(value, out _));

    [Fact]
    public void An_email_code_is_six_digits_drawn_over_the_whole_range()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => EmailCode.New()).ToList();

        Assert.All(codes, code => Assert.True(EmailCode.IsWellFormed(code)));
        Assert.Contains(codes, code => code[0] == '0'); // zero padded, so leading zeros happen
        Assert.True(codes.Distinct().Count() > 1990);
        Assert.Equal(Encoding.UTF8.GetBytes("0198c0de-0000-7000-8000-000000000001:042137"),
            EmailCode.MacInput(Guid.Parse("0198c0de-0000-7000-8000-000000000001"), "042137"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData(" 123456")]
    [InlineData("１２３４５６")]
    [InlineData(null)]
    public void A_code_that_is_not_exactly_six_ascii_digits_is_malformed(string? code) => Assert.False(EmailCode.IsWellFormed(code));

    [Fact]
    public void Lifetimes_are_the_spec_constants()
    {
        Assert.Equal(TimeSpan.FromHours(24), EmailTokenKinds.Lifetime(EmailTokenKind.Verification));
        Assert.Equal(TimeSpan.FromHours(1), EmailTokenKinds.Lifetime(EmailTokenKind.Recovery));
        Assert.Equal(TimeSpan.FromMinutes(15), EmailTokenKinds.Lifetime(EmailTokenKind.MagicLink));
        Assert.Equal(TimeSpan.FromMinutes(10), EmailTokenKinds.Lifetime(EmailTokenKind.EmailCode));
        Assert.Equal(TimeSpan.FromHours(1), EmailTokenKinds.Lifetime(EmailTokenKind.EmailChange));
    }

    [Theory]
    [InlineData("https://app.example.com/auth/callback", true)]
    [InlineData("https://APP.example.com:8443/x?y=1#frag", true)]
    [InlineData("http://localhost:3000/cb", true)]
    [InlineData("com.acme.app://auth", true)]
    [InlineData("javascript:alert(1)", false)] // no host
    [InlineData("https://ada:pw@app.example.com/", false)] // user info
    [InlineData("https://evil.example\\@app.example.com", false)] // backslash
    [InlineData("https://app.example.com/a b", false)] // whitespace
    [InlineData("https://app.example.com/\u0001", false)] // control character
    [InlineData("https://app.example.com./", false)] // trailing dot
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    public void A_redirect_url_must_have_a_safe_shape(string raw, bool allowed) =>
        Assert.Equal(allowed, RedirectUrlRule.TryCheck(raw, EmailTokenKind.Verification, out _));

    [Fact]
    public void A_redirect_url_is_at_most_2048_characters()
    {
        var prefix = "https://app.example.com/";
        Assert.True(RedirectUrlRule.TryCheck(prefix + new string('a', 2048 - prefix.Length), EmailTokenKind.Recovery, out _));
        Assert.False(RedirectUrlRule.TryCheck(prefix + new string('a', 2049 - prefix.Length), EmailTokenKind.Recovery, out _));
    }

    [Fact]
    public void Links_that_sign_in_never_use_a_custom_scheme()
    {
        Assert.True(RedirectUrlRule.TryCheck("com.acme.app://auth", EmailTokenKind.Verification, out var verify));
        Assert.Equal(RedirectScheme.App, verify.Scheme);
        Assert.True(RedirectUrlRule.TryCheck("com.acme.app://auth", EmailTokenKind.EmailChange, out _));
        Assert.False(RedirectUrlRule.TryCheck("com.acme.app://auth", EmailTokenKind.Recovery, out _));
        Assert.False(RedirectUrlRule.TryCheck("com.acme.app://auth", EmailTokenKind.MagicLink, out _));
        Assert.True(RedirectUrlRule.TryCheck("https://app.example.com", EmailTokenKind.MagicLink, out var web));
        Assert.Equal(RedirectScheme.Web, web.Scheme);
    }

    [Fact]
    public void The_emailed_link_sets_the_type_and_token_and_keeps_the_rest()
    {
        Assert.True(LinkToken.TryParse("orv_el_" + new string('A', 43), out var token));

        Assert.Equal($"https://app.example.com/auth?next=%2Fhome&orvano_type=recovery&orvano_token={token.Value}#top",
            LinkUrl.Build(new Uri("https://app.example.com/auth?orvano_token=old&next=%2Fhome&orvano_type=x#top"), EmailTokenKind.Recovery, token));
        Assert.Equal($"http://localhost:3000/cb?orvano_type=magic_link&orvano_token={token.Value}",
            LinkUrl.Build(new Uri("http://localhost:3000/cb"), EmailTokenKind.MagicLink, token));
        Assert.Equal($"com.acme.app://auth/?orvano_type=email_change&orvano_token={token.Value}",
            LinkUrl.Build(new Uri("com.acme.app://auth"), EmailTokenKind.EmailChange, token));
        Assert.Equal($"https://app.example.com/?orvano_type=verification&orvano_token={token.Value}",
            LinkUrl.Build(new Uri("https://APP.example.com:443"), EmailTokenKind.Verification, token));
    }

    [Fact]
    public void A_session_is_fresh_for_ten_minutes()
    {
        var created = DateTimeOffset.Parse("2026-10-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(SessionFreshness.IsFresh(created, created.AddMinutes(10)));
        Assert.False(SessionFreshness.IsFresh(created, created.AddMinutes(10).AddSeconds(1)));
    }

    [Fact]
    public void A_token_email_matches_the_user_email_ignoring_case()
    {
        Assert.True(EmailRule.SameAddress("Ada@X.com", "ada@x.com"));
        Assert.False(EmailRule.SameAddress("ada@x.com", "eve@x.com"));
        Assert.False(EmailRule.SameAddress(null, "ada@x.com"));
    }
}
