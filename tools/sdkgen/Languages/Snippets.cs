using System.Globalization;
using System.Text.Json.Nodes;
using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// Docs snippets (AC-15): one short program per public operation per SDK that carries it, in
/// <c>contract/dist/examples/&lt;sdk&gt;/&lt;operationId&gt;.&lt;ext&gt;</c>. <c>console</c> and test
/// operations get none (AC-17, AC-18). Argument values come from the contract's <c>@example</c>
/// values, else a placeholder by type.
/// </summary>
internal static class Snippets
{
    public const string Directory = "contract/dist/examples";

    /// <summary>
    /// One snippet's moving parts; the template adds the setup around them. <c>Model</c>: the result
    /// is a model, so Dart prints its <c>toJson()</c>.
    /// </summary>
    public sealed record Snippet(string Call, string Result, bool Model, bool Server);

    private sealed record Sdk(string Name, string Extension, Formatter Formatter, Func<ContractOperation, bool> Carries, Func<ContractOperation, ApiContract, string> Call, Func<ContractOperation, bool> Server);

    private static readonly Sdk[] Sdks =
    [
        new("js", "ts", Formatter.Prettier, _ => true, TsCall, o => !o.IsClient),
        new("nextjs", "tsx", Formatter.Prettier, o => o.IsClient, TsCall, _ => false),
        new("flutter", "dart", Formatter.Dart, o => o.IsClient, DartCall, _ => false),
        new("dart", "dart", Formatter.Dart, o => o.IsServer, DartCall, _ => true),
        new("dotnet", "cs", Formatter.CSharp, o => o.IsServer, CsCall, _ => true),
    ];

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var operations = contract.Operations.Where(o => !o.Test && o.Audience != Audience.Console).ToList();
        foreach (var sdk in Sdks)
        {
            var dir = $"{Directory}/{sdk.Name}";
            yield return new GeneratedOutput($"Docs snippets, {sdk.Name}", dir, sdk.Formatter,
            [
                .. operations.Where(sdk.Carries).Select(o => new GeneratedFile(
                    $"{dir}/{o.Id}.{sdk.Extension}",
                    renderer.Render("snippets", sdk.Name, new Snippet(sdk.Call(o, contract), ResultName(o), o.Result is ModelType, sdk.Server(o))))),
            ]);
        }
    }

    /// <summary>The variable a result goes in: the result model's name in camelCase, else <c>result</c>; empty for no content.</summary>
    private static string ResultName(ContractOperation op) => op.Result switch
    {
        null => "",
        ModelType m => Naming.Camel(m.Name),
        _ => "result",
    };

    private static IEnumerable<ContractParam> PathParams(ContractOperation op) => op.Params.Where(p => p.In == ParamLocation.Path);

    private static IEnumerable<ContractParam> RequiredQuery(ContractOperation op) =>
        op.Params.Where(p => p.In == ParamLocation.Query && p.Required);

    // ---- TypeScript: orvano.service.op(path..., body, { query }) ----

    private static string TsCall(ContractOperation op, ApiContract contract)
    {
        var args = PathParams(op).Select(p => TsValue(p.Type, p.Example, p.Name, contract)).ToList();
        if (op.Body is not null) args.Add(TsValue(op.Body, null, "body", contract));
        var query = RequiredQuery(op).Select(p => $"{p.Name}: {TsValue(p.Type, p.Example, p.Name, contract)}").ToList();
        if (query.Count > 0) args.Add("{ " + string.Join(", ", query) + " }");
        return $"orvano.{op.Service}.{op.Name}({string.Join(", ", args)})";
    }

    private static string TsValue(TypeRef type, JsonNode? example, string name, ApiContract contract) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => Naming.QuotedString(Text(example) ?? $"<{name}>"),
        PrimitiveType { Kind: PrimitiveKind.DateTime } => Naming.QuotedString(Date(example).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        PrimitiveType { Kind: PrimitiveKind.Boolean } => Bool(example) ? "true" : "false",
        PrimitiveType => Number(example),
        EnumType e => Naming.QuotedString(EnumValue(e, example, contract)),
        ArrayType a => $"[{TsValue(a.Item, (example as JsonArray)?.FirstOrDefault(), name, contract)}]",
        MapType m => $"{{ key: {TsValue(m.Value, null, name, contract)} }}",
        ModelType m => "{ " + string.Join(", ", Required(m, contract).Select(p => $"{p.Name}: {TsValue(p.Type, p.Example, p.Name, contract)}")) + " }",
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    // ---- Dart: orvano.service.op(path..., body, query: ...) ----

    private static string DartCall(ContractOperation op, ApiContract contract)
    {
        var args = PathParams(op).Select(p => DartValue(p.Type, p.Example, p.Name, contract)).ToList();
        if (op.Body is not null) args.Add(DartValue(op.Body, null, "body", contract));
        args.AddRange(RequiredQuery(op).Select(p => $"{p.Name}: {DartValue(p.Type, p.Example, p.Name, contract)}"));
        return $"orvano.{op.Service}.{op.Name}({string.Join(", ", args)})";
    }

    private static string DartValue(TypeRef type, JsonNode? example, string name, ApiContract contract) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => Naming.QuotedString(Text(example) ?? $"<{name}>"),
        PrimitiveType { Kind: PrimitiveKind.DateTime } => DartDate(Date(example)),
        PrimitiveType { Kind: PrimitiveKind.Boolean } => Bool(example) ? "true" : "false",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => Number(example, fraction: true),
        PrimitiveType => Number(example),
        EnumType e => $"{e.Name}.{Naming.MemberFromWire(EnumValue(e, example, contract))}",
        ArrayType a => $"[{DartValue(a.Item, (example as JsonArray)?.FirstOrDefault(), name, contract)}]",
        MapType m => $"{{'key': {DartValue(m.Value, null, name, contract)}}}",
        ModelType m => $"{m.Name}(" + string.Join(", ", Required(m, contract).Select(p => $"{p.Name}: {DartValue(p.Type, p.Example, p.Name, contract)}")) + ")",
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    private static string DartDate(DateTimeOffset d) =>
        d.TimeOfDay == TimeSpan.Zero
            ? $"DateTime.utc({d.Year}, {d.Month}, {d.Day})"
            : $"DateTime.utc({d.Year}, {d.Month}, {d.Day}, {d.Hour}, {d.Minute}, {d.Second})";

    // ---- C#: orvano.Service.OpAsync(path..., requiredQuery..., body) ----

    private static string CsCall(ContractOperation op, ApiContract contract)
    {
        var args = PathParams(op).Concat(RequiredQuery(op)).Select(p => CsValue(p.Type, p.Example, p.Name, contract)).ToList();
        // dotnet format does not wrap lines, so the body gets one property per line here.
        if (op.Body is not null)
        {
            args.Add($"new {op.Body.Name}(\n" + string.Join(",\n", Required(op.Body, contract)
                .Select(p => $"    {Naming.Pascal(p.Name)}: {CsValue(p.Type, p.Example, p.Name, contract)}")) + ")");
        }

        return $"orvano.{Naming.Pascal(op.Service)}.{Naming.Pascal(op.Name)}Async({string.Join(", ", args)})";
    }

    private static string CsValue(TypeRef type, JsonNode? example, string name, ApiContract contract) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => Naming.CsString(Text(example) ?? $"<{name}>"),
        PrimitiveType { Kind: PrimitiveKind.DateTime } => CsDate(Date(example)),
        PrimitiveType { Kind: PrimitiveKind.Boolean } => Bool(example) ? "true" : "false",
        PrimitiveType { Kind: PrimitiveKind.Int64 } => Number(example) + "L",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => Number(example, fraction: true),
        PrimitiveType => Number(example),
        EnumType e => $"{e.Name}.{Naming.Pascal(Naming.MemberFromWire(EnumValue(e, example, contract)))}",
        ArrayType a => $"[{CsValue(a.Item, (example as JsonArray)?.FirstOrDefault(), name, contract)}]",
        MapType m => $"new Dictionary<string, {CSharp.Type(m.Value)}> {{ [\"key\"] = {CsValue(m.Value, null, name, contract)} }}",
        ModelType m => $"new {m.Name}(" + string.Join(", ", Required(m, contract).Select(p => $"{Naming.Pascal(p.Name)}: {CsValue(p.Type, p.Example, p.Name, contract)}")) + ")",
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    private static string CsDate(DateTimeOffset d) =>
        $"new DateTimeOffset({d.Year}, {d.Month}, {d.Day}, {d.Hour}, {d.Minute}, {d.Second}, TimeSpan.Zero)";

    // ---- Example values, else placeholders ----

    /// <summary>A body model's required properties: what a snippet must set.</summary>
    private static IEnumerable<ContractProperty> Required(ModelType model, ApiContract contract) =>
        contract.Models.First(m => m.Name == model.Name).Properties.Where(p => !p.Optional);

    private static string? Text(JsonNode? example) =>
        example is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Bool(JsonNode? example) =>
        example is not JsonValue v || !v.TryGetValue<bool>(out var b) || b;

    private static string Number(JsonNode? example, bool fraction = false)
    {
        var value = example is JsonValue v && v.TryGetValue<double>(out var d) ? d : 1;
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return fraction && !text.Contains('.', StringComparison.Ordinal) && !text.Contains('E', StringComparison.Ordinal) ? text + ".0" : text;
    }

    private static DateTimeOffset Date(JsonNode? example) =>
        Text(example) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUniversalTime()
            : new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static string EnumValue(EnumType type, JsonNode? example, ApiContract contract) =>
        Text(example) ?? contract.Enums.First(e => e.Name == type.Name).Values[0];
}
