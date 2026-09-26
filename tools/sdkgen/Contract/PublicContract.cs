using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orvano.SdkGen.Contract;

/// <summary>
/// Builds <c>contract/dist/openapi.public.json</c> (AC-15, AC-17, AC-18): the compiled contract
/// minus <c>console</c> and <c>x-orvano-test</c> operations, every schema marked
/// <c>x-orvano-test</c>, and every schema that only removed operations reach. The docs reference
/// and the public breaking change check read this file, never the full contract.
/// </summary>
internal static class PublicContract
{
    private const string SchemaRefPrefix = "#/components/schemas/";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        // Backticks, quotes, and angle brackets in docs stay readable, as TypeSpec writes them.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(string openApiJson)
    {
        var doc = JsonNode.Parse(openApiJson)?.AsObject() ?? throw new InvalidOperationException("openapi.json is not a JSON object");
        var schemas = doc["components"]?["schemas"]?.AsObject() ?? [];
        var kept = new List<JsonNode>();
        var removed = new List<JsonNode>();

        if (doc["paths"] is JsonObject paths)
        {
            foreach (var (path, item) in paths.ToList())
            {
                if (item is not JsonObject operations) continue;
                foreach (var (method, op) in operations.ToList())
                {
                    if (op is null) continue;
                    if (IsPublic(op))
                    {
                        kept.Add(op);
                        continue;
                    }

                    removed.Add(op);
                    operations.Remove(method);
                }

                if (operations.Count == 0) paths.Remove(path);
            }
        }

        // A schema survives when a kept operation reaches it, or when no removed operation does
        // (a catalog like ErrorCode, or a public event payload). Test schemas never survive.
        var removedReach = Reach(removed, schemas);
        var standalone = schemas.Where(s => s.Value is not null && !removedReach.Contains(s.Key) && !IsTest(s.Value)).ToList();
        var keep = Reach(kept.Concat(standalone.Select(s => s.Value!)), schemas);
        keep.UnionWith(standalone.Select(s => s.Key));
        foreach (var name in schemas.Select(s => s.Key).ToList())
            if (!keep.Contains(name) || IsTest(schemas[name])) schemas.Remove(name);

        if (doc["tags"] is JsonArray tags)
        {
            var used = kept.SelectMany(op => op["tags"]?.AsArray() ?? []).Select(t => t?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            foreach (var tag in tags.ToList())
                if (!used.Contains(tag?["name"]?.GetValue<string>())) tags.Remove(tag);
        }

        return doc.ToJsonString(Options) + "\n";
    }

    private static bool IsPublic(JsonNode op) =>
        op["x-orvano-audience"]?.GetValue<string>() != "console" && !IsTest(op);

    private static bool IsTest(JsonNode? node) =>
        node?["x-orvano-test"] is JsonValue v && v.TryGetValue<bool>(out var test) && test;

    /// <summary>Every schema name reachable through <c>$ref</c> from <paramref name="roots"/>.</summary>
    private static HashSet<string> Reach(IEnumerable<JsonNode> roots, JsonObject schemas)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<JsonNode>(roots);
        while (pending.TryPop(out var node))
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "$ref" && value is JsonValue v && v.TryGetValue<string>(out var target)
                            && target.StartsWith(SchemaRefPrefix, StringComparison.Ordinal)
                            && reached.Add(target[SchemaRefPrefix.Length..])
                            && schemas[target[SchemaRefPrefix.Length..]] is { } schema)
                        {
                            pending.Push(schema);
                        }
                        else if (value is not null)
                        {
                            pending.Push(value);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                        if (item is not null) pending.Push(item);
                    break;
            }
        }

        return reached;
    }
}
