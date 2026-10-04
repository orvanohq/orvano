using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Auth.Domain;

/// <summary>What an email token is for (spec 0010, the <c>kind</c> column).</summary>
internal enum EmailTokenKind
{
    Verification,
    Recovery,
    MagicLink,
    EmailCode,
    EmailChange,
}

/// <summary>The wire names, lifetimes (AC-2), and link rules of each <see cref="EmailTokenKind"/>.</summary>
internal static class EmailTokenKinds
{
    public const string Verification = "verification";
    public const string Recovery = "recovery";
    public const string MagicLink = "magic_link";
    public const string EmailCode = "email_code";
    public const string EmailChange = "email_change";

    /// <summary>The <c>kind</c> column value, also the link's <c>orvano_type</c> for the four link kinds.</summary>
    public static string Wire(EmailTokenKind kind) => kind switch
    {
        EmailTokenKind.Verification => Verification,
        EmailTokenKind.Recovery => Recovery,
        EmailTokenKind.MagicLink => MagicLink,
        EmailTokenKind.EmailCode => EmailCode,
        EmailTokenKind.EmailChange => EmailChange,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>How long a token of this kind works; also the email's <c>expires_in_minutes</c>.</summary>
    public static TimeSpan Lifetime(EmailTokenKind kind) => kind switch
    {
        EmailTokenKind.Verification => AuthTimings.VerificationToken,
        EmailTokenKind.Recovery => AuthTimings.RecoveryToken,
        EmailTokenKind.MagicLink => AuthTimings.MagicLinkToken,
        EmailTokenKind.EmailCode => AuthTimings.EmailCodeToken,
        EmailTokenKind.EmailChange => AuthTimings.EmailChangeToken,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Whether redeeming the token signs someone in. Such links must open over https (or http on localhost), never a
    /// custom scheme another app on the device could register (AC-6).
    /// </summary>
    public static bool GrantsSession(EmailTokenKind kind) => kind is EmailTokenKind.Recovery or EmailTokenKind.MagicLink;
}

/// <summary>
/// An email link token (AC-1): <c>orv_el_</c> plus 32 random bytes as base64url (43 characters). The database keeps
/// only the SHA-256 of the whole token's UTF-8 bytes.
/// </summary>
internal sealed class LinkToken
{
    public const string Prefix = "orv_el_";
    public const int SecretSize = 32;
    private const int SecretChars = 43;

    private LinkToken(string value) => Value = value;

    /// <summary>The wire form. Never log it.</summary>
    public string Value { get; }

    /// <summary>SHA-256 of the token, the only form the database keeps.</summary>
    public byte[] Hash => SHA256.HashData(Encoding.UTF8.GetBytes(Value));

    /// <summary>A new token with a fresh secret from <see cref="RandomNumberGenerator"/>.</summary>
    public static LinkToken New() => new(Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretSize)));

    /// <summary>Reads a token; false for anything not exactly in the format.</summary>
    public static bool TryParse(string? value, out LinkToken token)
    {
        token = null!;
        if (value is null || value.Length != Prefix.Length + SecretChars || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var secret = value.AsSpan(Prefix.Length);
        Span<byte> bytes = stackalloc byte[SecretSize];
        if (!Base64Url.IsValid(secret) || Base64Url.DecodeFromChars(secret, bytes) != SecretSize) return false;
        // Only the canonical encoding: two spellings of one secret would hash differently.
        if (!string.Equals(Base64Url.EncodeToString(bytes), secret.ToString(), StringComparison.Ordinal)) return false;

        token = new LinkToken(value);
        return true;
    }
}

/// <summary>
/// A 6 digit email code (AC-1, AC-5): drawn uniformly, stored only as an HMAC over <c>&lt;row id&gt;:&lt;code&gt;</c>
/// keyed from the master key, and deleted after the 5th wrong guess.
/// </summary>
internal static class EmailCode
{
    public const int Length = 6;
    public const int MaxAttempts = 5;

    /// <summary>The <c>SecretBox.Mac</c> purpose, which is also the HKDF info.</summary>
    public const string MacPurpose = "orvano.auth.email-code";

    /// <summary>A new code: <c>RandomNumberGenerator.GetInt32(0, 1_000_000)</c>, zero padded.</summary>
    public static string New() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Exactly 6 ASCII digits.</summary>
    public static bool IsWellFormed(string? code) => code is { Length: Length } && code.All(char.IsAsciiDigit);

