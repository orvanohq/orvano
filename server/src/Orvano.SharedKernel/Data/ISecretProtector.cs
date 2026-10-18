namespace Orvano.SharedKernel.Data;

/// <summary>
/// Encrypts and decrypts the secrets stored on a row (spec 0011 ENC-1; envelope from spec 0006 SCR-1). The
/// authenticated extra data is the project id (none for platform scope), the row id, and the purpose, so a value copied
/// to another row or purpose does not decrypt. Implemented by <c>Orvano.Infrastructure</c> (ENC-3).
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Encrypts with a fresh random nonce on every call, under the current data key, whose id goes into the envelope
    /// so <see cref="Unprotect"/> can find the key after a rotation.
    /// </summary>
    EncryptedValue Protect(ReadOnlySpan<byte> plaintext, Guid? projectId, Guid rowId, string purpose);

    /// <summary>
    /// Decrypts with the data key named in the envelope. Throws when the project, row, or purpose differ from the ones
    /// used to protect, or when the bytes were changed; it never returns empty or partial plaintext instead.
    /// </summary>
    byte[] Unprotect(EncryptedValue value, Guid? projectId, Guid rowId, string purpose);
}
