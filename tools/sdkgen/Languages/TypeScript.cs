using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Languages;

/// <summary>
/// The TypeScript core (<c>@orvano/js</c>, entries <c>.</c> and <c>./server</c>) and the JS
/// scenario dispatch table.
/// </summary>
internal static class TypeScript
{
    public sealed record Property(string Doc, string Declaration);

    public sealed record Model(string Doc, string Name, IReadOnlyList<Property> Properties);

    public sealed record EnumDef(string Doc, string Name, string Union);

    public sealed record Operation(string Doc, string Name, string Params, string Result, string Request);

    public sealed record Service(string ClassName, string Property, string Name, IReadOnlyList<Operation> Operations);

    public sealed record DispatchEntry(string Id, int Status, string? Client, string? Server);

    public static IEnumerable<GeneratedOutput> Generate(ApiContract contract, TemplateRenderer renderer)
    {
        var publicSlice = contract.Slice(a => a != Audience.Console);
        var clientSlice = contract.Slice(a => a is Audience.Client or Audience.Both);
        var serverSlice = contract.Slice(a => a is Audience.Server or Audience.Both);

        var models = new
        {
            Enums = publicSlice.Enums.Select(e => new EnumDef(Doc(e.Doc, ""), e.Name, string.Join(" | ", e.Values.Select(Naming.QuotedString)))).ToList(),
            Models = publicSlice.Models.Select(m => new Model(
                Doc(m.Doc, ""),
                m.Name,
                [.. m.Properties.Select(p => new Property(Doc(p.Doc, "  "), $"{p.Name}{(p.Optional ? "?" : "")}: {Type(p.Type)}{(p.Nullable ? " | null" : "")}"))])).ToList(),
        };

        yield return new GeneratedOutput("TypeScript core", "sdks/js/src/generated", Formatter.Prettier,
        [
            new("sdks/js/src/generated/models.ts", renderer.Render("ts", "models", models)),
            new("sdks/js/src/generated/client.ts", renderer.Render("ts", "services", Services(clientSlice, "client"))),
            new("sdks/js/src/generated/server.ts", renderer.Render("ts", "services", Services(serverSlice, "server"))),
            new("sdks/js/src/generated/version.ts", renderer.Render("ts", "version", new { contract.Version })),
        ]);

        var dispatch = contract.Operations.Select(o => new DispatchEntry(
            o.Id,
            o.SuccessStatus,
            o.Audience is Audience.Client or Audience.Both ? Call(o) : null,
            o.Audience is Audience.Server or Audience.Both ? Call(o) : null)).ToList();
        var bodyModels = contract.Operations.Where(o => o.Audience != Audience.Console && o.Body is not null)
            .Select(o => o.Body!.Name).Distinct().Order(StringComparer.Ordinal).ToList();

        yield return new GeneratedOutput("JS scenario dispatch", "tests/scenarios/runners/js/src/generated", Formatter.Prettier,
        [
            new("tests/scenarios/runners/js/src/generated/dispatch.ts",
                renderer.Render("ts", "dispatch", new { Entries = dispatch, ModelImports = string.Join(", ", bodyModels) })),
        ]);
    }

    private static object Services(ApiContract slice, string entry)
    {
        var services = slice.Services.Select(s => new Service(
            Naming.Pascal(s.Service) + "Service",
            s.Service,
            s.Service,
            [.. s.Operations.Select(ToOperation)])).ToList();
        var used = slice.Models.Select(m => m.Name).Concat(slice.Enums.Select(e => e.Name))
            .Where(n => slice.Operations.Any(o => o.Body?.Name == n || Mentions(o.Result, n)))
            .Order(StringComparer.Ordinal).ToList();
        return new { Services = services, Entry = entry, ModelImports = string.Join(", ", used) };
    }

    private static bool Mentions(TypeRef? type, string name) => type switch
    {
        ModelType m => m.Name == name,
        EnumType e => e.Name == name,
        ArrayType a => Mentions(a.Item, name),
        MapType m => Mentions(m.Value, name),
        _ => false,
    };

    private static Operation ToOperation(ContractOperation op)
    {
        var parameters = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) parameters.Add($"{p.Name}: {Type(p.Type)}");
        if (op.Body is not null) parameters.Add($"body: {op.Body.Name}");
        var query = op.Params.Where(p => p.In == ParamLocation.Query).ToList();
        if (query.Count > 0)
        {
            var optional = query.All(q => !q.Required);
            var fields = string.Join("; ", query.Select(q => $"{q.Name}{(q.Required ? "" : "?")}: {Type(q.Type)}"));
            parameters.Add($"query{(optional ? "?" : "")}: {{ {fields} }}");
        }

        var request = new List<string> { $"method: '{op.HttpMethod}'", $"path: {Path(op)}" };
        if (query.Count > 0) request.Add("query");
        if (op.Body is not null) request.Add("body");
        if (op.Idempotent) request.Add("idempotent: true");

        return new Operation(
            Doc(op.Doc, "  "),
            op.Name,
            string.Join(", ", parameters),
            op.Result is null ? "void" : Type(op.Result),
            "{ " + string.Join(", ", request) + " }");
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
    private static string Call(ContractOperation op)
    {
        var args = new List<string>();
        foreach (var p in op.Params.Where(p => p.In == ParamLocation.Path)) args.Add($"input.{p.Name} as {Type(p.Type)}");
        if (op.Body is not null) args.Add($"input.body as {op.Body.Name}");
        var query = op.Params.Where(p => p.In == ParamLocation.Query).ToList();
        if (query.Count > 0)
            args.Add("{ " + string.Join(", ", query.Select(q => $"{q.Name}: input.{q.Name} as {Type(q.Type)}{(q.Required ? "" : " | undefined")}")) + " }");
        var inputName = args.Count > 0 ? "input" : "_input";
        return $"(o, {inputName}) => o.{op.Service}.{op.Name}({string.Join(", ", args)})";
    }

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
