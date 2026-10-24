using System.Text.Json.Nodes;

using Orvano.Host.Api.OpenApi;
using Orvano.SharedKernel.Api;
using Orvano.SharedKernel.Outbox;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>A test only public event (spec 0014 EVS-1); feature 33 registers the real ones.</summary>
/// <param name="ThingId">The thing's ID.</param>
/// <param name="Code">The thing's code.</param>
/// <param name="Shade">The thing's shade.</param>
public sealed record SampleThingCreated(Guid ThingId, string Code, SampleShade Shade) : IOutboxEvent
{
    public static string Type => "sample.thing_kind.created.v1";
}

/// <summary>An outbox event that is never registered as public.</summary>
/// <param name="ThingId">The thing's ID.</param>
public sealed record SampleThingPurged(Guid ThingId) : IOutboxEvent
{
    public static string Type => "sample.thing.purged.v1";
}

// Spec 0014 AC-20, EVS-1, EVS-2: the webhooks transformer over a test only public event.
public sealed class WebhooksTransformerTests
{
    [Fact, Trait("Spec", "0014:AC-20"), Trait("Spec", "0014:EVS-1")]
    public async Task Public_document_has_a_webhook_keyed_by_the_event_type_when_an_event_is_registered()
    {
        JsonNode document = await PublicDocumentAsync(registered: true);

        JsonNode post = document["webhooks"]!["sample.thing_kind.created.v1"]!["post"]!;
        post["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .ShouldBe("#/components/schemas/SampleThingKindCreatedV1Event");
        post["description"]!.GetValue<string>().ShouldBe("A sample thing was created.");
        post["operationId"]!.GetValue<string>().ShouldBe("Events_SampleThingKindCreatedV1Event");
    }

    [Fact, Trait("Spec", "0014:AC-20"), Trait("Spec", "0014:EVS-1")]
    public async Task Event_schema_lists_every_payload_property_as_required_like_a_response()
    {
        JsonNode document = await PublicDocumentAsync(registered: true);

        JsonNode schema = document["components"]!["schemas"]!["SampleThingKindCreatedV1Event"]!;
        schema["description"]!.GetValue<string>().ShouldBe("A sample thing was created.");
        schema["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["thingId", "code", "shade"]);
        schema["required"]!.AsArray().Select(name => name!.GetValue<string>()).Order(StringComparer.Ordinal).ShouldBe(["code", "shade", "thingId"]);
        schema["properties"]!["thingId"]!["format"]!.GetValue<string>().ShouldBe("uuid");
    }

    [Fact, Trait("Spec", "0014:EVS-2")]
    public async Task Public_document_has_no_webhooks_when_no_event_is_registered()
    {
        JsonNode document = await PublicDocumentAsync(registered: false);

        document["webhooks"].ShouldBeNull();
    }

    [Fact, Trait("Spec", "0014:EVS-2")]
    public async Task Unregistered_outbox_event_never_appears_in_either_document()
    {
        await using ContractTestHost host = await StartAsync(registered: true);

        foreach (string name in new[] { ContractDocuments.Public, ContractDocuments.Console })
        {
            string json = await host.EmitAsync(name);
            json.ShouldNotContain("sample.thing.purged.v1");
            json.ShouldNotContain("SampleThingPurged");
        }

        JsonNode.Parse(await host.EmitAsync(ContractDocuments.Console))!["webhooks"].ShouldBeNull();
    }

    private static async Task<JsonNode> PublicDocumentAsync(bool registered)
    {
        await using ContractTestHost host = await StartAsync(registered);
        return JsonNode.Parse(await host.EmitAsync(ContractDocuments.Public))!;
    }

    private static Task<ContractTestHost> StartAsync(bool registered) =>
        ContractTestHost.StartAsync(ContractTransformerTests.MapSamples, services =>
        {
            if (registered)
            {
                services.AddPublicEvent<SampleThingCreated>("A sample thing was created.");
            }
        });
}
