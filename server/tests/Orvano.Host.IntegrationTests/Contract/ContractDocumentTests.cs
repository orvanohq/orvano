using System.Reflection;
using System.Text.Json.Nodes;

using Orvano.Host.Api.OpenApi;
using Orvano.Modules.Platform;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Contract;

// Spec 0014 AC-1, AC-4, AC-5, AC-21: what the real host's two documents contain.
public sealed class ContractDocumentTests(ContractHostFixture host) : IClassFixture<ContractHostFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact, Trait("Spec", "0014:AC-1"), Trait("Spec", "0014:CTR-2"), Trait("Spec", "0014:HLT-5")]
    public async Task Documents_hold_only_their_v1_routes_when_every_role_is_on()
    {
        JsonObject publicDocument = await DocumentAsync(ContractDocuments.Public);
        JsonObject console = await DocumentAsync(ContractDocuments.Console);

        // /health/live, /health/ready, and the console SPA routes are outside /v1, so in neither (CTR-2, HLT-5).
        Paths(publicDocument).ShouldBe(["/v1/health", "/v1/projects/{projectId}/keys/current"]);
        Paths(console).ShouldBe(
        [
            "/v1/console/config", // spec 0016 FRS-1
            "/v1/console/organizations/{organizationId}", // spec 0017
            "/v1/console/organizations/{organizationId}/projects",
            "/v1/console/projects/{projectId}",
            "/v1/console/projects/{projectId}/keys",
            "/v1/console/session",
            "/v1/console/setup/owner",
            "/v1/console/setup/token", // spec 0016 STP-7
        ]);
    }

    [Fact, Trait("Spec", "0017:SDK-1"), Trait("Spec", "0017:SDK-2"), Trait("Spec", "0017:SDK-6"), Trait("Spec", "0017:AC-16")]
    public async Task Current_key_is_public_for_both_audiences_and_metered()
    {
        JsonObject document = await DocumentAsync(ContractDocuments.Public);

        JsonNode operation = document["paths"]!["/v1/projects/{projectId}/keys/current"]!["get"]!;

        operation["x-orvano-sdk"]!["service"]!.GetValue<string>().ShouldBe("keys");
        operation["x-orvano-sdk"]!["method"]!.GetValue<string>().ShouldBe("current");
        operation["x-orvano-audience"]!.AsArray().Select(x => x!.GetValue<string>()).ShouldBe(["client", "server"]);
        operation["x-orvano-metered"]!.GetValue<string>().ShouldBe("platform.keys.read");
    }

    [Theory, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:CTR-4"), Trait("Spec", "0014:VRS-2")]
    [InlineData(ContractDocuments.Public, "Orvano API")]
    [InlineData(ContractDocuments.Console, "Orvano Console API")]
    public async Task Info_carries_the_title_and_current_api_version_and_no_servers(string documentName, string title)
    {
        JsonObject document = await DocumentAsync(documentName);

        document["openapi"]!.GetValue<string>().ShouldStartWith("3.1.");
        document["info"]!["title"]!.GetValue<string>().ShouldBe(title);
        document["info"]!["version"]!.GetValue<string>().ShouldBe(ApiVersions.Current);
        document.ContainsKey("servers").ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0014:AC-5"), Trait("Spec", "0014:HDR-4")]
    public async Task Header_components_equal_OrvanoHeaders_when_emitted()
    {
        JsonObject document = await DocumentAsync(ContractDocuments.Public);
        string[] parameters = [.. document["components"]!["parameters"]!.AsObject().Select(member => member.Key)];
        string[] headers = [.. document["components"]!["headers"]!.AsObject().Select(member => member.Key)];

        // Every constant in OrvanoHeaders, except Content-Type (OpenAPI ignores it as a header parameter), is declared
        // as a request parameter, a response header, or both (Orvano-Api-Version).
        string[] constants = [.. typeof(OrvanoHeaders).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(name => name != OrvanoHeaders.ContentType)];
        parameters.Union(headers).Order(StringComparer.Ordinal).ShouldBe(constants.Order(StringComparer.Ordinal));

        parameters.ShouldBe(OrvanoHeaders.CorsRequestHeaders.Where(name => name != OrvanoHeaders.ContentType)
            .Append(OrvanoHeaders.Csrf), ignoreOrder: true);
        headers.ShouldBe(OrvanoHeaders.CorsExposedHeaders, ignoreOrder: true);
        foreach (JsonNode? parameter in document["components"]!["parameters"]!.AsObject().Select(member => member.Value))
        {
            parameter!["in"]!.GetValue<string>().ShouldBe("header");
            parameter["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact, Trait("Spec", "0014:AC-21"), Trait("Spec", "0014:XTN-1"), Trait("Spec", "0014:XTN-3")]
    public async Task Error_catalogue_lists_every_shared_code_and_operations_list_only_catalogued_codes()
    {
        JsonObject document = await DocumentAsync(ContractDocuments.Public);
        JsonObject catalogue = document["x-orvano-error-codes"]!.AsObject();

        // The shared codes plus each module's catalogue (PlatformErrorCodes, spec 0016 STP-7).
        IReadOnlyList<(string Code, ErrorCodeAttribute Entry)> expected = [.. ErrorCodes.All, .. ErrorCodes.Catalogue(typeof(PlatformErrorCodes))];
        catalogue.Select(member => member.Key).ShouldBe(expected.Select(entry => entry.Code).Order(StringComparer.Ordinal));
        foreach ((string code, ErrorCodeAttribute entry) in expected)
        {
            catalogue[code]!["status"]!.GetValue<int>().ShouldBe(entry.Status);
            catalogue[code]!["description"]!.GetValue<string>().ShouldBe(entry.Description);
        }

        catalogue[ErrorCodes.VersionConflict]!["members"]!.AsArray().Select(member => member!.GetValue<string>()).ShouldBe(["currentVersion"]);
        catalogue.ContainsKey("unknown").ShouldBeFalse(); // reserved for SDKs (PRB-8)

        JsonNode health = document["paths"]!["/v1/health"]!["get"]!;
        health["x-orvano-errors"]!.AsArray().ShouldBeEmpty(); // its 503 unavailable is an implied transport code
        health["x-orvano-sdk"]!.ToJsonString().ShouldBe("""{"service":"health","method":"get"}""");
        health["x-orvano-audience"]!.ToJsonString().ShouldBe("""["client","server"]""");
        health["operationId"]!.GetValue<string>().ShouldBe("Host_Health");
    }

    [Fact, Trait("Spec", "0014:XTN-1")]
    public async Task Console_document_has_no_public_extensions_or_error_catalogue()
    {
        JsonObject console = await DocumentAsync(ContractDocuments.Console);

        console.ContainsKey("x-orvano-error-codes").ShouldBeFalse();
    }

    private async Task<JsonObject> DocumentAsync(string name) => JsonNode.Parse(await host.EmitAsync(name, Token))!.AsObject();

    private static string[] Paths(JsonObject document) => [.. document["paths"]!.AsObject().Select(member => member.Key)];
}
