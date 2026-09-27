using System.Security.Cryptography;
using System.Text;

namespace Orvano.Core.Secrets;

/// <summary>A blob could not be decrypted: tampered, moved to another row, or sealed with a master key that is gone.</summary>
public sealed class SecretBoxException(string message) : CryptographicException(message);

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
