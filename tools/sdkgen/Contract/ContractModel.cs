using System.Text.Json.Nodes;

namespace Orvano.SdkGen.Contract;

/// <summary>Who may call an operation, from <c>x-orvano-audience</c>. It decides which packages carry it.</summary>
internal enum Audience
{
    Client,
    Server,
    Both,
    Console,

    /// <summary>A route a person's browser reaches by navigation (spec 0012); SdkGen generates nothing for it.</summary>
    Browser,
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

/// <summary>Any JSON value (TypeSpec <c>unknown</c>), for data the API stores without a schema, such as user metadata.</summary>
internal sealed record JsonValueType : TypeRef
{
    public static JsonValueType Instance { get; } = new();
}

/// <summary>
/// An open JSON object whose keys are only known at run time, such as a table row: a model marked
/// <c>x-orvano-dynamic</c> (a <c>Record&lt;unknown&gt;</c>). Languages map it to their open map type, never a class.
/// </summary>
internal sealed record DynamicType : TypeRef
{
    public static DynamicType Instance { get; } = new();
}

/// <summary>What a successful call does to the client's stored session, from <c>x-orvano-session</c>.</summary>
internal enum SessionEffect
{
    None,

    /// <summary>A sign in: the response's <c>session</c> becomes the stored session.</summary>
    Start,

    /// <summary>A refresh: the response itself is the new session.</summary>
    Refresh,

    /// <summary>A sign out: the stored session is cleared.</summary>
    End,

    /// <summary>The signed in user changed: the session stays, and the client tells its listeners.</summary>
    User,
}

// Optional: not in the schema's `required` list, omitted from JSON when null.
// Nullable: the value may be JSON null.
// Example: the first `@example` value, which docs snippets use (AC-15); null when there is none.
// Wire: the JSON name. It equals Name except in a model marked `x-orvano-standard-names`, whose snake_case wire
// names (a standard document's, such as OpenID discovery) get camelCase names in code.
internal sealed record ContractProperty(string Name, TypeRef Type, bool Optional, bool Nullable, string? Doc, JsonNode? Example = null)
{
    public string Wire { get; init; } = Name;
}

// Test: marked `x-orvano-test`, so it reaches only the scenario runners and the server (AC-18).
// Event: the `x-orvano-event` name when this model is an event payload (AC-8).
internal sealed record ContractModel(string Name, string? Doc, IReadOnlyList<ContractProperty> Properties, bool Test, string? Event);

internal sealed record ContractEnum(string Name, string? Doc, IReadOnlyList<string> Values, bool Test);

/// <summary>One stable error code from the <c>ErrorCode</c> or <c>TestErrorCode</c> catalog (AC-6).</summary>
internal sealed record ContractErrorCode(string Code, bool Test);

internal sealed record ContractParam(string Name, ParamLocation In, PrimitiveType Type, bool Required, string? Doc, JsonNode? Example = null);

// Id: the operationId, always `service.method`. Name: the method name, the part after the dot.
// Body: the request body, a named model or an open object (DynamicType); null when there is none.
// Result: the success response body, or null when the operation returns no content.
// PageItem: for a cursor list operation (AC-7), the type of one item in `items`; otherwise null.
internal sealed record ContractOperation(
    string Id,
    string Service,
    string Name,
    string HttpMethod,
    string Path,
    Audience Audience,
    string? Doc,
    IReadOnlyList<ContractParam> Params,
    TypeRef? Body,
    int SuccessStatus,
    TypeRef? Result,
    bool Idempotent,
    bool Test,
    TypeRef? PageItem,
    SessionEffect Session = SessionEffect.None,
    string? Scope = null)
{
    public bool IsClient => Audience is Audience.Client or Audience.Both;

    public bool IsServer => Audience is Audience.Server or Audience.Both;
}

/// <summary>The whole contract, sorted so every rendering is byte stable.</summary>
internal sealed record ApiContract(
    string Version,
    IReadOnlyList<ContractOperation> Operations,
    IReadOnlyList<ContractModel> Models,
    IReadOnlyList<ContractEnum> Enums,
    IReadOnlyList<ContractErrorCode> ErrorCodes)
{
    /// <summary>Event payload models, in event name order.</summary>
    public IReadOnlyList<ContractModel> Events =>
        [.. Models.Where(m => m.Event is not null).OrderBy(m => m.Event, StringComparer.Ordinal)];

    /// <summary>Operations grouped by service, services and operations in name order.</summary>
    public IReadOnlyList<(string Service, IReadOnlyList<ContractOperation> Operations)> Services =>
        [.. Operations
            .GroupBy(o => o.Service, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, (IReadOnlyList<ContractOperation>)[.. g.OrderBy(o => o.Name, StringComparer.Ordinal)]))];

    /// <summary>
    /// The operations that pass <paramref name="operations"/> and the events that pass
    /// <paramref name="events"/>, with only the models and enums they reach. This is how a package
    /// gets exactly its audiences (AC-4, AC-17) and a runner exactly its test code (AC-18).
    /// </summary>
    public ApiContract Slice(Func<ContractOperation, bool> operations, Func<ContractModel, bool> events)
    {
        var kept = Operations.Where(operations).ToList();
        var reached = Reach(kept, Models.Where(m => m.Event is not null && events(m)));
        return this with
        {
            Operations = kept,
            Models = [.. Models.Where(m => reached.Contains(m.Name))],
            Enums = [.. Enums.Where(e => reached.Contains(e.Name))],
        };
    }

    /// <summary>Every model and enum name reachable from these operations and event payloads.</summary>
    public HashSet<string> Reach(IEnumerable<ContractOperation> operations, IEnumerable<ContractModel> events)
    {
        var models = Models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);

        void Visit(TypeRef? type)
        {
            switch (type)
            {
                case ArrayType a: Visit(a.Item); break;
                case MapType m: Visit(m.Value); break;
                case EnumType e: reached.Add(e.Name); break;
                case ModelType m when reached.Add(m.Name) && models.TryGetValue(m.Name, out var model):
                    foreach (var p in model.Properties) Visit(p.Type);
                    break;
            }
        }

        foreach (var op in operations)
        {
            Visit(op.Body);
            Visit(op.Result);
        }

        foreach (var e in events) Visit(new ModelType(e.Name));
        return reached;
    }
}
