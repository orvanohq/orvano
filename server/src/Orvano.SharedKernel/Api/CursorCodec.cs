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
    /// Reads a cursor's sort keys. A cursor that does not parse, or whose hash is not <paramref name="query"/> (another
    /// filter or sort), fails with <c>400 invalid_cursor</c>.
    /// </summary>
    public static Result<JsonArray> Decode(string cursor, string query)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentException.ThrowIfNullOrEmpty(query);
        if (cursor.Length is 0 or > PageQuery.MaxCursorLength || !Base64Url.IsValid(cursor))
        {
            return Invalid;
        }

        try
        {
            if (JsonNode.Parse(Base64Url.DecodeFromChars(cursor)) is JsonObject body
                && body["v"] is JsonValue version && version.TryGetValue(out int number) && number == Version
                && body["q"] is JsonValue hash && hash.TryGetValue(out string? value) && value == query
                && body["k"] is JsonArray keys && keys.Count > 0)
            {
                return (JsonArray)keys.DeepClone();
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
