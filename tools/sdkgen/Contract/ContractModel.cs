namespace Orvano.SdkGen.Contract;

/// <summary>Who may call an operation, from <c>x-orvano-audience</c>. It decides which packages carry it.</summary>
internal enum Audience
{
    Client,
    Server,
    Both,
    Console,
}

internal enum ParamLocation
{
    Path,
    Query,
}

internal enum PrimitiveKind
{
    String,
    Int32,
    Int64,
    Float64,
    Boolean,
    DateTime,
}

/// <summary>A type the generator knows how to map to every language (spec 0001, type mapping).</summary>
internal abstract record TypeRef;

internal sealed record PrimitiveType(PrimitiveKind Kind) : TypeRef;

internal sealed record ArrayType(TypeRef Item) : TypeRef;

internal sealed record MapType(TypeRef Value) : TypeRef;

internal sealed record ModelType(string Name) : TypeRef;

internal sealed record EnumType(string Name) : TypeRef;

// Optional: not in the schema's `required` list, omitted from JSON when null.
// Nullable: the value may be JSON null.
internal sealed record ContractProperty(string Name, TypeRef Type, bool Optional, bool Nullable, string? Doc);

internal sealed record ContractModel(string Name, string? Doc, IReadOnlyList<ContractProperty> Properties);

internal sealed record ContractEnum(string Name, string? Doc, IReadOnlyList<string> Values);

internal sealed record ContractParam(string Name, ParamLocation In, PrimitiveType Type, bool Required, string? Doc);

// Id: the operationId, always `service.method`. Name: the method name, the part after the dot.
// Result: the success response body, or null when the operation returns no content.
internal sealed record ContractOperation(
    string Id,
    string Service,
    string Name,
    string HttpMethod,
    string Path,
    Audience Audience,
    string? Doc,
    IReadOnlyList<ContractParam> Params,
    ModelType? Body,
    int SuccessStatus,
    TypeRef? Result,
    bool Idempotent);

/// <summary>The whole contract, sorted so every rendering is byte stable.</summary>
internal sealed record ApiContract(
    string Version,
    IReadOnlyList<ContractOperation> Operations,
    IReadOnlyList<ContractModel> Models,
    IReadOnlyList<ContractEnum> Enums)
{
    /// <summary>Operations grouped by service, services and operations in name order.</summary>
    public IReadOnlyList<(string Service, IReadOnlyList<ContractOperation> Operations)> Services =>
        [.. Operations
            .GroupBy(o => o.Service, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, (IReadOnlyList<ContractOperation>)[.. g.OrderBy(o => o.Name, StringComparer.Ordinal)]))];

    /// <summary>
    /// The operations whose audience passes <paramref name="include"/>, with only the models and
    /// enums they reach. This is how a package gets exactly its audiences (AC-4, AC-17).
    /// </summary>
    public ApiContract Slice(Func<Audience, bool> include)
    {
        var operations = Operations.Where(o => include(o.Audience)).ToList();
        var models = Models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);

        void Visit(TypeRef? type)
        {
            switch (type)
            {
                case ArrayType a: Visit(a.Item); break;
                case MapType m: Visit(m.Value); break;
                case EnumType e: reached.Add(e.Name); break;
                case ModelType m when reached.Add(m.Name):
                    foreach (var p in models[m.Name].Properties) Visit(p.Type);
                    break;
            }
        }

        foreach (var op in operations)
        {
            Visit(op.Body);
            Visit(op.Result);
        }

        return this with
        {
            Operations = operations,
            Models = [.. Models.Where(m => reached.Contains(m.Name))],
            Enums = [.. Enums.Where(e => reached.Contains(e.Name))],
        };
    }
}
