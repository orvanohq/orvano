using System.Text.Json.Nodes;
using Orvano.SdkGen.Contract;

namespace Orvano.SdkGen.Tests;

// Spec 0001: openapi.public.json is the contract minus console and test operations and the schemas
// only they use. The docs reference and the public breaking change check read it (AC-15, AC-17, AC-18).
public class PublicContractTests
{
    private static JsonObject Build(JsonObject doc) => JsonNode.Parse(PublicContract.Build(doc.ToJsonString()))!.AsObject();

    private static IEnumerable<string> Paths(JsonObject doc) => doc["paths"]!.AsObject().Select(p => p.Key);

    private static IEnumerable<string> Schemas(JsonObject doc) => doc["components"]!["schemas"]!.AsObject().Select(s => s.Key);

    [Fact]
    public void Keeps_only_public_operations() // covers: AC-15, AC-17, AC-18
    {
        var result = Build(Repo.OpenApi());

        Assert.Equal(["/v1/health"], Paths(result));
    }

    [Fact]
    public void Drops_test_schemas_and_keeps_the_error_catalog_and_Problem() // covers: AC-6, AC-18
    {
        var result = Build(Repo.OpenApi());

        Assert.Equal(["ErrorCode", "Health", "Problem"], Schemas(result).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("x-orvano-test", result.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Drops_tags_only_removed_operations_used()
    {
        var result = Build(Repo.OpenApi());

        Assert.Equal(["health"], result["tags"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()));
    }

    [Fact]
    public void Keeps_a_schema_a_console_operation_shares_with_a_public_one_and_drops_one_only_it_uses() // covers: AC-17
    {
        var doc = Repo.OpenApi();
        var schemas = doc["components"]!["schemas"]!.AsObject();
        schemas["ConsoleThing"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("id"),
            ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "string" } },
        };
        doc["paths"]!["/v1/console/health"] = new JsonObject
        {
            ["get"] = ConsoleOperation("console.health", "Health"),
            ["post"] = ConsoleOperation("console.thing", "ConsoleThing"),
        };

        var result = Build(doc);

        Assert.DoesNotContain("/v1/console/health", Paths(result));
        Assert.Contains("Health", Schemas(result));
        Assert.DoesNotContain("ConsoleThing", Schemas(result));
    }

    [Fact]
    public void Keeps_the_rest_of_the_document_and_is_byte_stable()
    {
        var json = Repo.OpenApi().ToJsonString();

        var first = PublicContract.Build(json);
        var second = PublicContract.Build(json);

        Assert.Equal(first, second);
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
        var result = JsonNode.Parse(first)!;
        Assert.Equal("3.1.0", result["openapi"]!.GetValue<string>());
        Assert.Equal(Repo.Version, result["info"]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void Leaves_docs_text_readable_instead_of_escaping_it()
    {
        var result = PublicContract.Build(Repo.OpenApi().ToJsonString());

        Assert.Contains("`https://orvano.dev/errors/<code>`", result, StringComparison.Ordinal);
    }

    private static JsonObject ConsoleOperation(string id, string schema) => new()
    {
        ["operationId"] = id,
        ["responses"] = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = "OK",
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{schema}" } } },
            },
        },
        ["x-orvano-service"] = "console",
        ["x-orvano-audience"] = "console",
    };
}
