using System.Buffers.Text;
using Microsoft.Extensions.Configuration;

namespace Orvano.Core.Secrets;

/// <summary>
/// The master keys from <c>ORVANO_MASTER_KEYS</c> (spec 0002, secrets at rest): one or more <c>id:base64</c> pairs
/// separated by commas, each key exactly 32 bytes, IDs unique. The first entry is active; the others only decrypt.
/// The key bytes never appear in a message, log, or exception.
/// </summary>
public sealed class MasterKeys
{
    /// <summary>The setting's name.</summary>
    public const string Setting = "ORVANO_MASTER_KEYS";

    /// <summary>The size of every master key, in bytes.</summary>
    public const int KeySize = 32;

    /// <summary>The longest key ID; the blob stores its length in one byte.</summary>
    public const int MaxIdLength = 64;

    private readonly Dictionary<string, byte[]> _keys;

    private MasterKeys(string activeId, Dictionary<string, byte[]> keys)
    {
        ActiveId = activeId;
        _keys = keys;
    }

    /// <summary>The ID of the key new secrets are encrypted with.</summary>
    public string ActiveId { get; }

    internal ReadOnlySpan<byte> Active => _keys[ActiveId];

    internal bool TryGet(string id, out byte[] key) => _keys.TryGetValue(id, out key!);

    /// <summary>Reads and checks <c>ORVANO_MASTER_KEYS</c>.</summary>
    /// <exception cref="OrvanoConfigException">The setting is unset or malformed.</exception>
    public static MasterKeys FromConfig(IConfiguration config) => Parse(OrvanoConfig.Required(config, Setting));

    /// <summary>Parses a setting value.</summary>
    /// <exception cref="OrvanoConfigException">An entry is malformed, a key is not 32 bytes, or two IDs repeat.</exception>
    public static MasterKeys Parse(string value)
    {
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? activeId = null;
        var position = 0;
        foreach (var raw in value.Split(','))
        {
            position++;
            var entry = raw.Trim();
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) throw Invalid($"entry {position} is not id:base64");

            var id = entry[..colon];
            if (id.Length > MaxIdLength || !id.All(IsIdChar))
                throw Invalid($"entry {position} has an ID that is not 1 to {MaxIdLength} of [A-Za-z0-9._-]");
            if (keys.ContainsKey(id)) throw Invalid($"the ID '{id}' appears twice");

            var key = DecodeKey(entry.AsSpan(colon + 1));
            if (key is null) throw Invalid($"the key '{id}' is not the base64 of exactly {KeySize} bytes");

            keys.Add(id, key);
            activeId ??= id;
        }

        return new MasterKeys(activeId!, keys);
    }

    private static byte[]? DecodeKey(ReadOnlySpan<char> text)
    {
        Span<byte> buffer = stackalloc byte[48];
        if (!Convert.TryFromBase64Chars(text, buffer, out var written) || written != KeySize)
        {
            // Accept the URL safe alphabet too, since some generators emit it.
            if (!Base64Url.IsValid(text) || Base64Url.GetMaxDecodedLength(text.Length) > buffer.Length) return null;
            written = Base64Url.DecodeFromChars(text.TrimEnd('='), buffer);
            if (written != KeySize) return null;
        }

        return buffer[..KeySize].ToArray();
    }

    private static bool IsIdChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';

    private static OrvanoConfigException Invalid(string why) =>
        new($"{Setting} is invalid: {why}. Each entry is id:base64 of 32 random bytes, for example k20260101:<openssl rand -base64 32>.");
}
