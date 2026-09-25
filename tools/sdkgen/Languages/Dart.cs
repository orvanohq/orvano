using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// <c>orvano_core</c> (models, client and both services), <c>orvano_dart</c> (server services),
/// and the Dart scenario dispatch table. JSON mapping is written out by hand style, no build_runner.
/// </summary>
internal static class Dart
{
    public sealed record Field(string Doc, string Declaration, string Param, string FromJson, string ToJson);

    public sealed record Model(string Doc, string Name, IReadOnlyList<Field> Fields);

    public sealed record EnumMember(string Name, string Wire);

    public sealed record EnumDef(string Doc, string Name, IReadOnlyList<EnumMember> Members);

    public sealed record Operation(string Doc, string Name, string Params, string Result, string Call, string Return);

    public sealed record Service(string ClassName, string Property, string Name, IReadOnlyList<Operation> Operations);

    public sealed record DispatchEntry(string Id, int Status, string? Client, string? Server);

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var publicSlice = contract.Slice(a => a != Audience.Console);
        var coreSlice = contract.Slice(a => a is Audience.Client or Audience.Both);
        var serverOnly = contract.Slice(a => a is Audience.Server);
        var serverAll = contract.Slice(a => a is Audience.Server or Audience.Both);

        var models = new
        {
            Enums = publicSlice.Enums.Select(e => new EnumDef(
                Doc(e.Doc, ""),
                e.Name,
                [.. e.Values.Select(v => new EnumMember(Naming.MemberFromWire(v), Naming.QuotedString(v)))])).ToList(),
            Models = publicSlice.Models.Select(ToModel).ToList(),
        };

        var coreServices = Services(coreSlice, "");
        yield return new GeneratedOutput("Dart core", "sdks/dart/core/lib/src/generated", Formatter.Dart,
        [
            new("sdks/dart/core/lib/src/generated/models.dart", renderer.Render("dart", "models", models)),
            new("sdks/dart/core/lib/src/generated/services.dart", renderer.Render("dart", "services", new
            {
                Imports = new[] { "../client.dart", "models.dart" },
                Services = coreServices,
                Aggregate = coreServices,
            })),
            new("sdks/dart/core/lib/src/generated/version.dart", renderer.Render("dart", "version", new { contract.Version })),
        ]);

        // The server package reuses core's `both` services and hides core's client only ones.
        var clientOnly = contract.Slice(a => a is Audience.Client).Services.Select(s => Naming.Pascal(s.Service) + "Service");
        yield return new GeneratedOutput("Dart server", "sdks/dart/server/lib/src/generated", Formatter.Dart,
        [
            new("sdks/dart/server/lib/src/generated/services.dart", renderer.Render("dart", "services", new
            {
                Imports = new[] { "package:orvano_core/orvano_core.dart" },
                Services = Services(serverOnly, ""),
                Aggregate = Services(serverAll, ""),
            })),
            new("sdks/dart/server/lib/src/generated/core_exports.dart", renderer.Render("dart", "core_exports", new
            {
                Hidden = string.Join(", ", new[] { "Orvano" }.Concat(clientOnly).Order(StringComparer.Ordinal)),
            })),
        ]);

