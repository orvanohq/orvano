using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Orvano.SharedKernel.Access;

namespace Orvano.Modules.Platform.Keys;

/// <summary>A key's stored parts, and <see cref="Key"/>: the only copy of the key itself.</summary>
internal sealed record NewApiKeyToken(string Key, byte[] Hash, string Prefix, string Last4);

/// <summary>The form of an API key (spec 0006 KEY-1, KEY-2, spec 0017 AKY-2).</summary>
internal static class ApiKeyToken
{
    public const string PublishablePrefix = "orv_pk_";
    public const string SecretPrefix = "orv_sk_";

    private const int Length = 50;
    private const int DisplayPrefixLength = 15;

    public static NewApiKeyToken Create(KeyType type)
    {
        string key = (type == KeyType.Secret ? SecretPrefix : PublishablePrefix) + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return new NewApiKeyToken(key, Hash(key), key[..DisplayPrefixLength], key[^4..]);
    }

    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>AKY-9: a wrong shape needs no database read.</summary>
    public static bool IsWellFormed(string key) =>
        key.Length == Length
        && (key.StartsWith(PublishablePrefix, StringComparison.Ordinal) || key.StartsWith(SecretPrefix, StringComparison.Ordinal));
}
