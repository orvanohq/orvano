using System.Text.RegularExpressions;
using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// The .NET SDK (<c>Orvano</c>, server and both operations), the server's <c>Orvano.Contract</c>
/// types (every audience, test code included), and the .NET runner's test code and dispatch table.
/// </summary>
internal static partial class CSharp
{
    public sealed record Parameter(string Declaration, string Doc);

    public sealed record Model(string Summary, string Name, IReadOnlyList<Parameter> Parameters);

    public sealed record EnumMember(string Name, string Wire, string Summary);

    public sealed record EnumDef(string Summary, string Name, IReadOnlyList<EnumMember> Members);

    public sealed record Constant(string Summary, string Name, string Value);

    public sealed record CodeCatalog(string Summary, string Name, IReadOnlyList<Constant> Codes);

    public sealed record Event(string Name, string Type);

    public sealed record Operation(string Summary, IReadOnlyList<string> ParamDocs, string Signature, string Body);

    public sealed record Service(string ClassName, string Property, string Field, string Name, IReadOnlyList<Operation> Operations);

    public sealed record OperationConstants(string ClassName, string Summary, string Id, string Method, string Route, string Audience);

    public sealed record ServiceConstants(string ClassName, string Name, IReadOnlyList<OperationConstants> Operations);

    public sealed record DispatchEntry(string Id, int Status, string? Call, string? Paginate);

    private const string SdkContext = "OrvanoJsonContext";
    private const string TestContext = "TestJsonContext";
    private const string RunnerNamespace = "Orvano.Scenarios.Generated";

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var sdkSlice = contract.Slice(o => !o.Test && o.IsServer, e => !e.Test);
        var services = ToServices(sdkSlice, SdkContext);

