using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>
/// Spec 0014 CTR-4: the written form of a contract document. Two space indent, <c>\n</c> line ends, a final newline;
/// <c>paths</c>, every <c>components</c> map, <c>responses</c>, and <c>x-orvano-error-codes</c> sorted by key; object
/// keys in the serializer's OpenAPI order, then <c>x-*</c> keys sorted; <c>required</c> and <c>x-orvano-*</c> lists
/// sorted, and <c>"null"</c> last in a <c>type</c> list (JSW-6). Every other array keeps the order the server emits.
/// </summary>
internal static class ContractJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Normalize(string json)
    {
        JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("The document is empty.");
        return Normalize(root, [])!.ToJsonString(WriteOptions) + "\n";
    }

    // The maps whose keys are names rather than OpenAPI fields, by where they sit: /paths, /webhooks, each
    // /components/<kind>, each operation's responses, and /x-orvano-error-codes.
    private static bool IsNameMap(IReadOnlyList<string> path) => path switch
    {
        ["paths" or "webhooks" or "x-orvano-error-codes"] => true,
        ["components", _] => true,
        ["paths" or "webhooks", _, _, "responses"] => true,
        _ => false,
    };

    private static JsonNode? Normalize(JsonNode? node, List<string> path) => node switch
    {
        JsonObject obj => NormalizeObject(obj, path),
        JsonArray array => NormalizeArray(array, path),
        _ => node?.DeepClone(),
    };

    private static JsonObject NormalizeObject(JsonObject obj, List<string> path)
    {
        IEnumerable<KeyValuePair<string, JsonNode?>> members = IsNameMap(path)
            ? obj.OrderBy(member => member.Key, StringComparer.Ordinal)
            : obj.Where(member => !IsExtension(member.Key))
                .Concat(obj.Where(member => IsExtension(member.Key)).OrderBy(member => member.Key, StringComparer.Ordinal));
        var result = new JsonObject();
        foreach ((string key, JsonNode? value) in members)
        {
            result[key] = Normalize(value, [.. path, key]);
        }

        return result;
    }

    private static JsonArray NormalizeArray(JsonArray array, List<string> path)
    {
        IEnumerable<JsonNode?> items = array.Select((item, index) => Normalize(item, [.. path, index.ToString(CultureInfo.InvariantCulture)]));
        string? key = path.Count > 0 ? path[^1] : null;
        if (key == "required" || key?.StartsWith("x-orvano-", StringComparison.Ordinal) == true)
        {
            items = items.OrderBy(item => item?.ToJsonString(), StringComparer.Ordinal);
        }
        else if (key == "type")
        {
            // JSW-6: a nullable type is written [<type>, "null"].
            items = items.OrderBy(item => item?.GetValue<string>() == "null");
        }

        return [.. items];
    }

    private static bool IsExtension(string key) => key.StartsWith("x-", StringComparison.Ordinal);

    /// <summary>The JSON pointer of the first place <paramref name="actual"/> differs from <paramref name="expected"/>, or null.</summary>
    public static string? FirstDifference(JsonNode? expected, JsonNode? actual, string pointer = "")
    {
        switch (expected, actual)
        {
            case (JsonObject e, JsonObject a):
                foreach (string key in e.Select(member => member.Key).Union(a.Select(member => member.Key)))
                {
                    string child = pointer + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    if (!e.ContainsKey(key) || !a.ContainsKey(key))
                    {
                        return child;
                    }

                    if (FirstDifference(e[key], a[key], child) is { } difference)
                    {
                        return difference;
                    }
                }

                return e.Select(m => m.Key).SequenceEqual(a.Select(m => m.Key)) ? null : pointer + " (key order)";
            case (JsonArray e, JsonArray a):
                for (int i = 0; i < Math.Max(e.Count, a.Count); i++)
                {
                    if (i >= e.Count || i >= a.Count)
                    {
                        return $"{pointer}/{i}";
                    }

                    if (FirstDifference(e[i], a[i], $"{pointer}/{i}") is { } difference)
                    {
                        return difference;
                    }
                }

                return null;
            default:
                return JsonNode.DeepEquals(expected, actual) ? null : (pointer.Length == 0 ? "/" : pointer);
        }
    }
}
