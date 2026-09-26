using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// The TypeScript core (<c>@orvano/js</c>, entries <c>.</c> and <c>./server</c>), the private
/// <c>@orvano/console-client</c>, and the JS runner's test services, surfaces, and dispatch tables.
/// </summary>
internal static class TypeScript
{
    public sealed record Property(string Doc, string Declaration);

    public sealed record Model(string Doc, string Name, IReadOnlyList<Property> Properties);

    public sealed record EnumDef(string Doc, string Name, string Union);

    public sealed record Constant(string Doc, string Name, string Value);

    public sealed record CodeCatalog(string Doc, string Name, IReadOnlyList<Constant> Codes);

    public sealed record Event(string Name, string Type);

    public sealed record Operation(string Doc, string Signature, string Body);

    public sealed record Service(string ClassName, string Property, string Name, IReadOnlyList<Operation> Operations);

    public sealed record Aggregate(string Doc, string Name, string? Base);

    public sealed record DispatchCall(string Key, string Fn);

    public sealed record DispatchEntry(string Id, int Status, IReadOnlyList<DispatchCall> Calls);

    private const string Sdk = "@orvano/js";
    private const string SdkServer = "@orvano/js/server";
    private const string ConsoleSdk = "@orvano/console-client";

    /// <summary>Where a generated file finds the handwritten runtime and each model.</summary>
    private sealed record Modules(string Client, string Pagination, Func<string, string> ModelModule);

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        // The published SDK: everything public except console operations.
        var publicSlice = contract.Slice(o => !o.Test && o.Audience != Audience.Console, e => !e.Test);
        var publicTypes = Names(publicSlice);
        var sdk = new Modules("../runtime/client.js", "../runtime/pagination.js", _ => "./models.js");

        yield return new GeneratedOutput("TypeScript core", "sdks/js/src/generated", Formatter.Prettier,
        [
            new("sdks/js/src/generated/models.ts", renderer.Render("ts", "models", Models(publicSlice, null, _ => ""))),
            new("sdks/js/src/generated/errors.ts", renderer.Render("ts", "models", Models(publicSlice with { Models = [], Enums = [] }, ErrorCodes(contract, test: false), _ => ""))),
            new("sdks/js/src/generated/events.ts", renderer.Render("ts", "events", Events(publicSlice, "eventRegistry", "../runtime/events.js", "./models.js"))),
            new("sdks/js/src/generated/client.ts", renderer.Render("ts", "services", Services(
                contract.Slice(o => !o.Test && o.IsClient, _ => false), sdk,
                new Aggregate("Every service in this entry, on one object: `orvano.health.get()`.", "Orvano", null)))),
            new("sdks/js/src/generated/server.ts", renderer.Render("ts", "services", Services(
                contract.Slice(o => !o.Test && o.IsServer, _ => false), sdk,
                new Aggregate("Every service in this entry, on one object: `orvano.health.get()`.", "Orvano", null)))),
            new("sdks/js/src/generated/version.ts", renderer.Render("ts", "version", new { contract.Version })),
        ]);

        // The private console client (AC-17): console operations only, on the @orvano/js runtime.
        // Models it shares with the public SDK come from @orvano/js; the rest are defined here.
        var consoleSlice = contract.Slice(o => !o.Test && o.Audience == Audience.Console, _ => false);
        var consoleModels = consoleSlice with
        {
            Models = [.. consoleSlice.Models.Where(m => !publicTypes.Contains(m.Name))],
            Enums = [.. consoleSlice.Enums.Where(e => !publicTypes.Contains(e.Name))],
        };
        var consoleTypes = Names(consoleModels);
        yield return new GeneratedOutput("Console client", "sdks/console-client/src/generated", Formatter.Prettier,
        [
            new("sdks/console-client/src/generated/models.ts", renderer.Render("ts", "models",
                Models(consoleModels, null, n => publicTypes.Contains(n) ? Sdk : ""))),
            new("sdks/console-client/src/generated/services.ts", renderer.Render("ts", "services", Services(
                consoleSlice,
                new Modules(Sdk, Sdk, n => publicTypes.Contains(n) ? Sdk : "./models.js"),
                new Aggregate("Every console service on one object. Only the Orvano console uses it.", "Orvano", null)))),
        ]);

