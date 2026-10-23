using System.Text.Json.Nodes;

namespace Orvano.Host.IntegrationTests.Contract;

// Spec 0014 AC-1, CTR-4: the written form is deterministic, and a drift names the first JSON pointer that differs.
public sealed class ContractJsonTests
{
    [Fact, Trait("Spec", "0014:CTR-4")]
    public void Normalize_sorts_the_name_maps_and_lists_and_keeps_other_order()
    {
        const string json = """
            {"x-orvano-b":1,"openapi":"3.1.1","x-orvano-a":2,
             "paths":{"/v1/z":{"get":{"summary":"z","responses":{"default":{},"404":{},"200":{}},"x-orvano-errors":["b","a"]}},"/v1/a":{}},
             "components":{"schemas":{"Z":{"required":["b","a"],"properties":{"b":{"type":["null","string"]},"a":{"type":"string"}}},"A":{}}}}
            """;

        string normalized = ContractJson.Normalize(json);

        JsonObject root = JsonNode.Parse(normalized)!.AsObject();
        root.Select(member => member.Key).ShouldBe(["openapi", "paths", "components", "x-orvano-a", "x-orvano-b"]);
        root["paths"]!.AsObject().Select(member => member.Key).ShouldBe(["/v1/a", "/v1/z"]);
        root["paths"]!["/v1/z"]!["get"]!["responses"]!.AsObject().Select(member => member.Key).ShouldBe(["200", "404", "default"]);
        root["paths"]!["/v1/z"]!["get"]!["x-orvano-errors"]!.ToJsonString().ShouldBe("""["a","b"]""");
        root["components"]!["schemas"]!.AsObject().Select(member => member.Key).ShouldBe(["A", "Z"]);
        JsonNode z = root["components"]!["schemas"]!["Z"]!;
        z["required"]!.ToJsonString().ShouldBe("""["a","b"]""");
        z["properties"]!.AsObject().Select(member => member.Key).ShouldBe(["b", "a"]); // property order is the server's
        z["properties"]!["b"]!["type"]!.ToJsonString().ShouldBe("""["string","null"]""");
        normalized.ShouldEndWith("}\n");
        normalized.ShouldContain("\n  \"openapi\"");
        ContractJson.Normalize(normalized).ShouldBe(normalized);
    }

    [Fact, Trait("Spec", "0014:AC-1"), Trait("Spec", "0014:CTR-3")]
    public void FirstDifference_names_the_changed_summary_when_an_endpoint_changes()
    {
        JsonNode committed = JsonNode.Parse("""{"paths":{"/v1/health":{"get":{"summary":"Old.","operationId":"Host_Health"}}}}""")!;
        JsonNode emitted = JsonNode.Parse("""{"paths":{"/v1/health":{"get":{"summary":"New.","operationId":"Host_Health"}}}}""")!;

        ContractJson.FirstDifference(committed, emitted).ShouldBe("/paths/~1v1~1health/get/summary");
        ContractJson.FirstDifference(committed, committed.DeepClone()).ShouldBeNull();
    }
}
