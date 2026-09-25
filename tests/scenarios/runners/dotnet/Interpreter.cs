using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Orvano.Scenarios.Generated;

namespace Orvano.Scenarios;

internal sealed class ScenarioFailure(string message) : Exception(message);

internal sealed class ScenarioSkipped(string message) : Exception(message);

internal sealed record ScenarioResult(string Name, string Outcome, string? Reason = null)
{
    public override string ToString() => $"{Outcome,-7} {Name}{(Reason is null ? "" : ": " + Reason)}";
}

/// <summary>
/// The .NET scenario interpreter. The SDK is server side only, so every role uses the same client;
/// a step whose operation the SDK lacks (a <c>client</c> operation) skips the scenario.
/// </summary>
internal static partial class Interpreter
{
    public static async Task<List<ScenarioResult>> RunAsync(IEnumerable<JsonObject> scenarios, OrvanoClient client, CancellationToken ct)
    {
        var results = new List<ScenarioResult>();
        foreach (var scenario in scenarios)
        {
            var name = scenario["name"]!.GetValue<string>();
            try
            {
                await RunScenarioAsync(scenario, client, ct);
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

    private static async Task RunScenarioAsync(JsonObject scenario, OrvanoClient client, CancellationToken ct)
    {
        var vars = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var steps = scenario["steps"]!.AsArray();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!.AsObject();
            var op = step["op"]!.GetValue<string>();
            var role = step["as"]!.GetValue<string>();
            var where = $"step {i + 1} ({op} as {role})";
            if (!Dispatch.Operations.TryGetValue(op, out var entry))
                throw new ScenarioFailure($"{where}: the contract has no operation {op}");
            if (role == "console")
                throw new ScenarioSkipped("console steps run only in the JS interpreter");
            if (entry.Call is null)
                throw new ScenarioSkipped($"{op} has no call in the .NET SDK");

            var input = Substitute(step["input"] ?? new JsonObject(), vars)!.AsObject();
            var expect = Substitute(step["expect"], vars)!.AsObject();

            int status;
            string? code = null;
            JsonNode? body = null;
            try
            {
                body = await entry.Call(client, input, ct);
                status = entry.Status;
            }
            catch (OrvanoException e)
            {
                status = e.Status;
                code = e.Code;
            }

            var expectedStatus = expect["status"]!.GetValue<int>();
            if (status != expectedStatus)
                throw new ScenarioFailure($"{where}: expected status {expectedStatus}, got {status}{(code is null ? "" : $" ({code})")}");
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
