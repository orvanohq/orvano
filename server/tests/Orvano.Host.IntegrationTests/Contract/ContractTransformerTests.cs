using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

using Orvano.Host.Api.OpenApi;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>The shade of a sample, to show how enums reach the contract.</summary>
public enum SampleShade
{
    Light,
    DeepBlue,
}

/// <summary>Where a sample lives.</summary>
/// <param name="City">The city; always set.</param>
/// <param name="Zip">The postal code, or null when unknown.</param>
public sealed record SampleAddress(string City, string? Zip);

/// <summary>A sample resource, covering every kind of member the schema transformers handle.</summary>
/// <param name="Id">The sample's ID.</param>
/// <param name="Name">The name; never null.</param>
/// <param name="Nickname">The nickname, or null when there is none.</param>
/// <param name="Count">How many, or null when not counted.</param>
/// <param name="Shade">The shade.</param>
/// <param name="Address">Where it lives now.</param>
/// <param name="Previous">Where it lived before, or null.</param>
/// <param name="Tags">Its tags, in the order they were added.</param>
/// <param name="CreatedAt">When it was created, in UTC.</param>
public sealed record SampleResponse(
    Guid Id,
    string Name,
    string? Nickname,
    int? Count,
    SampleShade Shade,
    SampleAddress Address,
    SampleAddress? Previous,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt);

/// <summary>A partial update of a sample.</summary>
/// <param name="Name">The new name; it can't be cleared.</param>
/// <param name="Nickname">The new nickname, or null to clear it.</param>
/// <param name="Count">The new count, or null to clear it.</param>
public sealed record UpdateSampleRequest(Optional<string> Name, Optional<string?> Nickname, Optional<int?> Count);

/// <summary>A versioned sample.</summary>
/// <param name="Id">The sample's ID.</param>
/// <param name="Name">The name.</param>
/// <param name="Version">The sample's version, the same as its ETag without quotes.</param>
public sealed record VersionedSampleResponse(Guid Id, string Name, [property: System.ComponentModel.ReadOnly(true)] string Version);

/// <summary>What a caller sends to create a sample.</summary>
/// <param name="Name">The name, 1 to 40 characters.</param>
/// <param name="Nickname">An optional nickname.</param>
public sealed record CreateSampleRequest(string Name, string? Nickname);

/// <summary>
/// Spec 0014 CTR-6 (spec 0002 TST-7): snapshots of what the transformers write over sample endpoints, one of
/// each audience plus a console and an outside route: the x-orvano-* extensions, the Problem responses and headers,
/// the components, the error catalogue, and JSW-6 response schemas (nullable, non nullable, nested).
/// </summary>
public sealed class ContractTransformerTests
{
    private static readonly SampleResponse Sample = new(
        Guid.Empty, "a", null, null, SampleShade.Light, new SampleAddress("b", null), null, [], DateTimeOffset.UnixEpoch);

    [Fact, Trait("Spec", "0014:CTR-6"), Trait("Spec", "0014:XTN-1"), Trait("Spec", "0014:JSW-6"), Trait("Spec", "0014:PRB-1")]
    public async Task Public_document_matches_the_snapshot_when_sample_endpoints_are_mapped()
    {
        await using ContractTestHost host = await ContractTestHost.StartAsync(MapSamples);

        await Snapshot.MatchAsync(await host.EmitAsync(ContractDocuments.Public), "samples.public");
    }

    [Fact, Trait("Spec", "0014:CTR-6"), Trait("Spec", "0014:CTR-2")]
    public async Task Console_document_matches_the_snapshot_when_sample_endpoints_are_mapped()
    {
        await using ContractTestHost host = await ContractTestHost.StartAsync(MapSamples);

        await Snapshot.MatchAsync(await host.EmitAsync(ContractDocuments.Console), "samples.console");
    }

    internal static void MapSamples(IEndpointRouteBuilder routes)
    {
        var api = ApiRoutes.Create(routes);
        RouteGroupBuilder samples = api.Project.MapGroup("/samples");

        samples.MapPost("/", (CreateSampleRequest request) => TypedResults.Created("/samples/1", Sample with { Name = request.Name }))
            .RequireClientApi(Scopes.ClientDatabases)
            .WithName("Sample_Create")
            .WithSummary("Create a sample.")
            .WithDescription("Creates a sample and returns it.")
            .WithSdk("samples", "create")
            .WithErrors(ErrorCodes.Conflict)
            .WithMeter("samples.create")
            .WithIdempotency()
            .ProducesProblem(StatusCodes.Status409Conflict);

        samples.MapGet("/{sampleId:guid}", Results<Ok<SampleResponse>, ErrorProblemResult> (Guid sampleId) =>
                sampleId == Guid.Empty ? Error.NotFound(ErrorCodes.NotFound, "No such sample.").ToProblem() : TypedResults.Ok(Sample))
            .RequireScope(Scopes.DatabasesRead)
            .WithName("Sample_Get")
            .WithSummary("Get a sample.")
            .WithDescription("Returns one sample by its ID.")
            .WithSdk("samples", "get")
            .WithErrors(ErrorCodes.NotFound)
            .WithSdkTemplate("typed-rows")
            .ProducesProblem(StatusCodes.Status404NotFound);

        samples.MapPatch("/{sampleId:guid}", (Guid sampleId, UpdateSampleRequest request, IfMatch ifMatch) =>
                TypedResults.Ok(new VersionedSampleResponse(sampleId, request.Name.Or("a"), ResourceVersion.Format(2))))
            .RequireScope(Scopes.DatabasesRead)
            .WithName("Sample_Update")
            .WithSummary("Update a sample.")
            .WithDescription("Changes the fields sent and leaves the rest.")
            .WithSdk("samples", "update")
            .WithErrors(ErrorCodes.VersionConflict)
            .WithConcurrency();

        samples.MapGet("/", ([AsParameters] PageQuery page) => TypedResults.Ok(new Page<SampleResponse>([Sample], null, null)))
            .RequireScope(Scopes.DatabasesRead)
            .WithName("Sample_List")
            .WithSummary("List samples.")
            .WithDescription("Returns the samples, a page at a time.")
            .WithSdk("samples", "list")
            .WithErrors(ErrorCodes.InvalidCursor)
            .WithPaging();

        api.Console.MapGet("/organizations/{organizationId:guid}/samples", () => TypedResults.Ok(Sample))
            .RequireConsolePermission(ConsolePermission.OrgRead)
            .WithName("Sample_ConsoleGet")
            .WithSummary("Get the organization's sample.")
            .WithDescription("Returns the organization's sample, for the console.");

        // Outside /v1: in neither document (CTR-2).
        routes.MapGet("/outside", () => "no").AllowPublicAccess();
    }
}
