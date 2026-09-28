using System.Security.Cryptography;
using Orvano.Auth.Application;
using Orvano.Auth.Domain;

namespace Orvano.Server.Tests.Auth;

// Spec 0004 domain rules: AC-1 (email), AC-2 (password), AC-6 (claims), AC-8 (refresh decision), AC-9 (expiry),
// AC-31 (client info), AC-34 (Argon2id, refresh token hashing).
public class AuthDomainTests
{
    // Made with argon2-cffi (the reference C implementation), salt 00..0f, so our encoded hashes are standard.
    private const string ReferenceCurrent = "$argon2id$v=19$m=19456,t=2,p=1$AAECAwQFBgcICQoLDA0ODw$quyusfeeuAGbpiw09i+j4VKJ+I/5h3mugd4HghmS/x8";
    private const string ReferenceOlder = "$argon2id$v=19$m=12288,t=3,p=1$AAECAwQFBgcICQoLDA0ODw$QtDcA3mZooFlY5HEEVqnzKsh4wcaNQsWCG8uNQAFoUI";

    [Theory]
    [InlineData("12345678", "12345678")]
    [InlineData("  spaced  ", "  spaced  ")]
    [InlineData("ｐａｓｓｗｏｒｄ", "password")] // fullwidth letters fold under NFKC
    [InlineData("ﬁｎａｌｌｙ!!", "finally!!")] // the fi ligature becomes two letters
    public void Password_policy_normalizes_to_nfkc(string input, string expected)
    {
        Assert.True(PasswordPolicy.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Password_policy_counts_code_points_not_utf16_units()
    {
        var eightEmoji = string.Concat(Enumerable.Repeat("😀", 8)); // 16 UTF-16 units, 8 code points
        var sevenEmoji = string.Concat(Enumerable.Repeat("😀", 7));

        Assert.True(PasswordPolicy.TryNormalize(eightEmoji, out _));
        Assert.False(PasswordPolicy.TryNormalize(sevenEmoji, out _));
        Assert.True(PasswordPolicy.TryNormalize(string.Concat(Enumerable.Repeat("😀", 256)), out _));
        Assert.False(PasswordPolicy.TryNormalize(string.Concat(Enumerable.Repeat("😀", 257)), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void Password_policy_refuses(string? input) => Assert.False(PasswordPolicy.TryNormalize(input, out _));

    [Fact]
    public void Password_policy_refuses_a_lone_surrogate()
    {
        Assert.False(PasswordPolicy.TryNormalize("abc" + '\ud800' + "defgh", out _));
        Assert.False(PasswordPolicy.TryNormalize("abcdefgh" + '\udc00', out _));
    }

    [Fact]
    public void Password_policy_refuses_more_than_256_code_points() =>
        Assert.False(PasswordPolicy.TryNormalize(new string('a', 257), out _));

    [Theory]
    [InlineData("ada@example.com", "ada@example.com")]
    [InlineData("  Ada@Example.com ", "Ada@Example.com")]
    [InlineData("a@b", "a@b")]
    public void Email_rule_trims_and_accepts(string input, string expected)
    {
        Assert.True(EmailRule.TryNormalize(input, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ada")]
    [InlineData("ada@")]
    [InlineData("@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("a@b@c")]
    public void Email_rule_refuses(string? input) => Assert.False(EmailRule.TryNormalize(input, out _));

    [Fact]
    public void Email_rule_caps_the_length_at_320()
    {
        var local = new string('a', 64);
        var ok = local + "@" + new string('b', 320 - 65);
        Assert.True(EmailRule.TryNormalize(ok, out _));
        Assert.False(EmailRule.TryNormalize(ok + "b", out _));
    }

    [Fact]
    public async Task Hashes_in_the_standard_encoded_form_and_verifies()
    {
        using var hasher = new PasswordHasher();

        var hash = await hasher.TryHashAsync("correct horse battery", TestContext.Current.CancellationToken);

        Assert.NotNull(hash);
        Assert.Matches(@"^\$argon2id\$v=19\$m=19456,t=2,p=1\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$", hash);
        Assert.Equal(new PasswordCheck(true, false), await hasher.TryVerifyAsync("correct horse battery", hash, TestContext.Current.CancellationToken));
        Assert.Equal(new PasswordCheck(false, false), await hasher.TryVerifyAsync("correct horse batterY", hash, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Verifies_hashes_from_the_reference_implementation()
    {
        using var hasher = new PasswordHasher();

        Assert.Equal(new PasswordCheck(true, false), await hasher.TryVerifyAsync("correct horse battery", ReferenceCurrent, TestContext.Current.CancellationToken));
        Assert.Equal(new PasswordCheck(false, false), await hasher.TryVerifyAsync("wrong horse battery", ReferenceCurrent, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Asks_for_a_rehash_when_the_stored_parameters_are_not_todays()
    {
        using var hasher = new PasswordHasher();

        Assert.Equal(new PasswordCheck(true, true), await hasher.TryVerifyAsync("correct horse battery", ReferenceOlder, TestContext.Current.CancellationToken));
        Assert.Equal(new PasswordCheck(false, false), await hasher.TryVerifyAsync("wrong", ReferenceOlder, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_user_costs_exactly_one_run_against_the_dummy_hash()
    {
        using var hasher = new PasswordHasher();
        var before = hasher.Runs;

        var check = await hasher.TryVerifyAsync("any password at all", null, TestContext.Current.CancellationToken);

        Assert.Equal(new PasswordCheck(false, false), check);
        Assert.Equal(before + 1, hasher.Runs);
    }

    [Fact]
    public async Task The_dummy_check_never_hashes_the_offered_password_however_long()
    {
        using var hasher = new PasswordHasher();
        var before = hasher.Runs;

        var check = await hasher.TryVerifyAsync(new string('a', 1_000_000), null, TestContext.Current.CancellationToken);

        Assert.Equal(new PasswordCheck(false, false), check);
        Assert.Equal(before + 1, hasher.Runs);
    }

    [Fact]
    public async Task Refuses_to_hash_a_password_longer_than_the_policy_allows()
    {
        using var hasher = new PasswordHasher();
        var tooLong = new string('a', (PasswordPolicy.MaxLength * 4) + 1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => hasher.TryHashAsync(tooLong, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => hasher.TryVerifyAsync(tooLong, ReferenceCurrent, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$AAECAwQFBgcICQoLDA0ODw$quyusfeeuAGbpiw09i+j4VKJ+I/5h3mugd4HghmS/x8")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=4$AAECAwQFBgcICQoLDA0ODw$quyusfeeuAGbpiw09i+j4VKJ+I/5h3mugd4HghmS/x8")]
    [InlineData("$argon2id$v=19$m=99999999,t=2,p=1$AAECAwQFBgcICQoLDA0ODw$quyusfeeuAGbpiw09i+j4VKJ+I/5h3mugd4HghmS/x8")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$AAEC$quyusfeeuAGbpiw09i+j4VKJ+I/5h3mugd4HghmS/x8")]
    public async Task A_malformed_or_unsafe_hash_never_matches_and_still_costs_one_run(string stored)
    {
        using var hasher = new PasswordHasher();
        var before = hasher.Runs;

        Assert.Equal(new PasswordCheck(false, false), await hasher.TryVerifyAsync("correct horse battery", stored, TestContext.Current.CancellationToken));
        Assert.Equal(before + 1, hasher.Runs);
    }

    [Fact]
    public async Task Gives_up_when_every_slot_stays_busy()
    {
        using var hasher = new PasswordHasher(TimeSpan.FromMilliseconds(50));
        var gate = typeof(PasswordHasher).GetField("_gate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var semaphore = (SemaphoreSlim)gate.GetValue(hasher)!;
        for (var i = 0; i < PasswordHasher.MaxConcurrent; i++) await semaphore.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Null(await hasher.TryHashAsync("correct horse battery", TestContext.Current.CancellationToken));
        Assert.Null(await hasher.TryVerifyAsync("correct horse battery", ReferenceCurrent, TestContext.Current.CancellationToken));

        semaphore.Release(PasswordHasher.MaxConcurrent);
        Assert.NotNull(await hasher.TryHashAsync("correct horse battery", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Refresh_token_round_trips_and_hashes_only_the_secret()
    {
        var sessionId = Guid.CreateVersion7();
        var token = RefreshToken.New(sessionId);

        Assert.Matches(@"^orv_rt_[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}$", token.Value);
        Assert.True(RefreshToken.TryParse(token.Value, out var parsed));
        Assert.Equal(sessionId, parsed.SessionId);
        Assert.Equal(token.Secret, parsed.Secret);
        Assert.Equal(SHA256.HashData(token.Secret), parsed.SecretHash);
        Assert.NotEqual(token.Value, RefreshToken.New(sessionId).Value);
    }

    [Fact]
    public void Refresh_token_puts_the_session_id_bytes_in_uuid_order()
    {
        var sessionId = Guid.Parse("0198c0de-0000-7000-8000-000000000001");
        var token = RefreshToken.New(sessionId);

        Assert.StartsWith("orv_rt_AZjA3gAAcACAAAAAAAAAAQ.", token.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orv_rt_AZjA3gAAcACAAAAAAAAAAQ")]
    [InlineData("orv_sk_AZjA3gAAcACAAAAAAAAAAQ.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("orv_rt_AZjA3gAAcACAAAAAAAAAAQ-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("orv_rt_AZjA3gAAcACAAAAAAAAAAQ.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("orv_rt_AZjA3gAAcACAAAAAAAAAA!.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("orv_rt_AZjA3gAAcACAAAAAAAAAAQ.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void Refresh_token_refuses_anything_off_format(string? value) => Assert.False(RefreshToken.TryParse(value, out _));

    [Fact]
    public void Session_lifetime_caps_idle_expiry_at_the_absolute_one()
    {
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expires = SessionLifetime.ExpiresAt(created);

        Assert.Equal(created.AddDays(365), expires);
        Assert.Equal(created.AddDays(30), SessionLifetime.IdleExpiresAt(created, expires));
        Assert.Equal(expires, SessionLifetime.IdleExpiresAt(created.AddDays(350), expires));
        Assert.False(SessionLifetime.IsExpired(created.AddDays(30).AddSeconds(-1), created.AddDays(30), expires));
        Assert.True(SessionLifetime.IsExpired(created.AddDays(30), created.AddDays(30), expires));
        Assert.True(SessionLifetime.IsExpired(expires, expires, expires));
    }

    [Fact]
    public void Refresh_decision_follows_the_spec_table()
    {
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        byte[] current = [.. Enumerable.Repeat((byte)1, 32)];
        byte[] previous = [.. Enumerable.Repeat((byte)2, 32)];
        byte[] other = [.. Enumerable.Repeat((byte)3, 32)];
        var session = new RefreshState(false, now.AddDays(10), now.AddDays(100), current, previous, now.AddSeconds(-5));

        Assert.Equal(RefreshAction.Rotate, RefreshDecision.Decide(session, current, now));
        Assert.Equal(RefreshAction.Replay, RefreshDecision.Decide(session, previous, now));
        Assert.Equal(RefreshAction.Replay, RefreshDecision.Decide(session with { RotatedAt = now.AddSeconds(-10) }, previous, now));
        Assert.Equal(RefreshAction.Reuse, RefreshDecision.Decide(session with { RotatedAt = now.AddSeconds(-11) }, previous, now));
        Assert.Equal(RefreshAction.Refuse, RefreshDecision.Decide(session, other, now));
        Assert.Equal(RefreshAction.Refuse, RefreshDecision.Decide(session with { PreviousRefreshHash = null, RotatedAt = null }, previous, now));
        Assert.Equal(RefreshAction.Refuse, RefreshDecision.Decide(session with { Ended = true }, current, now));
        Assert.Equal(RefreshAction.Refuse, RefreshDecision.Decide(session with { IdleExpiresAt = now }, current, now));
        Assert.Equal(RefreshAction.Refuse, RefreshDecision.Decide(session with { ExpiresAt = now.AddSeconds(-1) }, current, now));
    }

    [Fact]
    public void Access_token_claims_carry_ids_only_and_live_900_seconds()
    {
        var user = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, 750, TimeSpan.Zero);

        var claims = AccessTokenClaims.For("https://orvano.example.com", "shop", user, session, now);

        Assert.Equal("https://orvano.example.com/v1/projects/shop", claims.Issuer);
        Assert.Equal("shop", claims.Audience);
        Assert.Equal(user, claims.Subject);
        Assert.Equal(session, claims.SessionId);
        Assert.Equal(now.AddMilliseconds(-750), claims.IssuedAt);
        Assert.Equal(TimeSpan.FromSeconds(900), claims.ExpiresAt - claims.IssuedAt);
    }

    [Fact]
    public void Client_info_cuts_to_its_limits_without_splitting_a_surrogate_pair()
    {
        var emojiAtTheEdge = new string('a', ClientInfo.MaxUserAgent - 1) + "\U0001F600";

        var info = ClientInfo.Of(emojiAtTheEdge, new string('s', ClientInfo.MaxSdk + 5), null);

        Assert.Equal(new string('a', ClientInfo.MaxUserAgent - 1), info.UserAgent);
        Assert.Equal(new string('s', ClientInfo.MaxSdk), info.Sdk);
        Assert.Null(ClientInfo.Of(" ", null, null).UserAgent);
    }
}
