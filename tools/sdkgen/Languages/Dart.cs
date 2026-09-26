using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// <c>orvano_core</c> (models, client and both services, events, error codes), <c>orvano_dart</c>
/// (server services), and the Dart runner's test code and dispatch table. JSON mapping is written
/// out by hand style, no build_runner.
/// </summary>
internal static class Dart
{
    public sealed record Field(string Doc, string Declaration, string Param, string FromJson, string ToJson);

    public sealed record Model(string Doc, string Name, IReadOnlyList<Field> Fields);

    public sealed record EnumMember(string Name, string Wire);

    public sealed record EnumDef(string Doc, string Name, IReadOnlyList<EnumMember> Members);

    public sealed record Constant(string Doc, string Name, string Value);

    public sealed record CodeCatalog(string Doc, string Name, IReadOnlyList<Constant> Codes);

    public sealed record Event(string Name, string Type);

    public sealed record Operation(string Doc, string Signature, string Body);

    public sealed record Service(string ClassName, string Property, string Name, IReadOnlyList<Operation> Operations);

    public sealed record Aggregate(string Doc, string Name, string? Base, IReadOnlyList<Service> Services);

    public sealed record DispatchCall(string Key, string Fn);

    public sealed record DispatchEntry(string Id, int Status, IReadOnlyList<DispatchCall> Calls);

    private const string Core = "package:orvano_core/orvano_core.dart";
    private const string ServerPackage = "package:orvano_dart/orvano_dart.dart";

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var publicSlice = contract.Slice(o => !o.Test && o.Audience != Audience.Console, e => !e.Test);
        var clientView = contract.Slice(o => !o.Test && o.IsClient, _ => false);
        var serverView = contract.Slice(o => !o.Test && o.IsServer, _ => false);

        var coreServices = Services(clientView);
        yield return new GeneratedOutput("Dart core", "sdks/dart/core/lib/src/generated", Formatter.Dart,
        [
            new("sdks/dart/core/lib/src/generated/models.dart", renderer.Render("dart", "models", Models(publicSlice, null, []))),
            new("sdks/dart/core/lib/src/generated/errors.dart", renderer.Render("dart", "models",
                Models(publicSlice with { Models = [], Enums = [] }, ErrorCodes(contract, test: false), []))),
            new("sdks/dart/core/lib/src/generated/events.dart", renderer.Render("dart", "events",
                Events(publicSlice, "eventRegistry", ["../events.dart", "models.dart"]))),
            new("sdks/dart/core/lib/src/generated/services.dart", renderer.Render("dart", "services", new
            {
                Imports = Imports(clientView, "../client.dart", "../pagination.dart", "models.dart"),
                Services = coreServices,
                Aggregate = new Aggregate(Doc("Every service in this package, on one object: `orvano.health.get()`.", ""), "Orvano", null, coreServices),
            })),
            new("sdks/dart/core/lib/src/generated/version.dart", renderer.Render("dart", "version", new { contract.Version })),
        ]);

        // The server package reuses a core service when its server view is the same set of
        // operations, and defines its own (hiding core's) when the views differ.
        var serverServices = Services(serverView);
        var coreByName = coreServices.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var sameAsCore = serverView.Services
            .Where(s => clientView.Services.Any(c => c.Service == s.Service && c.Operations.Select(o => o.Id).SequenceEqual(s.Operations.Select(o => o.Id))))
            .Select(s => s.Service).ToHashSet(StringComparer.Ordinal);
        var hidden = new[] { "Client", "Orvano" }
            .Concat(coreServices.Where(s => !sameAsCore.Contains(s.Name)).Select(s => s.ClassName))
            .Order(StringComparer.Ordinal);
        yield return new GeneratedOutput("Dart server", "sdks/dart/server/lib/src/generated", Formatter.Dart,
        [
            new("sdks/dart/server/lib/src/generated/services.dart", renderer.Render("dart", "services", new
            {
                Imports = new[] { $"import '{Core}';" },
                Services = serverServices.Where(s => !sameAsCore.Contains(s.Name)).ToList(),
                Aggregate = new Aggregate(Doc("Every service in this package, on one object: `orvano.health.get()`.", ""), "Orvano", null, serverServices),
            })),
            new("sdks/dart/server/lib/src/generated/core_exports.dart", renderer.Render("dart", "core_exports", new
            {
                Hidden = string.Join(", ", hidden),
            })),
        ]);