        yield return new GeneratedOutput(".NET SDK", "sdks/dotnet/src/Orvano/Generated", Formatter.CSharp,
        [
            new("sdks/dotnet/src/Orvano/Generated/Models.cs", renderer.Render("csharp", "models", Types(sdkSlice, "Orvano", "public"))),
            new("sdks/dotnet/src/Orvano/Generated/Services.cs", renderer.Render("csharp", "services", new
            {
                Namespace = "Orvano",
                Services = services,
                ConstructorVisibility = "internal",
                PartialClient = true,
                UsesGlobalization = UsesGlobalization(services),
            })),
            new("sdks/dotnet/src/Orvano/Generated/OrvanoJsonContext.cs", renderer.Render("csharp", "json_context", new
            {
                Namespace = "Orvano",
                Name = SdkContext,
                Summary = "Source generated JSON metadata for every contract type, used on every target framework.",
                Types = SerializableTypes(sdkSlice).Append("OrvanoProblem").ToList(),
            })),
            new("sdks/dotnet/src/Orvano/Generated/ErrorCodes.cs", renderer.Render("csharp", "error_codes", new
            {
                Namespace = "Orvano",
                Catalogs = new[] { ErrorCodes(contract, test: false) },
            })),
            new("sdks/dotnet/src/Orvano/Generated/Events.cs", renderer.Render("csharp", "events", new
            {
                Namespace = "Orvano",
                Declaration = "public static partial class OrvanoEvents",
                Events = Events(sdkSlice, SdkContext),
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
            new("server/src/Orvano.Contract/Generated/ErrorCodes.cs", renderer.Render("csharp", "error_codes", new
            {
                Namespace = "Orvano.Contract",
                Catalogs = new[] { ErrorCodes(contract, test: false), ErrorCodes(contract, test: true) },
            })),
        ]);

        // The .NET runner (AC-18): test code lives only here. Its services call the SDK's internal
        // SendAsync (InternalsVisibleTo) with the runner's own JSON context.
        var testSlice = contract.Slice(o => o.Test && o.IsServer, e => e.Test);
        var testTypes = testSlice with
        {
            Models = [.. testSlice.Models.Where(m => m.Test)],
            Enums = [.. testSlice.Enums.Where(e => e.Test)],
        };
        var testServices = ToServices(testSlice, TestContext);
        var dispatch = contract.Operations.Where(o => o.Audience != Audience.Console).Select(o => new DispatchEntry(
            o.Id,
            o.SuccessStatus,
            o.IsServer ? Call(o) : null,
            o.IsServer && o.PageItem is not null ? CallAll(o) : null)).ToList();

        yield return new GeneratedOutput(".NET scenario runner", "tests/scenarios/runners/dotnet/Generated", Formatter.CSharp,
        [
            new("tests/scenarios/runners/dotnet/Generated/TestModels.cs", renderer.Render("csharp", "models", Types(testTypes, RunnerNamespace, "public"))),
            new("tests/scenarios/runners/dotnet/Generated/TestServices.cs", renderer.Render("csharp", "services", new
            {
                Namespace = RunnerNamespace,
                Services = testServices,
                ConstructorVisibility = "public",
                PartialClient = false,
                UsesGlobalization = UsesGlobalization(testServices),
            })),
            new("tests/scenarios/runners/dotnet/Generated/TestJsonContext.cs", renderer.Render("csharp", "json_context", new
            {
                Namespace = RunnerNamespace,
                Name = TestContext,
                Summary = "Source generated JSON metadata for the test types the runner sends and decodes.",
                Types = SerializableTypes(testSlice),
            })),
            new("tests/scenarios/runners/dotnet/Generated/TestErrorCodes.cs", renderer.Render("csharp", "error_codes", new
            {
                Namespace = RunnerNamespace,
                Catalogs = new[] { ErrorCodes(contract, test: true) },
            })),
            new("tests/scenarios/runners/dotnet/Generated/TestEvents.cs", renderer.Render("csharp", "events", new
            {
                Namespace = RunnerNamespace,
                Declaration = "public static class TestEvents",
                Events = Events(testTypes, TestContext),
            })),
            new("tests/scenarios/runners/dotnet/Generated/Dispatch.cs", renderer.Render("csharp", "dispatch", new { Entries = dispatch })),
        ]);
    }

    private static bool UsesGlobalization(List<Service> services) =>
        services.Any(s => s.Operations.Any(o => o.Body.Contains("CultureInfo", StringComparison.Ordinal)));

    private static CodeCatalog ErrorCodes(ApiContract contract, bool test) => new(
        test ? "Error codes only the test operations send." : "Every stable error code the server sends in <c>Problem.code</c>.",
        test ? "TestErrorCode" : "ErrorCode",
        [.. contract.ErrorCodes.Where(c => c.Test == test).Select(c => new Constant(
            $"The <c>{c.Code}</c> error code.", Naming.Pascal(Naming.MemberFromWire(c.Code)), Naming.CsString(c.Code)))]);

    private static List<Event> Events(ApiContract slice, string context) =>
        [.. slice.Models.Where(m => m.Event is not null).OrderBy(m => m.Event, StringComparer.Ordinal)
            .Select(m => new Event(Naming.CsString(m.Event!), $"{context}.Default.{m.Name}"))];

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

    private static List<Service> ToServices(ApiContract slice, string context) =>
        [.. slice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            Naming.Pascal(s.Service),
            "_" + s.Service,
            s.Service,
            [.. s.Operations.SelectMany(o => ToOperations(o, context))]))];

