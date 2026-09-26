using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Orvano.Scenarios.Generated;

namespace Orvano.Scenarios;

internal sealed class ScenarioFailure(string message) : Exception(message);

internal sealed class ScenarioSkipped(string message) : Exception(message);

internal sealed record ScenarioResult(string Name, string Outcome, string? Reason = null)
{
    public override string ToString() => $"{Outcome,-7} {Name}{(Reason is null ? "" : ": " + Reason)}";
}

/// <summary>The SDK clients a run uses, one per role. The .NET SDK is server side only, so both are <see cref="OrvanoClient"/>.</summary>
/// <param name="Client">For <c>as: client</c> steps: no API key.</param>
/// <param name="Server">For <c>as: server</c> steps: the scenario API key.</param>
internal sealed record Surface(OrvanoClient Client, OrvanoClient Server);

/// <summary>
/// The .NET scenario interpreter. The SDK is server side only, so a step whose operation it lacks
/// (a <c>client</c> or <c>console</c> operation) skips the scenario.
/// </summary>
internal static partial class Interpreter
{
    /// <summary>The SDK's events plus the test events, as the spec has runners decode them.</summary>
    private static readonly Dictionary<string, JsonTypeInfo> Events = OrvanoEvents.Registry
        .Concat(TestEvents.Registry)
        .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    public static async Task<List<ScenarioResult>> RunAsync(IEnumerable<JsonObject> scenarios, Surface surface, CancellationToken ct)
    {
        var results = new List<ScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            try
            {
                await RunScenarioAsync(scenario, surface, ct);
                results.Add(new ScenarioResult(name, "passed"));
            }
            catch (ScenarioSkipped e)
            {
                results.Add(new ScenarioResult(name, "skipped", e.Message));
            }
            catch (Exception e) when (e is ScenarioFailure or JsonException or InvalidOperationException or HttpRequestException)
            {
                results.Add(new ScenarioResult(name, "failed", e.Message));
            }
        }

        return results;
    }

    private static async Task RunScenarioAsync(JsonObject scenario, Surface surface, CancellationToken ct)
    {
        var vars = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var steps = scenario["steps"]!.AsArray();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!.AsObject();
            var op = step["op"]?.GetValue<string>();
            var eventName = step["event"]?.GetValue<string>();
            var role = step["as"]?.GetValue<string>();
            var where = $"step {i + 1} ({op ?? $"event {eventName ?? "?"}"}{(role is null ? "" : $" as {role}")})";
            var expect = Substitute(step["expect"], vars)!.AsObject();

            int? status = null;
            string? code = null;
            JsonNode? body;
            if (eventName is not null)
            {
                using var raw = JsonDocument.Parse(Substitute(step["raw"], vars)?.ToJsonString() ?? "null");
                var decoded = OrvanoEvents.Decode(eventName, raw.RootElement, Events)
                    ?? throw new ScenarioFailure($"{where}: the contract has no event {eventName}");
                body = JsonSerializer.SerializeToNode(decoded, Events[eventName]);
            }
            else
            {
                // Console operations are not in this dispatch table, so the role decides first.
                var client = role switch
                {
                    "client" => surface.Client,
                    "server" => surface.Server,
                    "console" => throw new ScenarioSkipped("console steps run only in the JS interpreter"),
                    _ => throw new ScenarioFailure($"{where}: an operation step needs `as`"),
                };
                if (op is null || !Dispatch.Operations.TryGetValue(op, out var entry))
                    throw new ScenarioFailure($"{where}: the contract has no operation {op ?? "?"}");
                var paginate = step["paginate"]?.GetValue<bool>() == true;
                var call = (paginate ? entry.All : entry.Call)
                    ?? throw new ScenarioSkipped($"{op} has no {(paginate ? "paged " : "")}call in the .NET SDK");

                var input = Substitute(step["input"] ?? new JsonObject(), vars)!.AsObject();
                body = null;
                try
                {
                    body = await call(client, input, ct);
                    status = entry.Status;
                }
                catch (OrvanoException e)
                {
                    status = e.Status;
                    code = e.Code;
                }
            }

            if (expect["status"]?.GetValue<int>() is { } expectedStatus && status != expectedStatus)
                throw new ScenarioFailure($"{where}: expected status {expectedStatus}, got {status?.ToString(CultureInfo.InvariantCulture) ?? "none"}{(code is null ? "" : $" ({code})")}");
            if (expect["code"]?.GetValue<string>() is { } expectedCode && expectedCode != code)
                throw new ScenarioFailure($"{where}: expected code {expectedCode}, got {code ?? "none"}");
            if (expect.ContainsKey("body") && SubsetMismatch(expect["body"], body, "$") is { } mismatch)
                throw new ScenarioFailure($"{where}: body {mismatch}");

            foreach (var (name, path) in step["save"]?.AsObject() ?? [])
                vars[name] = Select(body, path!.GetValue<string>())?.DeepClone();
        }
    }

    [GeneratedRegex(@"^\$\{(\w+)\}$")]
    private static partial Regex Whole();

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex Embedded();

    /// <summary>Replaces <c>${name}</c> with saved values; a string that is exactly one keeps the value's type.</summary>
    private static JsonNode? Substitute(JsonNode? node, Dictionary<string, JsonNode?> vars)
    {
        JsonNode? Lookup(string name) => vars.TryGetValue(name, out var value)
            ? value?.DeepClone()
            : throw new ScenarioFailure($"${{{name}}} was never saved by an earlier step");

        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                var whole = Whole().Match(s);
                if (whole.Success) return Lookup(whole.Groups[1].Value);
                return JsonValue.Create(Embedded().Replace(s, m => Lookup(m.Groups[1].Value) switch
                {
                    JsonValue str when str.TryGetValue<string>(out var text) => text,
                    var other => other?.ToJsonString() ?? "null",
                }));
            case JsonArray a:
                return new JsonArray([.. a.Select(item => Substitute(item, vars))]);
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (key, value) in o) copy[key] = Substitute(value, vars);
                return copy;
            default:
                return node?.DeepClone();
        }
    }

    private static JsonNode? Select(JsonNode? body, string path)
    {
        if (!path.StartsWith('$')) throw new ScenarioFailure($"save path {path} must start with $");
        var current = body;
        foreach (var key in path[1..].Split('.', StringSplitOptions.RemoveEmptyEntries))
            current = current is JsonObject o ? o[key] : throw new ScenarioFailure($"save path {path} not found");
        return current;
    }

    private static string? SubsetMismatch(JsonNode? expected, JsonNode? actual, string at)
    {
        switch (expected)
        {
            case JsonArray e:
                if (actual is not JsonArray a || a.Count != e.Count) return $"{at}: expected {e.Count} items";
                for (var i = 0; i < e.Count; i++)
                    if (SubsetMismatch(e[i], a[i], $"{at}[{i}]") is { } m) return m;
                return null;
            case JsonObject e:
                if (actual is not JsonObject obj) return $"{at}: expected an object";
                foreach (var (key, value) in e)
                    if (SubsetMismatch(value, obj[key], $"{at}.{key}") is { } m) return m;
                return null;
            default:
                return JsonNode.DeepEquals(expected, actual)
                    ? null
                    : $"{at}: expected {expected?.ToJsonString() ?? "null"}, got {actual?.ToJsonString() ?? "null"}";
        }
    }
}