        // The Dart runner (AC-18): test code lives only here, on top of the SDK packages.
        var testSlice = contract.Slice(o => o.Test, e => e.Test);
        var testModels = testSlice with
        {
            Models = [.. testSlice.Models.Where(m => m.Test)],
            Enums = [.. testSlice.Enums.Where(e => e.Test)],
        };
        var testTypes = testModels.Models.Select(m => m.Name).Concat(testModels.Enums.Select(e => e.Name)).ToHashSet(StringComparer.Ordinal);
        var testClient = contract.Slice(o => o.Test && o.IsClient, _ => false);
        var testServer = contract.Slice(o => o.Test && o.IsServer, _ => false);
        var dispatch = contract.Operations.Where(o => o.Audience != Audience.Console).Select(o => new DispatchEntry(o.Id, o.SuccessStatus,
        [
            .. o.IsClient ? [new DispatchCall("client", Call(o))] : Array.Empty<DispatchCall>(),
            .. o.IsServer ? [new DispatchCall("server", Call(o))] : Array.Empty<DispatchCall>(),
            .. o.IsClient && o.PageItem is not null ? [new DispatchCall("clientAll", CallAll(o))] : Array.Empty<DispatchCall>(),
            .. o.IsServer && o.PageItem is not null ? [new DispatchCall("serverAll", CallAll(o))] : Array.Empty<DispatchCall>(),
        ])).ToList();
        var dispatchTypes = contract.Operations.Where(o => o.Audience != Audience.Console)
            .SelectMany(o => o.Params.Select(p => (TypeRef?)p.Type).Append(o.Body)).ToList();

