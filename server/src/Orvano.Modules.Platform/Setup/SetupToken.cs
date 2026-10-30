using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Orvano.Modules.Platform.Setup;

/// <summary>The one time setup token's form, hash, and link (spec 0016 STP-1, STP-4, STP-6).</summary>
internal static class SetupToken
{
    public const string Prefix = "orv_setup_";

    /// <summary>Fixed, not a setting (spec 0016 Configuration required).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>STP-1: the prefix plus base64url of 32 random bytes.</summary>
    public static string Create() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>STP-1: SHA-256, which is right for a 256 bit random value; only this is stored.</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>STP-6: whether the submitted token, trimmed, hashes to <paramref name="storedHash"/>, in constant time.</summary>
    public static bool Matches(string submitted, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(submitted.Trim()), storedHash);

    /// <summary>
    /// STP-4: <c>&lt;ORVANO_PUBLIC_URL&gt;/console/setup#token=&lt;token&gt;</c>. The token rides in the fragment, so it
    /// never reaches an access log or a <c>Referer</c>. Without a public URL (a run outside an install) the link is the
    /// path alone.
    /// </summary>
    public static string Link(string? publicUrl, string token) => $"{publicUrl}/console/setup#token={token}";

    /// <summary>STP-4's expiry, in UTC, ISO 8601.</summary>
    public static string Format(DateTimeOffset expiresAt) =>
        expiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
