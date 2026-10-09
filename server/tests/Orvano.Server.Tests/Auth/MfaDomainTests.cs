using System.Text;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0013 AC-7, AC-9, AC-10, AC-12, AC-20, AC-25, AC-29: the plain MFA rules, with no database.
public class MfaDomainTests
{
    // RFC 6238 appendix B, SHA-1: the 8 digit values end in these 6 digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Totp_codes_match_the_rfc_6238_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.Code(RfcSecret, step));
    }

    [Fact]
    public void Totp_accepts_one_step_of_drift_each_way_and_nothing_further()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);

        Assert.Equal(step - 1, Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 1), now, null));
        Assert.Equal(step, Totp.Match(RfcSecret, Totp.Code(RfcSecret, step), now, null));
        Assert.Equal(step + 1, Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 1), now, null));
        Assert.Null(Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 2), now, null));
        Assert.Null(Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 2), now, null));
    }

    [Fact]
    public void Totp_refuses_a_step_already_used_and_any_malformed_code()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);
        var code = Totp.Code(RfcSecret, step);

        Assert.Null(Totp.Match(RfcSecret, code, now, lastUsedStep: step));
        Assert.Equal(step + 1, Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 1), now, lastUsedStep: step));
        Assert.Null(Totp.Match(RfcSecret, null, now, null));
        Assert.Null(Totp.Match(RfcSecret, "12345", now, null));
        Assert.Null(Totp.Match(RfcSecret, "12345a", now, null));
        Assert.Null(Totp.Match(RfcSecret, "１２３４５６", now, null));
    }

    [Fact]
    public void A_totp_secret_is_20_random_bytes_shown_as_32_base32_characters()
    {
        var secret = Totp.NewSecret();
        var encoded = Base32.Encode(secret);

        Assert.Equal(20, secret.Length);
        Assert.Equal(32, encoded.Length);
        Assert.All(encoded, c => Assert.Contains(c, Base32.Alphabet));
        Assert.NotEqual(Base32.Encode(Totp.NewSecret()), encoded);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_the_rfc_4648_vectors_without_padding(string input, string expected) =>
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(input)));

    [Fact]
    public void The_otpauth_uri_url_encodes_the_issuer_and_label()
    {
        var uri = Totp.Uri("Acme & Co", "ada+mfa@example.com", "JBSWY3DPEHPK3PXP");

        Assert.Equal(
            "otpauth://totp/Acme%20%26%20Co:ada%2Bmfa%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Acme%20%26%20Co&algorithm=SHA1&digits=6&period=30",
            uri);
    }

    [Fact]
    public void Recovery_codes_are_10_base32_characters_shown_in_two_groups()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => RecoveryCode.New()).ToList();

        Assert.All(codes, code =>
        {
            Assert.Equal(10, code.Length);
            Assert.All(code, c => Assert.Contains(c, Base32.Alphabet));
            Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", RecoveryCode.Display(code));
        });
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData("K7QXM-2RPLD", "K7QXM2RPLD")]
    [InlineData("k7qxm-2rpld", "K7QXM2RPLD")]
    [InlineData(" K7QXM 2RPLD ", "K7QXM2RPLD")]
    [InlineData("K7QXM2RPLD", "K7QXM2RPLD")]
    public void Recovery_code_input_ignores_case_spaces_and_hyphens(string input, string expected)
    {
        Assert.True(RecoveryCode.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7QXM-2RPL")]
    [InlineData("K7QXM-2RPLD1")]
    [InlineData("K7QXM-2RPL1")]
    [InlineData("K7QXM_2RPLD")]
    public void Recovery_code_input_must_leave_exactly_10_base32_characters(string? input) =>
        Assert.False(RecoveryCode.TryNormalize(input, out _));

    [Fact]
    public void An_mfa_ticket_is_orv_mt_and_43_base64url_characters_in_one_spelling()
    {
        var ticket = MfaTicket.New();

        Assert.Matches("^orv_mt_[A-Za-z0-9_-]{43}$", ticket.Value);
        Assert.True(MfaTicket.TryParse(ticket.Value, out var parsed));
        Assert.Equal(ticket.Hash, parsed.Hash);
        Assert.False(MfaTicket.TryParse(null, out _));
        Assert.False(MfaTicket.TryParse(ticket.Value[..^1], out _));
        Assert.False(MfaTicket.TryParse("orv_el_" + ticket.Value[7..], out _));
        // The last character carries 2 unused bits: another spelling of the same bytes is refused.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var sibling = ticket.Value[..^1] + alphabet[alphabet.IndexOf(ticket.Value[^1], StringComparison.Ordinal) ^ 1];
        Assert.False(MfaTicket.TryParse(sibling, out _));
    }

    [Theory]
    [InlineData(SessionMethod.Password, new[] { "pwd" })]
    [InlineData(SessionMethod.SignUp, new[] { "pwd" })]
    [InlineData(SessionMethod.Recovery, new[] { "pwd" })]
    [InlineData(SessionMethod.MagicLink, new[] { "email" })]
    [InlineData(SessionMethod.EmailCode, new[] { "email" })]
    [InlineData(SessionMethod.OAuth, new[] { "fed" })]
    [InlineData(SessionMethod.IdToken, new[] { "fed" })]
    public void Step_one_gives_level_1_and_the_method_amr(string method, string[] amr)
    {
        var strength = SessionStrength.StepOne(method);

        Assert.Equal(1, strength.Aal);
        Assert.Equal(amr, strength.Amr);
    }

    [Fact]
    public void A_second_factor_raises_to_level_2_adds_mfa_and_keeps_amr_sorted()
    {
        var strength = SessionStrength.StepOne(SessionMethod.Password).With(SessionStrength.ForFactor(MfaFactors.Totp), aal2: true);
        var again = strength.With(SessionStrength.ForFactor(MfaFactors.RecoveryCode), aal2: false);

        Assert.Equal(2, strength.Aal);
        Assert.Equal(["mfa", "otp", "pwd"], strength.Amr);
        Assert.Equal(2, again.Aal);
        Assert.Equal(["mfa", "otp", "pwd", "rec"], again.Amr);
    }

    [Fact]
    public void A_passkey_adds_swk_when_backed_up_else_hwk_and_user() =>
        Assert.Equal(
            ["hwk", "mfa", "pwd", "user"],
            SessionStrength.StepOne(SessionMethod.Password).With(SessionStrength.ForPasskey(backedUp: false), aal2: true).Amr);

    [Fact]
    public void The_verified_email_rule_blocks_only_an_unverified_email_in_an_app_project()
    {
        var verified = DateTimeOffset.UnixEpoch;

        Assert.True(VerifiedEmailRule.Blocks("p_app", "ada@x.com", null));
        Assert.False(VerifiedEmailRule.Blocks("p_app", "ada@x.com", verified));
        Assert.False(VerifiedEmailRule.Blocks("p_app", null, null));
        Assert.False(VerifiedEmailRule.Blocks("console", "ada@x.com", null));
    }

    [Fact]
    public async Task Claiming_refuses_a_console_account()
    {
        var user = new LockedUser(Guid.NewGuid(), "ada@x.com", null, "active", null, HasPassword: true);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AccountClaims.ClaimAsync(null!, null!, "console", user, Actor.User(user.Id), endSessions: true, CancellationToken.None));

        Assert.Contains("AC-29", refused.Message, StringComparison.Ordinal);
    }

    // AC-3: console passkeys follow ORVANO_PUBLIC_URL alone. On for https with a host name or http://localhost on any
    // port; the RP ID is that host and the only origin is the URL's own.
    [Theory]
    [InlineData("https://orvano.example.com", "orvano.example.com")]
    [InlineData("https://orvano.example.com:8443", "orvano.example.com")]
    [InlineData("https://localhost", "localhost")]
    [InlineData("http://localhost", "localhost")]
    [InlineData("http://localhost:8081", "localhost")]
    public void Console_passkeys_are_on_for_https_with_a_host_name_or_http_localhost(string publicOrigin, string rpId)
    {
        var console = ConsolePasskeys.From(publicOrigin);

        Assert.True(console.Enabled);
        Assert.Equal(rpId, console.RpId);
        Assert.Equal(publicOrigin, console.Origin);
        Assert.Equal("Orvano", ConsolePasskeys.RpName);
    }

    // AC-3: an IP address (no RP ID can name one) or plain http on any other host turns console passkeys off.
    [Theory]
    [InlineData("https://10.0.0.5")]
    [InlineData("https://[2001:db8::1]")]
    [InlineData("http://orvano.example.com")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void Console_passkeys_are_off_for_an_ip_address_or_plain_http_elsewhere(string publicOrigin)
    {
        var console = ConsolePasskeys.From(publicOrigin);

        Assert.False(console.Enabled);
        Assert.Null(console.RpId);
    }
}