        var dispatch = contract.Operations.Select(o => new DispatchEntry(
            o.Id,
            o.SuccessStatus,
            o.Audience is Audience.Client or Audience.Both ? Call(o) : null,
            o.Audience is Audience.Server or Audience.Both ? Call(o) : null)).ToList();
        yield return new GeneratedOutput("Dart scenario dispatch", "tests/scenarios/runners/dart/lib/src/generated", Formatter.Dart,
        [
            new("tests/scenarios/runners/dart/lib/src/generated/dispatch.dart", renderer.Render("dart", "dispatch", new
            {
                Entries = dispatch,
                UsesCore = dispatch.Any(d => $"{d.Client}{d.Server}".Contains("core.", StringComparison.Ordinal)),
            })),
        ]);
    }

    private static Model ToModel(ContractModel m)
    {
        // Required parameters first keeps the constructor readable; order on the wire is irrelevant.
        var fields = m.Properties.Select(p =>
        {
            var nullable = p.Optional || p.Nullable;
            var type = Type(p.Type) + (nullable ? "?" : "");
            var source = $"json['{p.Name}']";
            var fromJson = nullable ? $"{source} == null ? null : {FromJson(p.Type, source)}" : FromJson(p.Type, source);
            var toJson = p.Optional
                ? $"if ({p.Name} case final v?) '{p.Name}': {ToJson(p.Type, "v")}"
                : p.Nullable
                    ? $"'{p.Name}': {p.Name} == null ? null : {ToJson(p.Type, p.Name + "!")}"
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

    private static List<Service> Services(ApiContract slice, string prefix) =>
        [.. slice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            s.Service,
            s.Service,
            [.. s.Operations.Select(o => ToOperation(o, prefix))]))];

    private static Operation ToOperation(ContractOperation op, string prefix)
    {
        var positional = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) positional.Add($"{Type(p.Type)} {p.Name}");
        if (op.Body is not null) positional.Add($"{prefix}{op.Body.Name} body");
        var query = op.Params.Where(p => p.In == ParamLocation.Query).ToList();
        var named = query.Select(q => q.Required ? $"required {Type(q.Type)} {q.Name}" : $"{Type(q.Type)}? {q.Name}").ToList();
        var parameters = string.Join(", ", positional) + (named.Count > 0 ? (positional.Count > 0 ? ", " : "") + "{" + string.Join(", ", named) + "}" : "");

        var call = new List<string> { Naming.QuotedString(op.HttpMethod), Path(op) };
        if (query.Count > 0)
            call.Add("query: {" + string.Join(", ", query.Select(q => $"'{q.Name}': {QueryValue(q)}")) + "}");
        if (op.Body is not null) call.Add("body: body.toJson()");
        if (op.Idempotent) call.Add("idempotent: true");

        var result = op.Result is null ? "void" : Type(op.Result, prefix);
        return new Operation(
            Doc(op.Doc, "  "),
            op.Name,
            parameters,
            result,
            string.Join(", ", call),
            op.Result is null ? "" : FromJson(op.Result, "json", prefix));
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
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) args.Add(FromJson(p.Type, $"input['{p.Name}']", "core."));
        if (op.Body is not null) args.Add($"core.{op.Body.Name}.fromJson(input['body'] as Map<String, dynamic>)");
        foreach (var q in op.Params.Where(p => p.In == ParamLocation.Query))
        {
            var source = $"input['{q.Name}']";
            args.Add($"{q.Name}: " + (q.Required ? FromJson(q.Type, source, "core.") : $"{source} == null ? null : {FromJson(q.Type, source, "core.")}"));
        }

        var invoke = $"o.{op.Service}.{op.Name}({string.Join(", ", args)})";
        return op.Result is null
            ? $"(o, input) async {{ await {invoke}; return null; }}"
            : $"(o, input) async {{ final r = await {invoke}; return {ToJson(op.Result, "r")}; }}";
    }

    public static string Type(TypeRef type, string prefix = "") => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => "String",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => "DateTime",
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Int64 } => "int",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => "double",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => "bool",
        ArrayType a => $"List<{Type(a.Item, prefix)}>",
        MapType m => $"Map<String, {Type(m.Value, prefix)}>",
        ModelType m => prefix + m.Name,
        EnumType e => prefix + e.Name,
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    /// <summary>An expression that decodes the JSON value <paramref name="source"/> (never null).</summary>
    private static string FromJson(TypeRef type, string source, string prefix = "") => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => $"{source} as String",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => $"DateTime.parse({source} as String)",
        PrimitiveType { Kind: PrimitiveKind.Int32 or PrimitiveKind.Int64 } => $"({source} as num).toInt()",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => $"({source} as num).toDouble()",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => $"{source} as bool",
        ArrayType a => $"({source} as List<dynamic>).map((e) => {FromJson(a.Item, "e", prefix)}).toList()",
        MapType m => $"({source} as Map<String, dynamic>).map((k, v) => MapEntry(k, {FromJson(m.Value, "v", prefix)}))",
        ModelType m => $"{prefix}{m.Name}.fromJson({source} as Map<String, dynamic>)",
        EnumType e => $"{prefix}{e.Name}.fromJson({source} as String)",
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
