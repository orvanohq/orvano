using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

using Orvano.Host.Api.OpenApi;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>One host built like the image with every role on, whose two documents the contract tests read.</summary>
public sealed class ContractHostFixture : IAsyncLifetime
{
    private readonly ConsoleBuild _console = new();
    private OrvanoApiFactory? _factory;

    public ContractHostFixture(PostgresFixture postgres)
    {
        Dictionary<string, string> settings = OrvanoApiFactory.DefaultSettings(postgres);
        settings["ROLES"] = "api,worker";
        _factory = new OrvanoApiFactory(settings, _console.ContentRoot);
    }

    public IServiceProvider Services => _factory!.Services;

    public ValueTask InitializeAsync()
    {
        using HttpClient client = _factory!.CreateClient(); // starts the host, so the startup checks run
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _factory = null;
        _console.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>The document in its written form (CTR-4).</summary>
    public async Task<string> EmitAsync(string documentName, CancellationToken cancellationToken)
    {
        OpenApiDocument document = await Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(documentName)
            .GetOpenApiDocumentAsync(cancellationToken);
        return ContractJson.Normalize(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken));
    }
}

/// <summary>
/// Spec 0014 AC-1, CTR-3, CTR-5: the committed contract equals what the server emits. <c>pnpm api:emit</c> runs this
/// class with <c>ORVANO_CONTRACT_WRITE=1</c> to rewrite <c>api/</c>; every other run (the server CI job included)
/// compares and fails naming the first difference. The host never maps the test fixture endpoints of other classes.
/// </summary>
public sealed class ContractEmitTests(ContractHostFixture host) : IClassFixture<ContractHostFixture>
{
    public const string WriteVariable = "ORVANO_CONTRACT_WRITE";

    public static TheoryData<string, string> Documents { get; } = new()
    {
        { ContractDocuments.Public, "openapi.json" },
        { ContractDocuments.Console, "console.openapi.json" },
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory, MemberData(nameof(Documents)), Trait("Spec", "0014:AC-1"), Trait("Spec", "0014:CTR-3"), Trait("Spec", "0014:CTR-5")]
    public async Task Committed_contract_equals_the_emitted_document_when_compared(string documentName, string fileName)
    {
        string emitted = await host.EmitAsync(documentName, Token);
        string path = Path.Combine(RepoPaths.Api, fileName);

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            await File.WriteAllTextAsync(path, emitted, Token);
            return;
        }

        File.Exists(path).ShouldBeTrue($"api/{fileName} is missing. Run `pnpm api:emit` and commit it.");
        string committed = await File.ReadAllTextAsync(path, Token);
        if (committed != emitted)
        {
            string where = ContractJson.FirstDifference(JsonNode.Parse(committed), JsonNode.Parse(emitted)) ?? "(formatting only)";
            Assert.Fail($"api/{fileName} differs from what the server emits, first at {where}. Run `pnpm api:emit` and commit the result.");
        }
    }

    [Theory, MemberData(nameof(Documents)), Trait("Spec", "0014:AC-1"), Trait("Spec", "0014:CTR-4")]
    public async Task Emit_writes_the_same_bytes_when_run_twice(string documentName, string fileName)
    {
        _ = fileName;
        string first = await host.EmitAsync(documentName, Token);
        string second = await host.EmitAsync(documentName, Token);

        second.ShouldBe(first);
        first.ShouldEndWith("}\n");
        first.ShouldNotContain("\r");
        ContractJson.Normalize(first).ShouldBe(first);
    }
}

/// <summary>Where the repo's files are, found from the test binaries by walking up to the pnpm workspace root.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Api => Path.Combine(Root, "api");

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "pnpm-workspace.yaml")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No pnpm-workspace.yaml above {AppContext.BaseDirectory}; run the tests from the repo checkout.");
    }
}
