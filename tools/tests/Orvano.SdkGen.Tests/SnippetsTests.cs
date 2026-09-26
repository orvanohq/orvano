using System.Text.Json.Nodes;
using Orvano.SdkGen.Contract;
using Orvano.SdkGen.Languages;
using Orvano.SdkGen.Rendering;

namespace Orvano.SdkGen.Tests;

// Spec 0001, AC-15: one docs snippet per public operation per SDK that carries it, with values from
// the contract's @example, else placeholders by type. Console and test operations get none (AC-17, AC-18).
public class SnippetsTests
{
    private static readonly PrimitiveType Str = new(PrimitiveKind.String);

    private static ContractOperation Op(string id, Audience audience, bool test = false, ModelType? body = null, IReadOnlyList<ContractParam>? parameters = null) =>
        new(id, id.Split('.')[0], id.Split('.')[1], body is null ? "GET" : "POST",
            test ? "/v1/test/x" : audience == Audience.Console ? "/v1/console/x" : "/v1/x",
            audience, null, parameters ?? [], body, 200, new ModelType("Widget"), false, test, null);

    private static readonly ContractModel Widget = new("Widget", null, [new("id", Str, false, false, null)], false, null);

    private static readonly ContractModel Input = new("WidgetInput", null,
    [
        new("title", Str, false, false, null, JsonValue.Create("My widget")),
        new("kind", new EnumType("WidgetKind"), false, false, null),
        new("at", new PrimitiveType(PrimitiveKind.DateTime), false, false, null, JsonValue.Create("2026-03-04T05:06:07Z")),
        new("count", new PrimitiveType(PrimitiveKind.Int64), false, false, null),
        new("ratio", new PrimitiveType(PrimitiveKind.Float64), false, false, null),
        new("on", new PrimitiveType(PrimitiveKind.Boolean), false, false, null),
        new("tags", new ArrayType(Str), false, false, null, new JsonArray("red")),
        new("labels", new MapType(Str), false, false, null),
        new("note", Str, true, false, null, JsonValue.Create("left out: optional")),
    ], false, null);

    private static readonly ContractEnum Kind = new("WidgetKind", null, ["square_ish", "round"], false);

    private static ApiContract Contract(params ContractOperation[] operations) =>
        new("0.0.0", operations, [Widget, Input], [Kind], []);

    private static Dictionary<string, string> Render(ApiContract contract) =>
        Snippets.Generate(contract, Repo.Renderer).SelectMany(o => o.Files).ToDictionary(f => f.Path, f => f.Content);

