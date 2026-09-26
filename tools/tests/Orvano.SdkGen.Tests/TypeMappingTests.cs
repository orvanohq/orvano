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
    public void Maps_each_contract_shape_to_every_language(string shape, string ts, string dart, string cs)
    {
        var type = Shape(shape);

        Assert.Equal((ts, dart, cs), (TypeScript.Type(type), Dart.Type(type), CSharp.Type(type)));
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
}
