using System.Security.Cryptography;
using System.Text;

namespace Orvano.Core.Secrets;

/// <summary>A blob could not be decrypted: tampered, moved to another row, or sealed with a master key that is gone.</summary>
public sealed class SecretBoxException(string message) : CryptographicException(message);

/// <summary>An HMAC tag and the ID of the master key it was derived from (<see cref="SecretBox.Mac"/>).</summary>
/// <param name="KeyId">The master key's ID; store it beside the tag, so a key rotation never breaks a live tag.</param>
/// <param name="Tag">HMAC-SHA256, 32 bytes.</param>
public sealed record MacTag(string KeyId, byte[] Tag);

/// <summary>
/// Envelope encryption for secrets at rest (spec 0002). Each value gets a fresh random 32 byte data key and is sealed
/// with AES-256-GCM, bound by associated data to its row and column (<see cref="AssociatedData"/>), so a ciphertext
/// can't be moved to another row. The data key is then sealed with the active master key, bound to that key's ID.
/// The result is one versioned blob for a <c>bytea</c> column.
/// </summary>
/// <remarks>
/// Blob layout, version 1: <c>version (1) | idLength (1) | keyId | wrapNonce (12) | wrappedKey (32) | wrapTag (16) |
/// dataNonce (12) | dataTag (16) | ciphertext</c>.
/// </remarks>
public sealed class SecretBox(MasterKeys keys)
{
    private const byte Version1 = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int DataKeySize = 32;

    /// <summary>The associated data spec 0002 fixes for a stored secret: <c>&lt;table&gt;:&lt;rowId&gt;:&lt;column&gt;</c>.</summary>
    public static string AssociatedData(string table, string rowId, string column) => $"{table}:{rowId}:{column}";

    /// <summary>Seals <paramref name="plaintext"/> for the row and column <paramref name="associatedData"/> names.</summary>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, string associatedData)
    {
        var keyId = Encoding.ASCII.GetBytes(keys.ActiveId);
        var headerSize = 2 + keyId.Length;
        var blob = new byte[headerSize + NonceSize + DataKeySize + TagSize + NonceSize + TagSize + plaintext.Length];
        blob[0] = Version1;
        blob[1] = (byte)keyId.Length;
        keyId.CopyTo(blob, 2);

        var span = blob.AsSpan(headerSize);
        var wrapNonce = span[..NonceSize];
        var wrappedKey = span.Slice(NonceSize, DataKeySize);
        var wrapTag = span.Slice(NonceSize + DataKeySize, TagSize);
        var dataNonce = span.Slice(NonceSize + DataKeySize + TagSize, NonceSize);
        var dataTag = span.Slice((2 * NonceSize) + DataKeySize + TagSize, TagSize);
        var ciphertext = span[((2 * NonceSize) + DataKeySize + (2 * TagSize))..];

        Span<byte> dataKey = stackalloc byte[DataKeySize];
        try
        {
            RandomNumberGenerator.Fill(dataKey);
            RandomNumberGenerator.Fill(wrapNonce);
            RandomNumberGenerator.Fill(dataNonce);

            using (var data = new AesGcm(dataKey, TagSize))
            {
                data.Encrypt(dataNonce, plaintext, ciphertext, dataTag, Encoding.UTF8.GetBytes(associatedData));
            }

            using var wrap = new AesGcm(keys.Active, TagSize);
            wrap.Encrypt(wrapNonce, dataKey, wrappedKey, wrapTag, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }

        return blob;
    }

    /// <summary>
    /// An HMAC-SHA256 over <paramref name="data"/>, keyed by HKDF-SHA256 of the active master key with
    /// <paramref name="purpose"/> as the info (spec 0010, AC-1). The key never leaves the process, so a database dump
    /// alone can't check a guess against the tag.
    /// </summary>
    /// <param name="purpose">Separates keys by use, as in <c>orvano.auth.email-code</c>.</param>
    /// <param name="data">The bytes to authenticate.</param>
    public MacTag Mac(string purpose, ReadOnlySpan<byte> data) => new(keys.ActiveId, Tag(keys.Active, purpose, data));

    /// <summary>
    /// Whether <paramref name="tag"/> is the <see cref="Mac"/> of <paramref name="data"/> under the master key
    /// <paramref name="keyId"/>, compared in fixed time. A key that is no longer configured never matches.
    /// </summary>
    /// <param name="keyId">The <see cref="MacTag.KeyId"/> stored with the tag.</param>
    /// <param name="purpose">The purpose the tag was made with.</param>
    /// <param name="data">The bytes to check.</param>
    /// <param name="tag">The stored tag.</param>
    public bool VerifyMac(string keyId, string purpose, ReadOnlySpan<byte> data, ReadOnlySpan<byte> tag)
    {
        if (!keys.TryGet(keyId, out var masterKey)) return false;
        var expected = Tag(masterKey, purpose, data);
        return CryptographicOperations.FixedTimeEquals(expected, tag);
    }

    private static byte[] Tag(ReadOnlySpan<byte> masterKey, string purpose, ReadOnlySpan<byte> data)
    {
        Span<byte> macKey = stackalloc byte[DataKeySize];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, macKey, [], Encoding.UTF8.GetBytes(purpose));
            return HMACSHA256.HashData(macKey, data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(macKey);
        }
    }

    /// <summary>Opens a blob <see cref="Encrypt"/> sealed for the same <paramref name="associatedData"/>.</summary>
    /// <exception cref="SecretBoxException">The blob is malformed, tampered, bound to another row, or its master key is gone.</exception>
    public byte[] Decrypt(ReadOnlySpan<byte> blob, string associatedData)
    {
        if (blob.Length < 2 || blob[0] != Version1) throw new SecretBoxException("The secret blob has an unknown format.");
        var idLength = blob[1];
        var fixedSize = 2 + idLength + NonceSize + DataKeySize + TagSize + NonceSize + TagSize;
        if (idLength == 0 || blob.Length < fixedSize) throw new SecretBoxException("The secret blob is truncated.");

        var keyId = blob.Slice(2, idLength);
        if (!keys.TryGet(Encoding.ASCII.GetString(keyId), out var masterKey))
            throw new SecretBoxException("The secret was sealed with a master key that is no longer configured.");

        var span = blob[(2 + idLength)..];
        var wrapNonce = span[..NonceSize];
        var wrappedKey = span.Slice(NonceSize, DataKeySize);
        var wrapTag = span.Slice(NonceSize + DataKeySize, TagSize);
        var dataNonce = span.Slice(NonceSize + DataKeySize + TagSize, NonceSize);
        var dataTag = span.Slice((2 * NonceSize) + DataKeySize + TagSize, TagSize);
        var ciphertext = span[((2 * NonceSize) + DataKeySize + (2 * TagSize))..];

        Span<byte> dataKey = stackalloc byte[DataKeySize];
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using (var wrap = new AesGcm(masterKey, TagSize))
            {
                wrap.Decrypt(wrapNonce, wrappedKey, wrapTag, dataKey, keyId);
            }

            using var data = new AesGcm(dataKey, TagSize);
            data.Decrypt(dataNonce, ciphertext, dataTag, plaintext, Encoding.UTF8.GetBytes(associatedData));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new SecretBoxException("The secret blob failed authentication.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}
