using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;

namespace Orvano.SharedKernel.Api;

/// <summary>
/// Builds and reads list cursors (spec 0014 PAG-5): base64url, without padding, of <c>{ v: 1, k: [keys], q: hash }</c>.
/// <c>k</c> holds the last row's sort keys; <c>q</c> ties the cursor to the filter and sort it was issued for. Cursors
/// are not signed: the access filter applies on every page, so a forged one reaches only rows the caller may see.
/// </summary>
public static class CursorCodec
{
    private const int Version = 1;

    private static readonly Error Invalid = Error.Validation(
        ErrorCodes.InvalidCursor, "The cursor is not valid for this request. Start again from the first page.");

    /// <summary>Writes a cursor for the row whose sort keys are <paramref name="keys"/>.</summary>
    /// <param name="keys">The row's sort keys in the list's order, ending with its ID, written per the JSW rules.</param>
    /// <param name="query">The request's <see cref="Query(HttpRequest)"/> hash.</param>
    public static string Encode(JsonArray keys, string query)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrEmpty(query);
        var body = new JsonObject { ["v"] = Version, ["k"] = keys.DeepClone(), ["q"] = query };
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    /// <summary>
    /// Reads a cursor's sort keys, which must be <paramref name="shape"/>: as many keys as it has kinds, each of its
    /// kind. A cursor that does not parse, whose hash is not <paramref name="query"/> (another filter or sort), or whose
    /// keys have another shape (a forged one: the hash is not secret) fails with <c>400 invalid_cursor</c>.
    /// </summary>
    /// <param name="cursor">The <c>cursor</c> query parameter.</param>
    /// <param name="query">The request's <see cref="Query(HttpRequest)"/> hash.</param>
    /// <param name="shape">The kinds of the list's sort keys, in its order, ending with the ID's.</param>
    public static Result<CursorKeys> Decode(string cursor, string query, params ReadOnlySpan<CursorKey> shape)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentException.ThrowIfNullOrEmpty(query);
        if (shape.IsEmpty)
        {
            throw new ArgumentException("A list sorts by at least its ID.", nameof(shape));
        }

        if (cursor.Length is 0 or > PageQuery.MaxCursorLength || !Base64Url.IsValid(cursor))
        {
            return Invalid;
        }

        try
        {
            if (JsonNode.Parse(Base64Url.DecodeFromChars(cursor)) is JsonObject body
                && body["v"] is JsonValue version && version.TryGetValue(out int number) && number == Version
                && body["q"] is JsonValue hash && hash.TryGetValue(out string? value) && value == query
                && body["k"] is JsonArray keys && keys.Count == shape.Length)
            {
                object[] values = new object[shape.Length];
                for (int index = 0; index < shape.Length; index++)
                {
                    if (!TryRead(keys[index], shape[index], out object? key))
                    {
                        return Invalid;
                    }

                    values[index] = key;
                }

                return new CursorKeys(values);
            }
        }
        catch (JsonException)
        {
            // falls through: not JSON
        }
        catch (FormatException)
        {
            // falls through: not base64url
        }

        return Invalid;
    }

    // Each kind is a JSON string written per the JSW rules: uuids as strings, timestamps per JSW-3, int64 per JSW-5.
    private static bool TryRead(JsonNode? node, CursorKey kind, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? key)
    {
        key = null;
        if (node is not JsonValue element || !element.TryGetValue(out string? text))
        {
            return false;
        }

        switch (kind)
        {
            case CursorKey.Text:
                key = text;
                return true;
            case CursorKey.Uuid when Guid.TryParseExact(text, "D", out Guid id):
                key = id;
                return true;
            case CursorKey.Timestamp when UtcDateTimeOffsetConverter.TryParse(text, out DateTimeOffset at):
                key = at;
                return true;
            case CursorKey.Int64Text when long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long integer):
                key = integer;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The <c>q</c> hash of a request: the first 16 hex characters of the SHA-256 of the canonical JSON (keys sorted,
    /// no whitespace) of its route values and its query parameters other than <c>limit</c> and <c>cursor</c>.
    /// </summary>
    public static string Query(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var route = new JsonObject();
        foreach ((string key, object? routeValue) in request.RouteValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            route[key] = Convert.ToString(routeValue, System.Globalization.CultureInfo.InvariantCulture);
        }

        var parameters = new JsonObject();
        foreach ((string key, Microsoft.Extensions.Primitives.StringValues values) in request.Query.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (key is "limit" or "cursor")
            {
                continue;
            }

            parameters[key] = new JsonArray([.. values.Select(text => (JsonNode?)JsonValue.Create(text))]);
        }

        string canonical = new JsonObject { ["query"] = parameters, ["route"] = route }.ToJsonString();
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest, 0, 8);
    }
}

/// <summary>The kind of one sort key in a cursor (spec 0014 PAG-5), each written as a JSON string.</summary>
public enum CursorKey
{
    /// <summary>A text key, read as it is.</summary>
    Text,

    /// <summary>A uuid, such as a row ID.</summary>
    Uuid,

    /// <summary>An RFC 3339 timestamp (JSW-3), read as UTC.</summary>
    Timestamp,

    /// <summary>A 64 bit integer, written as a string (JSW-5).</summary>
    Int64Text,
}

/// <summary>
/// The sort keys of a cursor that <see cref="CursorCodec.Decode"/> read, each already of the kind its shape named. Ask
/// for key <c>i</c> as that kind; asking for another kind is a bug in the list, not in the cursor, and throws.
/// </summary>
public sealed class CursorKeys
{
    private readonly object[] _values;

    internal CursorKeys(object[] values) => _values = values;

    /// <summary>How many keys the cursor has: as many as its shape named.</summary>
    public int Count => _values.Length;

    public string GetText(int index) => Get<string>(index);

    public Guid GetUuid(int index) => Get<Guid>(index);

    public DateTimeOffset GetTimestamp(int index) => Get<DateTimeOffset>(index);

    public long GetInt64(int index) => Get<long>(index);

    private T Get<T>(int index) => _values[index] is T value
        ? value
        : throw new InvalidOperationException($"Cursor key {index} is a {_values[index].GetType().Name}, not a {typeof(T).Name}: check the shape passed to Decode.");
}