    [Fact]
    public void A_both_operation_gets_a_snippet_in_every_SDK() // covers: AC-15
    {
        var files = Render(Contract(Op("widgets.get", Audience.Both)));

        Assert.Equal(
            [
                "contract/dist/examples/dart/widgets.get.dart",
                "contract/dist/examples/dotnet/widgets.get.cs",
                "contract/dist/examples/flutter/widgets.get.dart",
                "contract/dist/examples/js/widgets.get.ts",
                "contract/dist/examples/nextjs/widgets.get.tsx",
            ],
            files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_client_operation_skips_the_server_SDKs() // covers: AC-4, AC-15
    {
        var files = Render(Contract(Op("widgets.get", Audience.Client)));

        Assert.Equal(["flutter", "js", "nextjs"], files.Keys.Select(k => k.Split('/')[3]).Order(StringComparer.Ordinal));
        Assert.Contains("from '@orvano/js'", files["contract/dist/examples/js/widgets.get.ts"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_operation_skips_the_app_SDKs_and_uses_an_API_key() // covers: AC-4, AC-15
    {
        var files = Render(Contract(Op("widgets.get", Audience.Server)));

        Assert.Equal(["dart", "dotnet", "js"], files.Keys.Select(k => k.Split('/')[3]).Order(StringComparer.Ordinal));
        Assert.Contains("from '@orvano/js/server'", files["contract/dist/examples/js/widgets.get.ts"], StringComparison.Ordinal);
        Assert.Contains("apiKey: process.env.ORVANO_API_KEY", files["contract/dist/examples/js/widgets.get.ts"], StringComparison.Ordinal);
        Assert.Contains("ApiKey = Environment.GetEnvironmentVariable(\"ORVANO_API_KEY\")", files["contract/dist/examples/dotnet/widgets.get.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Console_and_test_operations_get_no_snippet() // covers: AC-17, AC-18
    {
        var files = Render(Contract(Op("admin.get", Audience.Console), Op("test.get", Audience.Both, test: true)));

        Assert.Empty(files);
    }

    [Fact]
    public void Every_snippet_starts_with_the_generated_header()
    {
        var files = Render(Contract(Op("widgets.get", Audience.Both)));

        Assert.All(files.Values, content => Assert.StartsWith($"// {TemplateRenderer.Header}\n", content, StringComparison.Ordinal));
    }

    [Fact]
    public void Uses_property_examples_and_placeholders_by_type_in_TypeScript() // covers: AC-15
    {
        var files = Render(Contract(Op("widgets.create", Audience.Both, body: new ModelType("WidgetInput"),
            parameters: [new("projectId", ParamLocation.Path, Str, true, null), new("size", ParamLocation.Query, new PrimitiveType(PrimitiveKind.Int32), true, null)])));

        var js = files["contract/dist/examples/js/widgets.create.ts"];
        Assert.Contains("orvano.widgets.create('<projectId>', { title: 'My widget', kind: 'square_ish', at: '2026-03-04T05:06:07Z', count: 1, ratio: 1, on: true, tags: ['red'], labels: { key: '<labels>' } }, { size: 1 })", js, StringComparison.Ordinal);
        Assert.DoesNotContain("left out", js, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_the_same_values_as_Dart_constructors() // covers: AC-15
    {
        var files = Render(Contract(Op("widgets.create", Audience.Both, body: new ModelType("WidgetInput"))));

        Assert.Contains(
            "WidgetInput(title: 'My widget', kind: WidgetKind.squareIsh, at: DateTime.utc(2026, 3, 4, 5, 6, 7), count: 1, ratio: 1.0, on: true, tags: ['red'], labels: {'key': '<labels>'})",
            files["contract/dist/examples/dart/widgets.create.dart"], StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_the_same_values_as_CSharp_named_arguments_one_per_line() // covers: AC-15
    {
        var files = Render(Contract(Op("widgets.create", Audience.Both, body: new ModelType("WidgetInput"))));

        var cs = files["contract/dist/examples/dotnet/widgets.create.cs"];
        Assert.Contains("orvano.Widgets.CreateAsync(new WidgetInput(\n    Title: \"My widget\",\n    Kind: WidgetKind.SquareIsh,\n", cs, StringComparison.Ordinal);
        Assert.Contains("At: new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)", cs, StringComparison.Ordinal);
        Assert.Contains("Count: 1L", cs, StringComparison.Ordinal);
        Assert.Contains("Labels: new Dictionary<string, string> { [\"key\"] = \"<labels>\" }", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_result_after_its_model_and_prints_it() // covers: AC-15
    {
        var files = Render(Contract(Op("widgets.get", Audience.Both)));

        Assert.Contains("const widget = await orvano.widgets.get()\nconsole.log(widget)", files["contract/dist/examples/js/widgets.get.ts"], StringComparison.Ordinal);
        Assert.Contains("print(widget.toJson());", files["contract/dist/examples/dart/widgets.get.dart"], StringComparison.Ordinal);
        Assert.Contains("var widget = await orvano.Widgets.GetAsync();", files["contract/dist/examples/dotnet/widgets.get.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_no_content_operation_awaits_without_a_result() // covers: AC-15
    {
        var purge = Op("widgets.purge", Audience.Both) with { Result = null, SuccessStatus = 204 };

        var files = Render(Contract(purge));

        Assert.Contains("\nawait orvano.widgets.purge()\n", files["contract/dist/examples/js/widgets.purge.ts"], StringComparison.Ordinal);
        Assert.Contains("\nawait orvano.Widgets.PurgeAsync();\n", files["contract/dist/examples/dotnet/widgets.purge.cs"], StringComparison.Ordinal);
    }
}
