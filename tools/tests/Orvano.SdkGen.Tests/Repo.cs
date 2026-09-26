using System.Text.Json.Nodes;
using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Tests;

/// <summary>Paths into the repo, and contracts built from the committed openapi.json.</summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string OpenApiPath => Path.Combine(Root, "contract", "dist", "openapi.json");

    public static string Version => File.ReadAllText(Path.Combine(Root, "VERSION")).Trim();

    public static TemplateRenderer Renderer { get; } = new(Path.Combine(Root, "tools", "sdkgen", "templates"));

    /// <summary>The committed contract as a mutable JSON tree.</summary>
    public static JsonObject OpenApi() => JsonNode.Parse(File.ReadAllText(OpenApiPath))!.AsObject();

    /// <summary>Reads <paramref name="doc"/> through <see cref="ContractReader"/>, as SdkGen does.</summary>
    public static async Task<(ApiContract? Contract, IReadOnlyList<string> Errors)> ReadAsync(JsonObject doc, string? version = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orvano-sdkgen-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, doc.ToJsonString(), TestContext.Current.CancellationToken);
        try
        {
            return await ContractReader.ReadAsync(path, version ?? Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The operation at <paramref name="path"/> and <paramref name="method"/>.</summary>
    public static JsonObject Operation(JsonObject doc, string path, string method) => doc["paths"]![path]![method]!.AsObject();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Orvano.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Could not find Orvano.slnx above " + AppContext.BaseDirectory);
    }
}