        // The JS runner (AC-18): test operations, models, events, and error codes live only here.
        var testSlice = contract.Slice(o => o.Test, e => e.Test);
        var testModels = testSlice with
        {
            Models = [.. testSlice.Models.Where(m => m.Test)],
            Enums = [.. testSlice.Enums.Where(e => e.Test)],
        };
        var testTypes = Names(testModels);
        string RunnerModule(string name) =>
            testTypes.Contains(name) ? "./test-models.js" : consoleTypes.Contains(name) ? ConsoleSdk : Sdk;
        var runner = new Modules(Sdk, Sdk, RunnerModule);

        var models = Models(testModels, ErrorCodes(contract, test: true), RunnerModule);
        var dispatch = contract.Operations.Where(o => o.Audience != Audience.Console).Select(o => new DispatchEntry(o.Id, o.SuccessStatus,
        [
            .. o.IsClient ? [new DispatchCall("client", Call(o, "o"))] : Array.Empty<DispatchCall>(),
            .. o.IsServer ? [new DispatchCall("server", Call(o, "o"))] : Array.Empty<DispatchCall>(),
            .. o.IsClient && o.PageItem is not null ? [new DispatchCall("clientAll", CallAll(o))] : Array.Empty<DispatchCall>(),
            .. o.IsServer && o.PageItem is not null ? [new DispatchCall("serverAll", CallAll(o))] : Array.Empty<DispatchCall>(),
        ])).ToList();
        var consoleDispatch = contract.Operations.Where(o => o.Audience == Audience.Console).Select(o => new DispatchEntry(o.Id, o.SuccessStatus,
        [
            new DispatchCall("console", Call(o, "o")),
            .. o.PageItem is not null ? [new DispatchCall("consoleAll", CallAll(o))] : Array.Empty<DispatchCall>(),
        ])).ToList();

