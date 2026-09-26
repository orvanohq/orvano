using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Languages;
using Orvano.SdkGen.Rendering;

// Orvano SdkGen (spec 0001). Reads contract/dist/openapi.json, validates it (AC-2), and regenerates
// every SDK's generated layer, the server contract types, the scenario dispatch tables (AC-3), the
// public contract and docs snippets (AC-15), and stamps VERSION into the package manifests (AC-11).
// Output is sorted and formatted, so running it twice produces no diff.

var root = FindRepoRoot(Directory.GetCurrentDirectory());
if (root is null)
{
    Console.Error.WriteLine("error: run SdkGen inside the Orvano repo (no Orvano.slnx found above the current directory)");
    return 2;
}

var version = (await File.ReadAllTextAsync(Path.Combine(root, "VERSION"))).Trim();
var openApi = Path.Combine(root, "contract", "dist", "openapi.json");
if (!File.Exists(openApi))
{
    Console.Error.WriteLine("error: contract/dist/openapi.json is missing; run `pnpm --filter @orvano/contract build` first");
    return 2;
}

var (contract, errors) = await ContractReader.ReadAsync(openApi, version);
if (contract is null)
{
    foreach (var e in errors) Console.Error.WriteLine($"error: {e}");
    Console.Error.WriteLine($"SdkGen stopped: {errors.Count} problem(s) in the contract. Nothing was written.");
    return 1;
}

Console.WriteLine($"Orvano SdkGen: contract {contract.Version}, {contract.Operations.Count} operation(s), {contract.Models.Count} model(s)");

var renderer = new TemplateRenderer(Path.Combine(AppContext.BaseDirectory, "templates"));
List<GeneratedOutput> outputs =
[
    .. TypeScript.Generate(contract, renderer),
    .. Dart.Generate(contract, renderer),
    .. CSharp.Generate(contract, renderer),
    .. Snippets.Generate(contract, renderer),
    new("Public contract", "contract/dist", Formatter.None,
        [new("contract/dist/openapi.public.json", PublicContract.Build(await File.ReadAllTextAsync(openApi)))],
        OwnsDirectory: false),
];

try
{
    await OutputWriter.WriteAsync(root, outputs);
    await ManifestStamper.StampAsync(root, version);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

Console.WriteLine("Done.");
return 0;

static string? FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Orvano.slnx"))) return dir.FullName;
    return null;
}