        yield return new GeneratedOutput("Dart scenario runner", "tests/scenarios/runners/dart/lib/src/generated", Formatter.Dart,
        [
            new("tests/scenarios/runners/dart/lib/src/generated/test_models.dart", renderer.Render("dart", "models",
                Models(testModels, ErrorCodes(contract, test: true), UsesPublic(testModels.Models.SelectMany(m => m.Properties).Select(p => p.Type), testTypes)))),
            new("tests/scenarios/runners/dart/lib/src/generated/test_events.dart", renderer.Render("dart", "events",
                Events(testModels, "testEventRegistry", [Core, "test_models.dart"]))),
            new("tests/scenarios/runners/dart/lib/src/generated/test_client.dart", renderer.Render("dart", "services", Surface(
                testClient, testTypes, "ClientSurface", "Orvano", [], "`orvano_core` plus the test services, as the scenarios call them."))),
            new("tests/scenarios/runners/dart/lib/src/generated/test_server.dart", renderer.Render("dart", "services", Surface(
                testServer, testTypes, "ServerSurface", "srv.Orvano", [$"import '{ServerPackage}' as srv;"], "`orvano_dart` plus the test services, as the scenarios call them."))),
            new("tests/scenarios/runners/dart/lib/src/generated/dispatch.dart", renderer.Render("dart", "dispatch", new
            {
                Entries = dispatch,
                Imports = UsesPublic(dispatchTypes, testTypes)
                    .Concat(dispatchTypes.SelectMany(Mentioned).Any(testTypes.Contains) ? ["import 'test_models.dart';"] : Array.Empty<string>())
                    .ToList(),
            })),
        ]);
    }

    private static object Surface(ApiContract slice, HashSet<string> testTypes, string name, string baseName, string[] extraImports, string doc)
    {
        var services = Services(slice);
        var types = slice.Operations.SelectMany(o => Mentioned(o.Body).Concat(Mentioned(o.Result)).Concat(Mentioned(o.PageItem))).ToList();
        List<string> imports = [$"import '{Core}';", .. extraImports];
        if (types.Any(testTypes.Contains)) imports.Add("import 'test_models.dart';");
        return new
        {
            Imports = imports,
            Services = services,
            Aggregate = new Aggregate(Doc(doc, ""), name, baseName, services),
        };
    }

    /// <summary>Imports for a services file inside <c>orvano_core</c>: only what the operations use.</summary>
    private static List<string> Imports(ApiContract slice, string client, string pagination, string models)
    {
        List<string> imports = [$"import '{client}';"];
        if (slice.Operations.Any(o => o.PageItem is not null)) imports.Add($"import '{pagination}';");
        if (slice.Operations.Any(o => Mentioned(o.Body).Concat(Mentioned(o.Result)).Any()))
            imports.Add($"import '{models}';");
        return imports;
    }

    /// <summary>An import of <c>orvano_core</c> when any of these types is a public model.</summary>
    private static List<string> UsesPublic(IEnumerable<TypeRef?> types, HashSet<string> testTypes) =>
        types.SelectMany(Mentioned).Any(n => !testTypes.Contains(n)) ? [$"import '{Core}';"] : [];

    private static IEnumerable<string> Mentioned(TypeRef? type) => type switch
    {
        ModelType m => [m.Name],
        EnumType e => [e.Name],
        ArrayType a => Mentioned(a.Item),
        MapType m => Mentioned(m.Value),
        _ => [],
    };

    private static object Models(ApiContract slice, CodeCatalog? catalog, List<string> imports) => new
    {
        Imports = imports,
        Catalog = catalog,
        Enums = slice.Enums.Select(e => new EnumDef(
            Doc(e.Doc, ""),
            e.Name,
            [.. e.Values.Select(v => new EnumMember(Naming.MemberFromWire(v), Naming.QuotedString(v)))])).ToList(),
        Models = slice.Models.Select(ToModel).ToList(),
    };

    private static CodeCatalog ErrorCodes(ApiContract contract, bool test) => new(
        Doc(test ? "Error codes only the test operations send." : "Every stable error code the server sends as `OrvanoException.code`.", ""),
        test ? "TestErrorCode" : "ErrorCode",
        [.. contract.ErrorCodes.Where(c => c.Test == test).Select(c => new Constant($"  /// `{c.Code}`\n", Naming.MemberFromWire(c.Code), Naming.QuotedString(c.Code)))]);

    private static object Events(ApiContract slice, string name, string[] imports)
    {
        var events = slice.Models.Where(m => m.Event is not null).OrderBy(m => m.Event, StringComparer.Ordinal)
            .Select(m => new Event(Naming.QuotedString(m.Event!), m.Name)).ToList();
        return new
        {
            Name = name,
            // With no events, only the typedef is used; importing models too would be unused.
            Imports = events.Count == 0 ? imports.Take(1).ToList() : [.. imports],
            Events = events,
        };
    }

    private static Model ToModel(ContractModel m)
    {
        var fields = m.Properties.Select(p =>
        {
            var nullable = p.Optional || p.Nullable;
            var type = Type(p.Type) + (nullable ? "?" : "");
            var source = $"json['{p.Name}']";
            var fromJson = nullable ? $"{source} == null ? null : {FromJson(p.Type, source)}" : FromJson(p.Type, source);
            // An optional value that needs no conversion uses a null aware element (`'x': ?x`);
            // `--fatal-infos` rejects the `if (x case final v?)` form for it (use_null_aware_elements).
            var toJson = p.Optional
                ? ToJson(p.Type, "v") == "v"
                    ? $"'{p.Name}': ?{p.Name}"
                    : $"if ({p.Name} case final v?) '{p.Name}': {ToJson(p.Type, "v")}"
                : p.Nullable && ToJson(p.Type, "v") != "v"
                    ? $"'{p.Name}': switch ({p.Name}) {{ final v? => {ToJson(p.Type, "v")}, null => null }}"
                    : $"'{p.Name}': {ToJson(p.Type, p.Name)}";
            return new Field(
                Doc(p.Doc, "  "),
                $"final {type} {p.Name};",
                nullable ? $"this.{p.Name}" : $"required this.{p.Name}",
                $"{p.Name}: {fromJson}",
                toJson);
        }).ToList();
        return new Model(Doc(m.Doc, ""), m.Name, fields);
    }

    private static List<Service> Services(ApiContract slice) =>
        [.. slice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            s.Service,
            s.Service,
            [.. s.Operations.SelectMany(ToOperations)]))];

    private static IEnumerable<Operation> ToOperations(ContractOperation op)
    {
        var call = new List<string> { Naming.QuotedString(op.HttpMethod), Path(op) };
        var query = op.Params.Where(p => p.In == ParamLocation.Query).ToList();
        if (query.Count > 0)
            call.Add("query: {" + string.Join(", ", query.Select(q => $"'{q.Name}': {QueryValue(q)}")) + "}");
        if (op.Body is not null) call.Add("body: body.toJson()");
        if (op.Idempotent) call.Add("idempotent: true");
        call.Add("options: options");

        var result = op.Result is null ? "void" : Type(op.Result);
        var body = op.Result is null
            ? $"async {{\n    await _client.send({string.Join(", ", call)});\n  }}"
            : $"async {{\n    final json = await _client.send({string.Join(", ", call)});\n    return {FromJson(op.Result, "json")};\n  }}";
        yield return new Operation(Doc(op.Doc, "  "), $"Future<{result}> {op.Name}({Params(op, withCursor: true)})", body);

        if (op.PageItem is null) yield break;
        var args = op.Params.Where(p => p.In == ParamLocation.Path).Select(p => p.Name)
            .Concat(op.Params.Where(p => p.In == ParamLocation.Query).Select(p => $"{p.Name}: {p.Name}"))
            .Append("options: options");
        yield return new Operation(
            Doc($"Every item of [{op.Name}], walking all pages: `await for (final item in ...)`.", "  "),
            $"Stream<{Type(op.PageItem)}> {op.Name}All({Params(op, withCursor: false)})",
            $"=> paginate(\n    (cursor) => {op.Name}({string.Join(", ", args)}),\n    (page) => (page.items, page.nextCursor),\n  );");
    }

    private static string Params(ContractOperation op, bool withCursor)
    {
        var positional = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) positional.Add($"{Type(p.Type)} {p.Name}");
        if (op.Body is not null) positional.Add($"{op.Body.Name} body");
        var named = op.Params.Where(p => p.In == ParamLocation.Query && (withCursor || p.Name != "cursor"))
            .Select(q => q.Required ? $"required {Type(q.Type)} {q.Name}" : $"{Type(q.Type)}? {q.Name}")
            .Append("RequestOptions? options");
        return string.Join(", ", positional) + (positional.Count > 0 ? ", " : "") + "{" + string.Join(", ", named) + "}";
    }

    private static string QueryValue(ContractParam q) => q.Type.Kind switch
    {
        PrimitiveKind.String => q.Name,
        PrimitiveKind.DateTime => $"{q.Name}{(q.Required ? "" : "?")}.toUtc().toIso8601String()",
        _ => $"{q.Name}{(q.Required ? "" : "?")}.toString()",
    };

    private static string Path(ContractOperation op)
    {
        var path = op.Path;
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path))
            path = path.Replace("{" + p.Name + "}", "${Uri.encodeComponent(" + (p.Type.Kind == PrimitiveKind.String ? p.Name : p.Name + ".toString()") + ")}", StringComparison.Ordinal);
        return "'" + path + "'";
    }

    /// <summary>The dispatch call: reads arguments from the scenario input and returns JSON.</summary>
    private static string Call(ContractOperation op)
    {
        var invoke = $"o.{op.Service}.{op.Name}({Args(op, withCursor: true)})";
        return op.Result is null
            ? $"(o, input) async {{ await {invoke}; return null; }}"
            : $"(o, input) async {{ final r = await {invoke}; return {ToJson(op.Result, "r")}; }}";
    }

    private static string CallAll(ContractOperation op) =>
        $"(o, input) => o.{op.Service}.{op.Name}All({Args(op, withCursor: false)}).map((e) => {ToJson(op.PageItem!, "e")})";

    private static string Args(ContractOperation op, bool withCursor)
    {
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) args.Add(FromJson(p.Type, $"input['{p.Name}']"));
        if (op.Body is not null) args.Add($"{op.Body.Name}.fromJson(input['body'] as Map<String, dynamic>)");
        foreach (var q in op.Params.Where(p => p.In == ParamLocation.Query && (withCursor || p.Name != "cursor")))
        {
            var source = $"input['{q.Name}']";
            args.Add($"{q.Name}: " + (q.Required ? FromJson(q.Type, source) : $"{source} == null ? null : {FromJson(q.Type, source)}"));
        }

        return string.Join(", ", args);
    }

    public static string Type(TypeRef type) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => "String",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => "DateTime",
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Int64 } => "int",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => "double",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => "bool",
        ArrayType a => $"List<{Type(a.Item)}>",
        MapType m => $"Map<String, {Type(m.Value)}>",
        ModelType m => m.Name,
        EnumType e => e.Name,
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    /// <summary>An expression that decodes the JSON value <paramref name="source"/> (never null).</summary>
    private static string FromJson(TypeRef type, string source) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => $"{source} as String",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => $"DateTime.parse({source} as String)",
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Int64 } => $"({source} as num).toInt()",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => $"({source} as num).toDouble()",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => $"{source} as bool",
        ArrayType a => $"({source} as List<dynamic>).map((e) => {FromJson(a.Item, "e")}).toList()",
        MapType m => $"({source} as Map<String, dynamic>).map((k, v) => MapEntry(k, {FromJson(m.Value, "v")}))",
        ModelType m => $"{m.Name}.fromJson({source} as Map<String, dynamic>)",
        EnumType e => $"{e.Name}.fromJson({source} as String)",
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    /// <summary>An expression that encodes the Dart value <paramref name="value"/> (never null) as JSON.</summary>
    private static string ToJson(TypeRef type, string value) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.DateTime } => $"{value}.toUtc().toIso8601String()",
        PrimitiveType => value,
        ArrayType a => ToJson(a.Item, "e") == "e" ? value : $"{value}.map((e) => {ToJson(a.Item, "e")}).toList()",
        MapType m => ToJson(m.Value, "v") == "v" ? value : $"{value}.map((k, v) => MapEntry(k, {ToJson(m.Value, "v")}))",
        ModelType => $"{value}.toJson()",
        EnumType => $"{value}.value",
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    private static string Doc(string? doc, string indent) =>
        string.Concat(Naming.DocLines(doc).Select(l => $"{indent}/// {l}\n"));
}
