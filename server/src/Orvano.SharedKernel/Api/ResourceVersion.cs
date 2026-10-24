using System.Buffers.Binary;
using System.Buffers.Text;

using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// A resource's version on the wire (spec 0014 CNC-1, CNC-2): base64url, without padding, of the entity's version
/// number (<see cref="Data.IVersioned"/>) as 8 big endian bytes. It is opaque to clients, and no engine detail such as
/// <c>xmin</c> shows. A single resource answer sends it as a strong <c>ETag</c>, and its body's read only
/// <c>version</c> holds the same value without quotes.
/// </summary>
public static class ResourceVersion
{
    /// <summary>The <c>version</c> field's value for <paramref name="version"/>.</summary>
    public static string Format(ulong version)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, version);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>The strong <c>ETag</c> for <paramref name="version"/>: the <see cref="Format"/> value in quotes.</summary>
    public static string ETag(ulong version) => $"\"{Format(version)}\"";

    /// <summary>Sets the response's <c>ETag</c> to <paramref name="version"/> (CNC-1).</summary>
    public static void SetETag(HttpResponse response, ulong version)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers.ETag = ETag(version);
    }
}
