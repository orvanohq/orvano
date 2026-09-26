using System.Text.Json.Nodes;
using Orvano.SdkGen.Contract;

namespace Orvano.SdkGen.Tests;

// Spec 0001: SdkGen reads the compiled contract and refuses to run, naming the operation or schema,
// when a rule the SDKs depend on is broken (AC-2). Each test changes one thing in the committed
// openapi.json.
public class ContractReaderTests
{
    [Fact]
    public async Task Reads_the_committed_contract_with_every_audience_and_service() // covers: AC-1, AC-2
    {
        var (contract, errors) = await Repo.ReadAsync(Repo.OpenApi());

        Assert.Empty(errors);
        Assert.NotNull(contract);
        Assert.Equal(Repo.Version, contract.Version);
        var health = Assert.Single(contract.Operations, o => o.Id == "health.get");
        Assert.Equal((Audience.Both, "health", "GET", "/v1/health", false), (health.Audience, health.Service, health.HttpMethod, health.Path, health.Test));
        Assert.Equal(Audience.Console, Assert.Single(contract.Operations, o => o.Id == "test.consolePing").Audience);
    }

    [Fact]
    public async Task Refuses_an_operation_without_an_audience_and_names_it() // covers: AC-2
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get").Remove("x-orvano-audience");

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'health.get'", StringComparison.Ordinal) && e.Contains("missing x-orvano-audience", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_an_operation_without_a_service_group() // covers: AC-2
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get").Remove("x-orvano-service");

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'health.get'", StringComparison.Ordinal) && e.Contains("missing x-orvano-service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_an_unknown_audience_value() // covers: AC-2
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get")["x-orvano-audience"] = "everyone";

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'everyone'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_duplicate_operationId_and_names_the_first_use() // covers: AC-2
    {
        var doc = Repo.OpenApi();
        var conflict = Repo.Operation(doc, "/v1/test/conflict", "post");
        conflict["operationId"] = "test.list";

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("duplicate operationId", StringComparison.Ordinal) && e.Contains("GET /v1/test/items", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_contract_whose_version_differs_from_VERSION() // covers: AC-2, AC-11
    {
        var (contract, errors) = await Repo.ReadAsync(Repo.OpenApi(), version: "9.9.9");

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("VERSION is '9.9.9'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_console_operation_outside_the_console_routes() // covers: AC-17
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get")["x-orvano-audience"] = "console";

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("console operations, and only they, live under /v1/console/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_test_operation_outside_the_test_routes() // covers: AC-18
    {
        var doc = Repo.OpenApi();
        var paths = doc["paths"]!.AsObject();
        var items = paths["/v1/test/items"]!;
        paths.Remove("/v1/test/items");
        paths["/v1/items"] = items;

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'test.list'", StringComparison.Ordinal) && e.Contains("live under /v1/test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_test_route_that_is_not_marked_as_a_test() // covers: AC-18
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/test/conflict", "post").Remove("x-orvano-test");

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'test.conflict'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_schema_only_tests_use_when_it_is_not_marked() // covers: AC-18
    {
        var doc = Repo.OpenApi();
        doc["components"]!["schemas"]!["TestItem"]!.AsObject().Remove("x-orvano-test");

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("schema 'TestItem' is used only by test operations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_a_public_operation_that_reaches_a_test_schema() // covers: AC-18
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get")["responses"]!["200"]!["content"]!["application/json"]!["schema"] =
            new JsonObject { ["$ref"] = "#/components/schemas/TestItem" };

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("schema 'TestItem' is marked x-orvano-test but a public operation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refuses_an_operation_without_the_Problem_default_response() // covers: AC-6
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get")["responses"]!.AsObject().Remove("default");

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'health.get'", StringComparison.Ordinal) && e.Contains("default response must be Problem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collects_every_problem_before_failing() // covers: AC-2
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/health", "get").Remove("x-orvano-audience");
        Repo.Operation(doc, "/v1/test/conflict", "post").Remove("x-orvano-service");

        var (_, errors) = await Repo.ReadAsync(doc);

        Assert.Contains(errors, e => e.Contains("'health.get'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'test.conflict'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recognizes_a_cursor_list_operation_and_its_item_type() // covers: AC-7
    {
        var (contract, _) = await Repo.ReadAsync(Repo.OpenApi());

        var list = Assert.Single(contract!.Operations, o => o.Id == "test.list");
        Assert.Equal(new ModelType("TestItem"), list.PageItem);
        Assert.Null(Assert.Single(contract.Operations, o => o.Id == "health.get").PageItem);
    }

    [Fact]
    public async Task Refuses_a_cursor_operation_whose_response_is_not_a_page() // covers: AC-7
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/test/items", "get")["responses"]!["200"]!["content"]!["application/json"]!["schema"] =
            new JsonObject { ["$ref"] = "#/components/schemas/Health" };

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("'test.list'", StringComparison.Ordinal) && e.Contains("a list operation is a GET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reads_the_idempotent_flag_so_only_marked_operations_retry() // covers: AC-14
    {
        var doc = Repo.OpenApi();
        Repo.Operation(doc, "/v1/test/conflict", "post")["x-orvano-idempotent"] = true;

        var (contract, _) = await Repo.ReadAsync(doc);

        Assert.True(Assert.Single(contract!.Operations, o => o.Id == "test.conflict").Idempotent);
        Assert.False(Assert.Single(contract.Operations, o => o.Id == "health.get").Idempotent);
    }

    [Fact]
    public async Task Reads_error_codes_from_both_catalogs_and_keeps_test_codes_apart() // covers: AC-6, AC-18
    {
        var (contract, _) = await Repo.ReadAsync(Repo.OpenApi());

        Assert.Contains(new ContractErrorCode("not_found", Test: false), contract!.ErrorCodes);
        Assert.Contains(new ContractErrorCode("test_conflict", Test: true), contract.ErrorCodes);
    }

    [Fact]
    public async Task Reads_event_payloads_by_name() // covers: AC-8
    {
        var (contract, _) = await Repo.ReadAsync(Repo.OpenApi());

        var pinged = Assert.Single(contract!.Events);
        Assert.Equal(("test.pinged", true), (pinged.Event, pinged.Test));
    }

    [Theory]
    [InlineData("unevaluatedProperties")] // what TypeSpec's OpenAPI 3.1 emitter writes for Record<T>
    [InlineData("additionalProperties")]
    public async Task Reads_a_Record_property_as_a_map_from_either_keyword(string keyword)
    {
        var doc = Repo.OpenApi();
        var health = doc["components"]!["schemas"]!["Health"]!.AsObject();
        health["properties"]!["labels"] = new JsonObject { ["type"] = "object", [keyword] = new JsonObject { ["type"] = "string" } };
        health["properties"]!["parts"] = new JsonObject { ["type"] = "object", [keyword] = new JsonObject { ["$ref"] = "#/components/schemas/Health" } };

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Empty(errors);
        var properties = contract!.Models.Single(m => m.Name == "Health").Properties;
        Assert.Equal(new MapType(new PrimitiveType(PrimitiveKind.String)), properties.Single(p => p.Name == "labels").Type);
        Assert.Equal(new MapType(new ModelType("Health")), properties.Single(p => p.Name == "parts").Type);
    }

    [Fact]
    public async Task Refuses_an_inline_object_and_asks_for_a_named_model()
    {
        var doc = Repo.OpenApi();
        doc["components"]!["schemas"]!["Health"]!["properties"]!["extra"] =
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["a"] = new JsonObject { ["type"] = "string" } } };

        var (contract, errors) = await Repo.ReadAsync(doc);

        Assert.Null(contract);
        Assert.Contains(errors, e => e.Contains("property 'extra'", StringComparison.Ordinal) && e.Contains("declare a named model", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Keeps_a_property_example_for_docs_snippets() // covers: AC-15
    {
        var (contract, _) = await Repo.ReadAsync(Repo.OpenApi());

        var version = contract!.Models.Single(m => m.Name == "Health").Properties.Single(p => p.Name == "version");
        Assert.Equal("0.4.2", version.Example?.GetValue<string>());
    }
}
