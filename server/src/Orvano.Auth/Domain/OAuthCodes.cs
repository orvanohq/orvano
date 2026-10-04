using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Auth.Domain;

/// <summary>
/// PKCE with S256 (RFC 7636), both ways (spec 0012, AC-4, AC-7): the challenge an SDK sends is exactly 43 base64url
/// characters, the verifier it proves with is 43 to 128 characters of <c>[A-Za-z0-9-._~]</c>, and the challenge is
/// base64url(SHA-256(ASCII verifier)).
/// </summary>
internal static class Pkce
{
    public const int ChallengeLength = 43;
    public const int MinVerifier = 43;
    public const int MaxVerifier = 128;

    /// <summary>Whether <paramref name="challenge"/> has the shape of an S256 challenge.</summary>
    public static bool IsChallenge(string? challenge) =>
        challenge is { Length: ChallengeLength } && Base64Url.IsValid(challenge) && challenge.All(IsBase64UrlChar);

    /// <summary>Whether <paramref name="verifier"/> is a verifier RFC 7636 allows.</summary>
    public static bool IsVerifier(string? verifier) =>
        verifier is { Length: >= MinVerifier and <= MaxVerifier } && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>The S256 challenge of a verifier.</summary>
    public static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Whether the verifier proves the challenge, compared in fixed time.</summary>
    public static bool Proves(string verifier, string challenge) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Challenge(verifier)), Encoding.ASCII.GetBytes(challenge));

    private static bool IsBase64UrlChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}

/// <summary>
/// The random values of a flow (AC-4): the <c>state</c>, the <c>nonce</c>, and the provider PKCE verifier, each 32
/// bytes from <see cref="RandomNumberGenerator"/> as base64url (43 characters). The database keeps state and nonce
/// only as SHA-256, and the verifier only sealed.
/// </summary>
internal static class FlowSecret
{
    public const int Size = 32;

    /// <summary>A new value. Never log it.</summary>
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Size));

    /// <summary>SHA-256 of the value's UTF-8 bytes: how state and nonce are stored and compared.</summary>
    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

/// <summary>
/// The one time code the callback hands the app (AC-6): <c>orv_oc_</c> plus 32 random bytes as base64url (43
/// characters). It works for 2 minutes, once, and only with the verifier of the SDK that started the flow. The
/// database keeps only its SHA-256.
/// </summary>
internal sealed class HandoffCode
{
    public const string Prefix = "orv_oc_";
    private const int SecretChars = 43;

    private HandoffCode(string value) => Value = value;

    /// <summary>The wire form. Never log it.</summary>
    public string Value { get; }

    /// <summary>SHA-256 of the whole code, the only form the database keeps.</summary>
    public byte[] Hash => SHA256.HashData(Encoding.UTF8.GetBytes(Value));

    /// <summary>A new code with a fresh secret.</summary>
    public static HandoffCode New() => new(Prefix + FlowSecret.New());

    /// <summary>Reads a code; false for anything not exactly <c>orv_oc_</c> plus 43 base64url characters.</summary>
    public static bool TryParse(string? value, out HandoffCode code)
    {
        code = null!;
        if (value is null || value.Length != Prefix.Length + SecretChars || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var secret = value.AsSpan(Prefix.Length);
        foreach (var c in secret)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) return false;
        }

        code = new HandoffCode(value);
        return true;
    }
}

/// <summary>
/// The native sign in nonce (AC-9, AC-20): the app keeps the raw value and gives the provider the lowercase hex
/// SHA-256 of it, so the ID token's <c>nonce</c> claim is that hash.
/// </summary>
internal static class NativeNonce
{
    public const int MinLength = 16;
    public const int MaxLength = 128;

    public static bool IsValid(string? nonce) => nonce is { Length: >= MinLength and <= MaxLength };

    /// <summary>The value the token must carry: lowercase hex SHA-256 of the raw nonce's UTF-8 bytes.</summary>
    public static string Hashed(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
