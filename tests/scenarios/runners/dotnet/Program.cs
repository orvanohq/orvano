// Runs the shared scenarios through the Orvano .NET SDK against the Orvano at ORVANO_ENDPOINT
// (default http://localhost:8080).
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Orvano;
using Orvano.Scenarios;
using YamlDotNet.Serialization;

var endpoint = Environment.GetEnvironmentVariable("ORVANO_ENDPOINT") ?? "http://localhost:8080";
var scenariosDir = FindScenarios(AppContext.BaseDirectory)
    ?? throw new DirectoryNotFoundException("tests/scenarios not found above " + AppContext.BaseDirectory);

// YAML to JSON: YamlDotNet reads plain objects, System.Text.Json turns them into nodes.
var yaml = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
var scenarios = Directory.GetFiles(scenariosDir, "*.yaml")
    .Where(f => Path.GetFileName(f) != "fixtures.yaml")
    .Order(StringComparer.Ordinal)
    .Select(f => JsonSerializer.SerializeToNode(yaml.Deserialize<object>(File.ReadAllText(f)))!.AsObject())
    .ToList();

// The API key scenario runners send; the server ignores keys until the auth spec (row 8).
const string TestServerKey = "test-server-key";

using var client = new OrvanoClient(new OrvanoClientOptions(new Uri(endpoint)));
using var server = new OrvanoClient(new OrvanoClientOptions(new Uri(endpoint)) { ApiKey = TestServerKey });
var results = await Interpreter.RunAsync(scenarios, new Surface(client, server), CancellationToken.None);

var sdkTarget = typeof(OrvanoClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
var surface = $".NET ({RuntimeInformation.FrameworkDescription}, SDK built for {sdkTarget})";
foreach (var r in results) Console.WriteLine(r);
var failed = results.Count(r => r.Outcome == "failed");
var passed = results.Count(r => r.Outcome == "passed");
Console.WriteLine($"{surface}: {passed} passed, {failed} failed, {results.Count - passed - failed} skipped");
return failed > 0 || passed == 0 ? 1 : 0;

static string? FindScenarios(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "tests", "scenarios");
        if (File.Exists(Path.Combine(candidate, "fixtures.yaml"))) return candidate;
    }

    return null;
}
