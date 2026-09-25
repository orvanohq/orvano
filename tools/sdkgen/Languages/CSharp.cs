using System.Text.RegularExpressions;
using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// The .NET SDK (<c>Orvano</c>, server and both operations), the server's <c>Orvano.Contract</c>
/// types (every audience), and the .NET scenario dispatch table.
/// </summary>
internal static partial class CSharp
{
    public sealed record Parameter(string Declaration, string Doc);

    public sealed record Model(string Summary, string Name, IReadOnlyList<Parameter> Parameters);

    public sealed record EnumMember(string Name, string Wire, string Summary);

    public sealed record EnumDef(string Summary, string Name, IReadOnlyList<EnumMember> Members);

    public sealed record Operation(string Summary, IReadOnlyList<string> ParamDocs, string Name, string Params, string Result, string Body);

    public sealed record Service(string ClassName, string Property, string Field, string Name, IReadOnlyList<Operation> Operations);

    public sealed record OperationConstants(string ClassName, string Summary, string Id, string Method, string Route, string Audience);

    public sealed record ServiceConstants(string ClassName, string Name, IReadOnlyList<OperationConstants> Operations);

    public sealed record DispatchEntry(string Id, int Status, string? Call);

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var sdkSlice = contract.Slice(a => a is Audience.Server or Audience.Both);
        var sdkTypes = Types(sdkSlice, "Orvano", "public");
        var services = sdkSlice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            Naming.Pascal(s.Service),
            "_" + s.Service,
            s.Service,
            [.. s.Operations.Select(ToOperation)])).ToList();

        yield return new GeneratedOutput(".NET SDK", "sdks/dotnet/src/Orvano/Generated", Formatter.CSharp,
        [
            new("sdks/dotnet/src/Orvano/Generated/Models.cs", renderer.Render("csharp", "models", sdkTypes)),
            new("sdks/dotnet/src/Orvano/Generated/Services.cs", renderer.Render("csharp", "services", new
            {
                Services = services,
                UsesGlobalization = services.Any(s => s.Operations.Any(o => o.Body.Contains("CultureInfo", StringComparison.Ordinal))),
            })),
            new("sdks/dotnet/src/Orvano/Generated/OrvanoJsonContext.cs", renderer.Render("csharp", "json_context", new
            {
                Types = SerializableTypes(sdkSlice),
            })),
            new("sdks/dotnet/src/Orvano/Generated/SdkInfo.cs", renderer.Render("csharp", "sdk_info", new { contract.Version })),
        ]);

        var serverServices = contract.Services.Select(s => new ServiceConstants(
            Naming.Pascal(s.Service) + "Operations",
            s.Service,
            [.. s.Operations.Select(o => new OperationConstants(
                Naming.Pascal(o.Name),
                Xml($"{o.HttpMethod} {o.Path}: {Naming.DocLines(o.Doc).FirstOrDefault() ?? o.Id}"),
                Naming.CsString(o.Id),
                Naming.CsString(o.HttpMethod),
                Naming.CsString(o.Path["/v1".Length..]),
                Naming.CsString(o.Audience.ToString().ToLowerInvariant())))])).ToList();

        yield return new GeneratedOutput("Server contract types", "server/src/Orvano.Contract/Generated", Formatter.CSharp,
        [
            new("server/src/Orvano.Contract/Generated/Models.cs", renderer.Render("csharp", "models", Types(contract, "Orvano.Contract", "public"))),
            new("server/src/Orvano.Contract/Generated/Operations.cs", renderer.Render("csharp", "operations", new { Services = serverServices })),
        ]);

        var dispatch = contract.Operations.Select(o => new DispatchEntry(
            o.Id, o.SuccessStatus, o.Audience is Audience.Server or Audience.Both ? Call(o) : null)).ToList();
        yield return new GeneratedOutput(".NET scenario dispatch", "tests/scenarios/runners/dotnet/Generated", Formatter.CSharp,
        [
            new("tests/scenarios/runners/dotnet/Generated/Dispatch.cs", renderer.Render("csharp", "dispatch", new { Entries = dispatch })),
        ]);
    }

    private static object Types(ApiContract slice, string ns, string visibility) => new
    {
        Namespace = ns,
        Visibility = visibility,
        Enums = slice.Enums.Select(e => new EnumDef(
            Xml(Summary(e.Doc, e.Name)),
            e.Name,
            [.. e.Values.Select(v => new EnumMember(Naming.Pascal(Naming.MemberFromWire(v)), Naming.CsString(v), Xml($"The wire value <c>{v}</c>.")))])).ToList(),
        Models = slice.Models.Select(m => new Model(
            Xml(Summary(m.Doc, m.Name)),
            m.Name,
            // Optional properties have a default, so they go last.
            [.. m.Properties.OrderBy(p => p.Optional).Select(p =>
            {
                var type = Type(p.Type) + (p.Optional || p.Nullable ? "?" : "");
                var attributes = $"[property: JsonPropertyName({Naming.CsString(p.Name)})"
                    + (p.Optional ? ", JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]" : "]");
                return new Parameter(
                    $"{attributes} {type} {Naming.Pascal(p.Name)}{(p.Optional ? " = null" : "")}",
                    $"<param name=\"{Naming.Pascal(p.Name)}\">{Xml(Summary(p.Doc, p.Name))}</param>");
            })])).ToList(),
    };

    private static List<string> SerializableTypes(ApiContract slice)
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in slice.Models) types.Add(m.Name);
        foreach (var e in slice.Enums) types.Add(e.Name);
        foreach (var op in slice.Operations)
            if (op.Result is ArrayType or MapType) types.Add(Type(op.Result));
        return [.. types];
    }

    private static Operation ToOperation(ContractOperation op)
    {
        var parameters = new List<string>();
        var docs = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path).Concat(op.Params.Where(p => p.In == ParamLocation.Query && p.Required)))
        {
            parameters.Add($"{Type(p.Type)} {p.Name}");
            docs.Add($"<param name=\"{p.Name}\">{Xml(Summary(p.Doc, p.Name))}</param>");
        }

        if (op.Body is not null)
        {
            parameters.Add($"{op.Body.Name} body");
            docs.Add("<param name=\"body\">The request body.</param>");
        }

        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Query && !p.Required))
        {
            parameters.Add($"{Type(p.Type)}? {p.Name} = null");
            docs.Add($"<param name=\"{p.Name}\">{Xml(Summary(p.Doc, p.Name))}</param>");
        }

        parameters.Add("CancellationToken cancellationToken = default");
        docs.Add("<param name=\"cancellationToken\">Cancels the request.</param>");

        var path = op.Path;
        var interpolated = false;
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path))
        {
            interpolated = true;
            path = path.Replace("{" + p.Name + "}", "{Uri.EscapeDataString(" + QueryText(p) + ")}", StringComparison.Ordinal);
        }

        var request = new List<string> { Naming.CsString(op.HttpMethod), (interpolated ? "$" : "") + Naming.CsString(path) };
        var query = op.Params.Where(p => p.In == ParamLocation.Query).ToList();
        request.Add(query.Count == 0
            ? "null"
            : "[" + string.Join(", ", query.Select(q => $"new({Naming.CsString(q.Name)}, {(q.Required ? QueryText(q) : $"{q.Name} is null ? null : {QueryText(q, ".Value")}")})")) + "]");
        request.Add(op.Body is null ? "null" : $"OrvanoRequest.Json(body, OrvanoJsonContext.Default.{op.Body.Name})");
        request.Add(op.Idempotent ? "true" : "false");

        var send = op.Result is null
            ? $"_client.SendAsync(new OrvanoRequest({string.Join(", ", request)}), cancellationToken)"
            : $"_client.SendAsync(new OrvanoRequest({string.Join(", ", request)}), OrvanoJsonContext.Default.{JsonInfoName(op.Result)}, cancellationToken)";

        return new Operation(
            Xml(Summary(op.Doc, op.Id)),
            docs,
            Naming.Pascal(op.Name) + "Async",
            string.Join(", ", parameters),
            op.Result is null ? "Task" : $"Task<{Type(op.Result)}>",
            send);
    }

    private static string QueryText(ContractParam p, string suffix = "") => p.Type.Kind switch
    {
        PrimitiveKind.String => p.Name,
        PrimitiveKind.Boolean => $"{p.Name}{suffix} ? \"true\" : \"false\"",
        PrimitiveKind.DateTime => $"{p.Name}{suffix}.ToUniversalTime().ToString(\"O\", CultureInfo.InvariantCulture)",
        _ => $"{p.Name}{suffix}.ToString(CultureInfo.InvariantCulture)",
    };

    /// <summary>The dispatch call: reads arguments from the scenario input and returns JSON.</summary>
    private static string Call(ContractOperation op)
    {
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path).Concat(op.Params.Where(p => p.In == ParamLocation.Query && p.Required)))
            args.Add($"Args.Required<{Type(p.Type)}>(input, {Naming.CsString(p.Name)})");
        if (op.Body is not null) args.Add($"Args.Required<{op.Body.Name}>(input, \"body\")");
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Query && !p.Required))
            args.Add($"{p.Name}: Args.Optional<{Type(p.Type)}>(input, {Naming.CsString(p.Name)})");
        args.Add("ct");
        var invoke = $"client.{Naming.Pascal(op.Service)}.{Naming.Pascal(op.Name)}Async({string.Join(", ", args)})";
        return op.Result is null
            ? $"async (client, input, ct) => {{ await {invoke}; return null; }}"
            : $"async (client, input, ct) => Args.ToJson(await {invoke})";
    }

    public static string Type(TypeRef type) => type switch
    {
        PrimitiveType { Kind: PrimitiveKind.String } => "string",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => "DateTimeOffset",
        PrimitiveType { Kind: PrimitiveKind.Int32 } => "int",
        PrimitiveType { Kind: PrimitiveKind.Int64 } => "long",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => "double",
        PrimitiveType { Kind: PrimitiveKind.Boolean } => "bool",
        ArrayType a => $"IReadOnlyList<{Type(a.Item)}>",
        MapType m => $"IReadOnlyDictionary<string, {Type(m.Value)}>",
        ModelType m => m.Name,
        EnumType e => e.Name,
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    /// <summary>The property name System.Text.Json's source generator gives a type's metadata.</summary>
    private static string JsonInfoName(TypeRef type) => type switch
    {
        ArrayType a => $"IReadOnlyList{JsonInfoName(a.Item)}",
        MapType m => $"IReadOnlyDictionaryString{JsonInfoName(m.Value)}",
        PrimitiveType { Kind: PrimitiveKind.Float64 } => "Double",
        PrimitiveType { Kind: PrimitiveKind.DateTime } => "DateTimeOffset",
        PrimitiveType p => p.Kind.ToString(),
        ModelType m => m.Name,
        EnumType e => e.Name,
        _ => throw new InvalidOperationException($"unmapped type {type}"),
    };

    private static string Summary(string? doc, string fallback) =>
        string.Join(" ", Naming.DocLines(doc)) is { Length: > 0 } s ? s : $"The <c>{fallback}</c> value.";

    /// <summary>Escapes text for an XML doc comment and turns `code` spans into &lt;c&gt; elements.</summary>
    private static string Xml(string text)
    {
        var escaped = text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("&lt;c&gt;", "<c>", StringComparison.Ordinal)
            .Replace("&lt;/c&gt;", "</c>", StringComparison.Ordinal);
        return CodeSpan().Replace(escaped, "<c>$1</c>");
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex CodeSpan();
}