        yield return new GeneratedOutput("JS scenario runner", "tests/scenarios/runners/js/src/generated", Formatter.Prettier,
        [
            new("tests/scenarios/runners/js/src/generated/test-models.ts", renderer.Render("ts", "models", models)),
            new("tests/scenarios/runners/js/src/generated/test-events.ts", renderer.Render("ts", "events",
                Events(testSlice with { Models = [.. testSlice.Models.Where(m => m.Test)] }, "testEventRegistry", Sdk, "./test-models.js"))),
            new("tests/scenarios/runners/js/src/generated/client.ts", renderer.Render("ts", "services", Services(
                contract.Slice(o => o.Test && o.IsClient, _ => false), runner,
                new Aggregate("`@orvano/js` plus the test services, as the scenarios call them.", "ClientSurface", $"Orvano:{Sdk}")))),
            new("tests/scenarios/runners/js/src/generated/server.ts", renderer.Render("ts", "services", Services(
                contract.Slice(o => o.Test && o.IsServer, _ => false), runner,
                new Aggregate("`@orvano/js/server` plus the test services, as the scenarios call them.", "ServerSurface", $"Orvano:{SdkServer}")))),
            new("tests/scenarios/runners/js/src/generated/console.ts", renderer.Render("ts", "services", Services(
                contract.Slice(o => o.Test && o.Audience == Audience.Console, _ => false), runner,
                new Aggregate("`@orvano/console-client` plus the console test services, as the scenarios call them.", "ConsoleSurface", $"Orvano:{ConsoleSdk}")))),
            new("tests/scenarios/runners/js/src/generated/dispatch.ts", renderer.Render("ts", "dispatch",
                Dispatch("dispatch", "Every non console operation; a missing `client` or `server` call means the SDK has none.", dispatch,
                    contract.Operations.Where(o => o.Audience != Audience.Console), RunnerModule))),
            new("tests/scenarios/runners/js/src/generated/console-dispatch.ts", renderer.Render("ts", "dispatch",
                Dispatch("consoleDispatch", "Every console operation, called through `@orvano/console-client`.", consoleDispatch,
                    contract.Operations.Where(o => o.Audience == Audience.Console), RunnerModule))),
        ]);
    }

    private static HashSet<string> Names(ApiContract slice) =>
        slice.Models.Select(m => m.Name).Concat(slice.Enums.Select(e => e.Name)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Models and enums for one file; types defined elsewhere are imported from <paramref name="module"/>.</summary>
    private static object Models(ApiContract slice, CodeCatalog? catalog, Func<string, string> module)
    {
        var defined = Names(slice);
        var used = slice.Models.SelectMany(m => m.Properties).SelectMany(p => Mentioned(p.Type)).Where(n => !defined.Contains(n));
        return new
        {
            Imports = Imports(used, module),
            Enums = slice.Enums.Select(e => new EnumDef(Doc(e.Doc, ""), e.Name, string.Join(" | ", e.Values.Select(Naming.QuotedString)))).ToList(),
            Models = slice.Models.Select(m => new Model(
                Doc(m.Doc, ""),
                m.Name,
                [.. m.Properties.Select(p => new Property(Doc(p.Doc, "  "), $"{p.Name}{(p.Optional ? "?" : "")}: {Type(p.Type)}{(p.Nullable ? " | null" : "")}"))])).ToList(),
            Catalog = catalog,
        };
    }

    private static CodeCatalog ErrorCodes(ApiContract contract, bool test) => new(
        Doc(test ? "Error codes only the test operations send." : "Every stable error code the server sends as `OrvanoError.code`.", ""),
        test ? "TestErrorCode" : "ErrorCode",
        [.. contract.ErrorCodes.Where(c => c.Test == test).Select(c => new Constant($"  /** `{c.Code}` */\n", Naming.MemberFromWire(c.Code), Naming.QuotedString(c.Code)))]);

    private static object Events(ApiContract slice, string name, string runtime, string models) => new
    {
        Name = name,
        Runtime = runtime,
        Models = models,
        Events = slice.Models.Where(m => m.Event is not null).OrderBy(m => m.Event, StringComparer.Ordinal)
            .Select(m => new Event(Naming.QuotedString(m.Event!), m.Name)).ToList(),
        ModelImports = string.Join(", ", slice.Models.Where(m => m.Event is not null).Select(m => m.Name).Order(StringComparer.Ordinal)),
    };

    private static object Services(ApiContract slice, Modules modules, Aggregate aggregate)
    {
        var services = slice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            s.Service,
            s.Service,
            [.. s.Operations.SelectMany(ToOperations)])).ToList();

        // Type names and value names per module, then one `import type` and one `import` line each.
        var types = slice.Operations.SelectMany(o => Mentioned(o.Body).Concat(Mentioned(o.Result)).Concat(Mentioned(o.PageItem)))
            .Select(n => (Module: modules.ModelModule(n), Name: n))
            .Append((Module: modules.Client, Name: "Client"));
        if (services.Count > 0) types = types.Append((Module: modules.Client, Name: "RequestOptions"));
        var values = new List<(string Module, string Name)>();
        if (slice.Operations.Any(o => o.PageItem is not null)) values.Add((modules.Pagination, "paginate"));

        string? baseName = null;
        if (aggregate.Base?.Split(':') is [var name, var module])
        {
            values.Add((module, name));
            baseName = name;
        }

        var imports = types.Select(t => (t.Module, t.Name, Type: true)).Concat(values.Select(v => (v.Module, v.Name, Type: false)))
            .GroupBy(x => (x.Module, x.Type))
            .OrderBy(g => g.Key.Module, StringComparer.Ordinal).ThenBy(g => !g.Key.Type)
            .Select(g => $"import {(g.Key.Type ? "type " : "")}{{ {string.Join(", ", g.Select(x => x.Name).Distinct().Order(StringComparer.Ordinal))} }} from '{g.Key.Module}'")
            .ToList();

        return new
        {
            Imports = imports,
            Services = services,
            Aggregate = aggregate with { Doc = Doc(aggregate.Doc, ""), Base = baseName },
        };
    }

    /// <summary>One <c>import type</c> line per module, names sorted, for every name the module owns.</summary>
    private static List<string> Imports(IEnumerable<string> names, Func<string, string> module) =>
        [.. names.Distinct(StringComparer.Ordinal)
            .Select(n => (Name: n, Module: module(n)))
            .Where(x => x.Module.Length > 0)
            .GroupBy(x => x.Module, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"import type {{ {string.Join(", ", g.Select(x => x.Name).Order(StringComparer.Ordinal))} }} from '{g.Key}'")];

    private static IEnumerable<string> Mentioned(TypeRef? type) => type switch
    {
        ModelType m => [m.Name],
        EnumType e => [e.Name],
        ArrayType a => Mentioned(a.Item),
        MapType m => Mentioned(m.Value),
        _ => [],
    };

    private static IEnumerable<Operation> ToOperations(ContractOperation op)
    {
        var result = op.Result is null ? "void" : Type(op.Result);
        var request = new List<string> { $"method: '{op.HttpMethod}'", $"path: {Path(op)}" };
        if (op.Params.Any(p => p.In == ParamLocation.Query)) request.Add("query");
        if (op.Body is not null) request.Add("body");
        if (op.Idempotent) request.Add("idempotent: true");

        yield return new Operation(
            Doc(op.Doc, "  "),
            $"{op.Name}({Params(op, withCursor: true)}): Promise<{result}>",
            // A no content call resolves to undefined; `void` is not allowed as a type argument.
            $"return this.#client.request<{(op.Result is null ? "undefined" : result)}>({{ {string.Join(", ", request)} }}, options)");

        if (op.PageItem is null) yield break;
        var rest = op.Params.Any(p => p.In == ParamLocation.Query && p.Name != "cursor") ? "{ ...query, cursor }" : "{ cursor }";
        var pathArgs = op.Params.Where(p => p.In == ParamLocation.Path).Select(p => p.Name).Append(rest).Append("options");
        yield return new Operation(
            Doc($"Every item of `{op.Name}`, walking all pages: `for await (const item of ...)`.", "  "),
            $"{op.Name}All({Params(op, withCursor: false)}): AsyncGenerator<{Type(op.PageItem)}>",
            $"return paginate((cursor) => this.{op.Name}({string.Join(", ", pathArgs)}))");
    }

    private static string Params(ContractOperation op, bool withCursor)
    {
        var parameters = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) parameters.Add($"{p.Name}: {Type(p.Type)}");
        if (op.Body is not null) parameters.Add($"body: {op.Body.Name}");
        var query = op.Params.Where(p => p.In == ParamLocation.Query && (withCursor || p.Name != "cursor")).ToList();
        if (query.Count > 0)
        {
            var optional = query.All(q => !q.Required);
            var fields = string.Join("; ", query.Select(q => q.Required ? $"{q.Name}: {Type(q.Type)}" : $"{q.Name}?: {Type(q.Type)} | undefined"));
            parameters.Add($"query{(optional ? "?" : "")}: {{ {fields} }}");
        }

        parameters.Add("options?: RequestOptions");
        return string.Join(", ", parameters);
    }

    private static string Path(ContractOperation op)
    {
        if (!op.Path.Contains('{', StringComparison.Ordinal)) return Naming.QuotedString(op.Path);
        var path = op.Path;
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path))
        {
            var value = p.Type.Kind == PrimitiveKind.String ? p.Name : $"String({p.Name})";
            path = path.Replace("{" + p.Name + "}", "${encodeURIComponent(" + value + ")}", StringComparison.Ordinal);
        }

        return "`" + path + "`";
    }

    /// <summary>The dispatch call for one operation: reads arguments from the scenario input.</summary>
    private static string Call(ContractOperation op, string target) =>
        $"(o, {(Args(op, withCursor: true) is { Length: > 0 } a ? $"input) => {target}.{op.Service}.{op.Name}({a})" : $"_input) => {target}.{op.Service}.{op.Name}()")}";

    private static string CallAll(ContractOperation op) =>
        $"(o, {(Args(op, withCursor: false) is { Length: > 0 } a ? $"input) => o.{op.Service}.{op.Name}All({a})" : $"_input) => o.{op.Service}.{op.Name}All()")}";

    private static string Args(ContractOperation op, bool withCursor)
    {
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) args.Add($"input.{p.Name} as {Type(p.Type)}");
        if (op.Body is not null) args.Add($"input.body as {op.Body.Name}");
        var query = op.Params.Where(p => p.In == ParamLocation.Query && (withCursor || p.Name != "cursor")).ToList();
        if (query.Count > 0)
            args.Add("{ " + string.Join(", ", query.Select(q => $"{q.Name}: input.{q.Name} as {Type(q.Type)}{(q.Required ? "" : " | undefined")}")) + " }");
        return string.Join(", ", args);
    }

    private static object Dispatch(string name, string doc, List<DispatchEntry> entries, IEnumerable<ContractOperation> operations, Func<string, string> module) => new
    {
        Name = name,
        Doc = doc,
        Entries = entries,
        Imports = Imports(operations.Where(o => o.Body is not null).Select(o => o.Body!.Name), module),
    };

    public static string Type(TypeRef type) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String or PrimitiveKind.DateTime } => "string",
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Int64 or PrimitiveKind.Float64 } => "number",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => "boolean",
        ArrayType a => $"{Type(a.Item)}[]",
        MapType m => $"Record<string, {Type(m.Value)}>",
        ModelType m => m.Name,
        EnumType e => e.Name,
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    private static string Doc(string? doc, string indent)
    {
        var lines = Naming.DocLines(doc).Select(l => l.Replace("*/", "*\\/", StringComparison.Ordinal)).ToList();
        return lines.Count switch
        {
            0 => "",
            1 => $"{indent}/** {lines[0]} */\n",
            _ => $"{indent}/**\n" + string.Concat(lines.Select(l => $"{indent} * {l}\n")) + $"{indent} */\n",
        };
    }
}
