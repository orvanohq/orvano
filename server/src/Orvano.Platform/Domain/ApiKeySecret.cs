using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Platform.Domain;

/// <summary>
/// An API key secret (AC-11): <c>orv_sk_</c> plus 43 base64url characters (32 random bytes). Only its
/// SHA-256 hash and a 12 character prefix are ever stored; the secret itself leaves the server once.
/// </summary>
internal sealed class ApiKeySecret
{
    public const string Marker = "orv_sk_";
    public const int PrefixLength = 12;
    public const int Length = 50;

    private ApiKeySecret(string value) => Value = value;

    /// <summary>The full secret. Never log, store, or put it in an event.</summary>
    public string Value { get; }

    /// <summary>The first 12 characters, <c>orv_sk_</c> plus 5, shown in lists so a key can be recognized.</summary>
    public string Prefix => Value[..PrefixLength];

    /// <summary>SHA-256 of the secret's UTF-8 bytes, the lookup key in <c>platform_api_keys.secret_hash</c>.</summary>
    public byte[] Hash => HashOf(Value);

    public static ApiKeySecret New() => new(Marker + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)));

    /// <summary>A secret as presented, when it has the right shape; anything else can never match a key.</summary>
    public static bool TryParse(string? presented, out ApiKeySecret secret)
    {
        secret = null!;
        if (presented is not { Length: Length } || !presented.StartsWith(Marker, StringComparison.Ordinal)) return false;
        foreach (var c in presented.AsSpan(Marker.Length))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) return false;
        }

        secret = new ApiKeySecret(presented);
        return true;
    }

    public static byte[] HashOf(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>Never prints the secret.</summary>
    public override string ToString() => Prefix + "…";
}

/// <summary>How often <c>last_used_at</c> may move (AC-12): at most once per 60 seconds per key.</summary>
internal static class ApiKeyUsage
{
    public static readonly TimeSpan Resolution = TimeSpan.FromSeconds(60);
}