    /// <summary>Path, required query, body, optional query, then the cancellation token.</summary>
    private static (List<string> Params, List<string> Docs) Parameters(ContractOperation op, bool withCursor)
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

        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Query && !p.Required && (withCursor || p.Name != "cursor")))
        {
            parameters.Add($"{Type(p.Type)}? {p.Name} = null");
            docs.Add($"<param name=\"{p.Name}\">{Xml(Summary(p.Doc, p.Name))}</param>");
        }

        parameters.Add("CancellationToken cancellationToken = default");
        docs.Add("<param name=\"cancellationToken\">Cancels the request.</param>");
        return (parameters, docs);
    }

    private static IEnumerable<Operation> ToOperations(ContractOperation op, string context)
    {
        var (parameters, docs) = Parameters(op, withCursor: true);

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
            : "[" + string.Join(", ", query.Select(q => $"new({Naming.CsString(q.Name)}, {(q.Required || q.Type.Kind == PrimitiveKind.String ? QueryText(q) : $"{q.Name} is null ? null : {QueryText(q, ".Value")}")})")) + "]");
        request.Add(op.Body is null ? "null" : $"OrvanoRequest.Json(body, {context}.Default.{op.Body.Name})");
        request.Add(op.Idempotent ? "true" : "false");

        var name = Naming.Pascal(op.Name);
        var send = op.Result is null
            ? $"_client.SendAsync(new OrvanoRequest({string.Join(", ", request)}), cancellationToken)"
            : $"_client.SendAsync(new OrvanoRequest({string.Join(", ", request)}), {context}.Default.{JsonInfoName(op.Result)}, cancellationToken)";
        yield return new Operation(
            Xml(Summary(op.Doc, op.Id)),
            docs,
            $"{(op.Result is null ? "Task" : $"Task<{Type(op.Result)}>")} {name}Async({string.Join(", ", parameters)})",
            send);

        if (op.PageItem is null) yield break;
        var (allParams, allDocs) = Parameters(op, withCursor: false);
        var args = op.Params.Where(p => p.In == ParamLocation.Path || p.Required).Select(p => $"{p.Name}: {p.Name}")
            .Concat(op.Body is null ? [] : ["body: body"])
            .Concat(op.Params.Where(p => p.In == ParamLocation.Query && !p.Required).Select(p => p.Name == "cursor" ? "cursor: cursor" : $"{p.Name}: {p.Name}"))
            .Append("cancellationToken: ct");
        yield return new Operation(
            Xml($"Every item of `{name}Async`, walking all pages: <c>await foreach</c>."),
            allDocs,
            $"IAsyncEnumerable<{Type(op.PageItem)}> {name}AllAsync({string.Join(", ", allParams)})",
            $"OrvanoPagination.IterateAsync((cursor, ct) => {name}Async({string.Join(", ", args)}), page => page.Items, page => page.NextCursor, cancellationToken)");
    }

    private static string QueryText(ContractParam p, string suffix = "") => p.Type.Kind switch
    {
        PrimitiveKind.String => p.Name,
        PrimitiveKind.Boolean => $"{p.Name}{suffix} ? \"true\" : \"false\"",
        PrimitiveKind.DateTime => $"{p.Name}{suffix}.ToUniversalTime().ToString(\"O\", CultureInfo.InvariantCulture)",
        _ => $"{p.Name}{suffix}.ToString(CultureInfo.InvariantCulture)",
    };

    /// <summary>The service a dispatch call goes through: the SDK's property, or the runner's test service.</summary>
    private static string ServiceOf(ContractOperation op) =>
        op.Test ? $"new {Naming.Pascal(op.Service)}Service(client)" : $"client.{Naming.Pascal(op.Service)}";

    private static string Args(ContractOperation op, bool withCursor)
    {
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path).Concat(op.Params.Where(p => p.In == ParamLocation.Query && p.Required)))
            args.Add($"Args.Required<{Type(p.Type)}>(input, {Naming.CsString(p.Name)})");
        if (op.Body is not null) args.Add($"Args.Required<{op.Body.Name}>(input, \"body\")");
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Query && !p.Required && (withCursor || p.Name != "cursor")))
            args.Add($"{p.Name}: Args.Optional<{Type(p.Type)}?>(input, {Naming.CsString(p.Name)})");
        args.Add("cancellationToken: ct");
        return string.Join(", ", args);
    }

    /// <summary>The dispatch call: reads arguments from the scenario input and returns JSON.</summary>
    private static string Call(ContractOperation op)
    {
        var invoke = $"{ServiceOf(op)}.{Naming.Pascal(op.Name)}Async({Args(op, withCursor: true)})";
        return op.Result is null
            ? $"async (client, input, ct) => {{ await {invoke}; return null; }}"
            : $"async (client, input, ct) => Args.ToJson(await {invoke})";
    }

    private static string CallAll(ContractOperation op) =>
        $"(client, input, ct) => Args.CollectAsync({ServiceOf(op)}.{Naming.Pascal(op.Name)}AllAsync({Args(op, withCursor: false)}), ct)";

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