    /// <summary>The bytes the HMAC covers, which bind the code to its row.</summary>
    public static byte[] MacInput(Guid rowId, string code) => Encoding.UTF8.GetBytes($"{rowId}:{code}");
}

/// <summary>Whether a redirect URL may carry a link that signs in, or only one that verifies.</summary>
internal enum RedirectScheme
{
    /// <summary><c>https</c>, or <c>http</c> (which Platform allows only on <c>localhost</c> and <c>127.0.0.1</c>).</summary>
    Web,

    /// <summary>A custom scheme, which must name a registered app (<c>com.acme.app://auth</c>).</summary>
    App,
}

/// <summary>A redirect URL whose shape passed <see cref="RedirectUrlRule"/>; the project's platforms decide the rest.</summary>
internal sealed record RedirectUrl(Uri Url, RedirectScheme Scheme);

/// <summary>
/// The shape of a <c>redirectUrl</c> (AC-6), checked before the project's platforms: at most 2,048 characters with no
/// backslash, whitespace, or control character; an absolute URL with no user info; a host that is not empty and does
/// not end in a dot (compared as its IDN form, lowercased). Links that sign in (<see cref="EmailTokenKinds.GrantsSession"/>)
/// must be http or https.
/// </summary>
internal static class RedirectUrlRule
{
    public const int MaxLength = 2048;

    public static bool TryCheck(string? raw, EmailTokenKind kind, out RedirectUrl redirect) =>
        TryCheck(raw, allowAppScheme: !EmailTokenKinds.GrantsSession(kind), out redirect);

    /// <summary>
    /// The same shape rule with the scheme choice made by the caller: the OAuth flows (spec 0012, AC-4) allow a custom
    /// scheme, because PKCE binds their code to the app that started the flow.
    /// </summary>
    public static bool TryCheck(string? raw, bool allowAppScheme, out RedirectUrl redirect)
    {
        redirect = null!;
        if (raw is null || raw.Length is 0 or > MaxLength) return false;
        if (raw.Any(c => c == '\\' || char.IsWhiteSpace(c) || char.IsControl(c))) return false;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.UserInfo.Length > 0) return false;

        string host;
        try
        {
            host = url.IdnHost.ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (host.Length == 0 || host.EndsWith('.')) return false;

        var web = url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp;
        if (!web && !allowAppScheme) return false;
        redirect = new RedirectUrl(url, web ? RedirectScheme.Web : RedirectScheme.App);
        return true;
    }
}

/// <summary>
/// The link an email carries (AC-6): the redirect URL's normalized form with <c>orvano_type</c> and
/// <c>orvano_token</c> set, replacing any parameters of those names and keeping the rest of the query and the fragment.
/// </summary>
internal static class LinkUrl
{
    public const string TypeParameter = "orvano_type";
    public const string TokenParameter = "orvano_token";

    public static string Build(Uri redirect, EmailTokenKind kind, LinkToken token) =>
        With(redirect, [TypeParameter, TokenParameter], [(TypeParameter, EmailTokenKinds.Wire(kind)), (TokenParameter, token.Value)]);

    /// <summary>
    /// The redirect URL's normalized form with every parameter named in <paramref name="strip"/> removed and
    /// <paramref name="added"/> appended in order, keeping the rest of the query and the fragment. The added values
    /// are escaped; the names are plain ASCII.
    /// </summary>
    public static string With(Uri redirect, IReadOnlyCollection<string> strip, IEnumerable<(string Name, string Value)> added)
    {
        var kept = redirect.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !strip.Contains(Name(pair)));
        var builder = new UriBuilder(redirect)
        {
            Query = string.Join('&', kept.Concat(added.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"))),
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string Name(string pair)
    {
        var name = pair.Split('=', 2)[0];
        try
        {
            return Uri.UnescapeDataString(name.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return name;
        }
    }
}

/// <summary>
/// The reauthentication rule (AC-17, AC-19): a user without a password proves who they are by a session created at
/// most 10 minutes ago, compared on the database clock.
/// </summary>
internal static class SessionFreshness
{
    public static bool IsFresh(DateTimeOffset sessionCreatedAt, DateTimeOffset now) => now - sessionCreatedAt <= AuthTimings.Reauthentication;
}
