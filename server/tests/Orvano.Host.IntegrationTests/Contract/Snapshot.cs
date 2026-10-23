using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>
/// Snapshot tests for emitted contract fragments (spec 0014 CTR-6, spec 0002 TST-7): the output is compared with a
/// committed file in <c>Snapshots/</c> beside the test, and a difference fails naming the first JSON pointer that
/// differs. Set <c>ORVANO_SNAPSHOT_WRITE=1</c> to rewrite the files, then review the diff before committing.
/// </summary>
internal static class Snapshot
{
    public const string WriteVariable = "ORVANO_SNAPSHOT_WRITE";

    public static async Task MatchAsync(string actual, string name, [CallerFilePath] string testFile = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots", name + ".json");
        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(path).ShouldBeTrue($"No snapshot at {path}. Run the test with {WriteVariable}=1, review the file, and commit it.");
        string expected = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        if (expected != actual)
        {
            string where = ContractJson.FirstDifference(JsonNode.Parse(expected), JsonNode.Parse(actual)) ?? "(formatting only)";
            Assert.Fail($"Snapshots/{name}.json differs first at {where}. If the change is intended, rerun with {WriteVariable}=1 and review the diff.");
        }
    }
}
