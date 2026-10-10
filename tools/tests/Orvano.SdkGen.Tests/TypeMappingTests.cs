using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Languages;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Tests;

// Spec 0001, "Type mapping": how each contract shape looks in TypeScript, Dart, and C#.
public class TypeMappingTests
{
    private static TypeRef Shape(string name) => name switch
    {
        "string" => new PrimitiveType(PrimitiveKind.String),
        "utcDateTime" => new PrimitiveType(PrimitiveKind.DateTime),
        "int32" => new PrimitiveType(PrimitiveKind.Int32),
        "int64" => new PrimitiveType(PrimitiveKind.Int64),
        "boolean" => new PrimitiveType(PrimitiveKind.Boolean),
        "Part[]" => new ArrayType(new ModelType("Part")),
        "Record<string>" => new MapType(new PrimitiveType(PrimitiveKind.String)),
        "enum Kind" => new EnumType("Kind"),
        "unknown" => JsonValueType.Instance,
        "Record<unknown>" => new MapType(JsonValueType.Instance),
        "x-orvano-dynamic" => DynamicType.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [InlineData("string", "string", "String", "string")]
    [InlineData("utcDateTime", "string", "DateTime", "DateTimeOffset")]
    [InlineData("int32", "number", "int", "int")]
    [InlineData("int64", "number", "int", "long")]
    [InlineData("boolean", "boolean", "bool", "bool")]
    [InlineData("Part[]", "Part[]", "List<Part>", "IReadOnlyList<Part>")]
    [InlineData("Record<string>", "Record<string, string>", "Map<String, String>", "IReadOnlyDictionary<string, string>")]
    [InlineData("enum Kind", "Kind", "Kind", "Kind")]
    [InlineData("unknown", "unknown", "Object?", "JsonElement")]
    [InlineData("Record<unknown>", "Record<string, unknown>", "Map<String, Object?>", "IReadOnlyDictionary<string, JsonElement>")]
    [InlineData("x-orvano-dynamic", "Record<string, unknown>", "Map<String, Object?>", "JsonObject")] // spec 0015, AC-28
    public void Maps_each_contract_shape_to_every_language(string shape, string ts, string dart, string cs)
    {
        var type = Shape(shape);

        Assert.Equal((ts, dart, cs), (TypeScript.Type(type), Dart.Type(type), CSharp.Type(type)));
    }

    [Theory] // spec 0015: an array type's wire name never collides with its element's, and Dart's reserved words get a suffix
    [InlineData("text[]", "textArray")]
    [InlineData("random_uuid", "randomUuid")]
    [InlineData("users.read", "usersRead")]
    public void Names_enum_members_from_wire_values(string wire, string member)
    {
        Assert.Equal(member, Naming.MemberFromWire(wire));
    }

    [Theory]
    [InlineData("default", "defaultValue")]
    [InlineData("title", "title")]
    public void Escapes_dart_reserved_words_in_members(string name, string member)
    {
        Assert.Equal(member, Naming.DartMember(name));
        Assert.Equal("valueValue", Naming.DartEnumMember("value"));
        Assert.Equal("now", Naming.DartEnumMember("now"));
    }

    [Fact]
    public void Sends_and_answers_an_open_object_in_every_language() // spec 0015, AC-28
    {
        var contract = new ApiContract("0.0.0",
            [new("rows.create", "rows", "create", "POST", "/v1/rows", Audience.Both, null, [], DynamicType.Instance, 201, DynamicType.Instance, false, false, null)],
            [], [], []);
        var renderer = Repo.Renderer;

        var ts = File(TypeScript.Generate(contract, renderer), "sdks/js/src/generated/server.ts");
        var dart = File(Dart.Generate(contract, renderer), "sdks/dart/core/lib/src/generated/services.dart");
        var cs = File(CSharp.Generate(contract, renderer), "sdks/dotnet/src/Orvano/Generated/Services.cs");

        Assert.Contains("create(body: Record<string, unknown>, options?: RequestOptions): Promise<Record<string, unknown>>", ts, StringComparison.Ordinal);
        Assert.Contains("Future<Map<String, Object?>> create(Map<String, Object?> body, {RequestOptions? options})", dart, StringComparison.Ordinal);
        Assert.Contains("body: body,", dart, StringComparison.Ordinal);
        Assert.Contains("Task<JsonObject> CreateAsync(JsonObject body", cs, StringComparison.Ordinal);
        Assert.Contains("using System.Text.Json.Nodes;", cs, StringComparison.Ordinal);
        Assert.Contains("OrvanoJsonContext.Default.JsonObject", cs, StringComparison.Ordinal);
    }

    private static readonly ContractModel Part = new("Part", null, [new("name", new PrimitiveType(PrimitiveKind.String), false, false, null)], false, null);

    private static readonly ContractModel Thing = new("Thing", null,
    [
        new("id", new PrimitiveType(PrimitiveKind.String), false, false, null),
        new("note", new PrimitiveType(PrimitiveKind.String), true, false, null),
        new("deletedAt", new PrimitiveType(PrimitiveKind.DateTime), false, true, null),
        new("part", new ModelType("Part"), true, false, null),
        new("labels", new MapType(new PrimitiveType(PrimitiveKind.String)), true, false, null),
    ], false, null);

    private static readonly ApiContract Contract = new("0.0.0",
        [new("things.get", "things", "get", "GET", "/v1/things", Audience.Both, null, [], null, 200, new ModelType("Thing"), false, false, null)],
        [Part, Thing], [], []);

    private static string File(IEnumerable<GeneratedOutput> outputs, string path) =>
        outputs.SelectMany(o => o.Files).Single(f => f.Path == path).Content;

    [Fact]
    public void TypeScript_marks_optional_properties_and_writes_nullable_ones_as_null_unions()
    {
        var models = File(TypeScript.Generate(Contract, Repo.Renderer), "sdks/js/src/generated/models.ts");

        Assert.Contains("note?: string", models, StringComparison.Ordinal);
        Assert.Contains("deletedAt: string | null", models, StringComparison.Ordinal);
        Assert.Contains("labels?: Record<string, string>", models, StringComparison.Ordinal);
    }

    [Fact]
    public void Dart_leaves_out_optional_values_with_null_aware_elements_and_writes_nullable_ones()
    {
        var models = File(Dart.Generate(Contract, Repo.Renderer), "sdks/dart/core/lib/src/generated/models.dart");

        // A value that needs no conversion uses `?x`; `--fatal-infos` rejects the `if case` form for it.
        Assert.Contains("'note': ?note", models, StringComparison.Ordinal);
        Assert.Contains("'labels': ?labels", models, StringComparison.Ordinal);
        Assert.Contains("if (part case final v?) 'part': v.toJson()", models, StringComparison.Ordinal);
        Assert.Contains("'deletedAt': switch (deletedAt)", models, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharp_skips_optional_values_when_null_and_always_writes_nullable_ones()
    {
        var models = File(CSharp.Generate(Contract, Repo.Renderer), "sdks/dotnet/src/Orvano/Generated/Models.cs");

        Assert.Contains("[property: JsonPropertyName(\"note\"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note = null", models, StringComparison.Ordinal);
        Assert.Contains("[property: JsonPropertyName(\"deletedAt\")] DateTimeOffset? DeletedAt", models, StringComparison.Ordinal);
    }

    private static readonly ContractModel Standard = new("Discovery", null,
    [
        new("jwksUri", new PrimitiveType(PrimitiveKind.String), false, false, null) { Wire = "jwks_uri" },
        new("extra", new MapType(JsonValueType.Instance), true, false, null),
    ], false, null);

    private static readonly ApiContract StandardContract = new("0.0.0",
        [new("things.discover", "things", "discover", "GET", "/v1/things/discovery", Audience.Both, null, [], null, 200, new ModelType("Discovery"), false, false, null)],
        [Standard], [], []);

    [Fact]
    public void A_standard_document_keeps_its_wire_names_on_the_wire_and_camel_case_in_code() // spec 0004: OpenID discovery
    {
        var ts = File(TypeScript.Generate(StandardContract, Repo.Renderer), "sdks/js/src/generated/models.ts");
        var dart = File(Dart.Generate(StandardContract, Repo.Renderer), "sdks/dart/core/lib/src/generated/models.dart");
        var cs = File(CSharp.Generate(StandardContract, Repo.Renderer), "sdks/dotnet/src/Orvano/Generated/Models.cs");

        Assert.Contains("jwks_uri: string", ts, StringComparison.Ordinal);
        Assert.Contains("final String jwksUri;", dart, StringComparison.Ordinal);
        Assert.Contains("json['jwks_uri'] as String", dart, StringComparison.Ordinal);
        Assert.Contains("'jwks_uri': jwksUri", dart, StringComparison.Ordinal);
        Assert.Contains("[property: JsonPropertyName(\"jwks_uri\")] string JwksUri", cs, StringComparison.Ordinal);
        Assert.Contains("using System.Text.Json;", cs, StringComparison.Ordinal);
        Assert.Contains("Map<String, Object?>.from(json['extra'] as Map<String, dynamic>)", dart, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Start", "session: 'start'", "session: SessionChange.start")]
    [InlineData("Refresh", "session: 'refresh'", "session: SessionChange.refresh")]
    [InlineData("End", "session: 'end'", "session: SessionChange.end")]
    public void A_session_operation_tells_the_runtime_what_to_do_with_the_session(string change, string ts, string dart) // spec 0004
    {
        var effect = Enum.Parse<SessionEffect>(change);
        var contract = Contract with
        {
            Operations = [new("things.signIn", "things", "signIn", "POST", "/v1/things/sign-in", Audience.Client, null, [], null, 201, new ModelType("Thing"), false, false, null, effect)],
        };

        Assert.Contains(ts, File(TypeScript.Generate(contract, Repo.Renderer), "sdks/js/src/generated/client.ts"), StringComparison.Ordinal);
        Assert.Contains(dart, File(Dart.Generate(contract, Repo.Renderer), "sdks/dart/core/lib/src/generated/services.dart"), StringComparison.Ordinal);
    }
}
