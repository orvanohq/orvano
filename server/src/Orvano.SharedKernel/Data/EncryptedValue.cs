using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Orvano.SharedKernel.Data;

/// <summary>
/// The stored envelope of a secret (spec 0006 SCR-1: format version, data key id, nonce, tag, ciphertext), held as a
/// <c>bytea</c> column on the row that uses it (spec 0011 ENC-1). Only <see cref="ISecretProtector"/> makes or opens
/// one; the EF mapping just passes the bytes through, because a converter cannot see the row. JSON gets the same
/// marker as <see cref="ToString"/>, never the bytes (spec 0006 LOG-2, LOG-3).
/// </summary>
[JsonConverter(typeof(RedactingJsonConverter))]
public readonly record struct EncryptedValue(ReadOnlyMemory<byte> Bytes)
{
    public bool Equals(EncryptedValue other) => Bytes.Span.SequenceEqual(other.Bytes.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes.Span);
        return hash.ToHashCode();
    }

    /// <summary>Never prints the bytes.</summary>
    public override string ToString() => $"EncryptedValue({Bytes.Length} bytes)";

    /// <summary>Writes the <see cref="ToString"/> marker, and refuses to read: an envelope never travels as JSON.</summary>
    internal sealed class RedactingJsonConverter : JsonConverter<EncryptedValue>
    {
        public override EncryptedValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("An EncryptedValue is never read from JSON.");

        public override void Write(Utf8JsonWriter writer, EncryptedValue value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>Refuses an empty envelope on save: it can never decrypt, so it is a secret that was never set.</summary>
    internal sealed class Converter() : ValueConverter<EncryptedValue, byte[]>(
        value => ToStored(value),
        bytes => new EncryptedValue(bytes))
    {
        private static byte[] ToStored(EncryptedValue value) =>
            value.Bytes.IsEmpty
                ? throw new InvalidOperationException(
                    "An EncryptedValue is empty, so it was never set by ISecretProtector. Protect the secret, or make the property nullable.")
                : value.Bytes.ToArray();
    }

    internal sealed class Comparer() : ValueComparer<EncryptedValue>(
        (left, right) => left.Equals(right),
        value => value.GetHashCode(),
        value => new EncryptedValue(value.Bytes.ToArray()));
}
