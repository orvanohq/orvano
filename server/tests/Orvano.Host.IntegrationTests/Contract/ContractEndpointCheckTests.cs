using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Orvano.Host.Api.OpenApi;
using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Contract;

/// <summary>A module's own error codes, registered with AddErrorCodes (spec 0014 PRB-7).</summary>
public static class SampleErrorCodes
{
    [ErrorCode(ErrorKind.Conflict, "A sample with this name already exists.")]
    public const string SampleNameTaken = "sample_name_taken";
}

/// <summary>A catalogue that declares a shared code again, which the catalogue refuses.</summary>
public static class DuplicateErrorCodes
{
    [ErrorCode(ErrorKind.NotFound, "Declared twice.")]
    public const string NotFound = ErrorCodes.NotFound;
}

// Spec 0014 XTN-2, XTN-3: a host whose public /v1 endpoint lacks its contract metadata does not start, and the message
// names each endpoint and what it lacks. Console routes and routes outside /v1 need nothing.
public sealed class ContractEndpointCheckTests
{
    [Fact, Trait("Spec", "0014:XTN-2")]
    public async Task Host_refuses_to_start_naming_the_endpoint_when_a_public_operation_lacks_WithSdk_and_WithErrors()
    {
        ContractDeclarationException error = await Should.ThrowAsync<ContractDeclarationException>(() =>
            ContractTestHost.StartAsync(routes => ApiRoutes.Create(routes).Instance.MapGet("/bare", () => "x").AllowPublicAccess()));

        error.Message.ShouldContain("spec 0014 XTN-2");
        error.Message.ShouldContain("/v1/bare: it has no SDK name; add .WithSdk(service, method)");
        error.Message.ShouldContain("/v1/bare: it does not list its errors; add .WithErrors(...)");
    }

    [Fact, Trait("Spec", "0014:XTN-3")]
    public async Task Host_refuses_to_start_when_WithErrors_lists_a_code_no_catalogue_declares()
    {
        ContractDeclarationException error = await Should.ThrowAsync<ContractDeclarationException>(() =>
            ContractTestHost.StartAsync(routes => Public(routes, "/taken").WithErrors(SampleErrorCodes.SampleNameTaken)));

        error.Message.ShouldContain("/v1/taken: .WithErrors lists 'sample_name_taken', which no error code catalogue declares (XTN-3)");
    }

    [Fact, Trait("Spec", "0014:XTN-3")]
    public async Task Host_refuses_to_start_when_WithErrors_lists_an_implied_transport_code()
    {
        ContractDeclarationException error = await Should.ThrowAsync<ContractDeclarationException>(() =>
            ContractTestHost.StartAsync(routes => Public(routes, "/busy").WithErrors(ErrorCodes.Unavailable)));

        error.Message.ShouldContain("/v1/busy: .WithErrors lists 'unavailable', a transport code every operation implies; remove it (XTN-3)");
    }

    [Fact, Trait("Spec", "0014:XTN-3"), Trait("Spec", "0014:PRB-7")]
    public async Task Host_starts_and_catalogues_a_module_code_when_the_module_registers_its_codes()
    {
        await using ContractTestHost host = await ContractTestHost.StartAsync(
            routes => Public(routes, "/taken").WithErrors(SampleErrorCodes.SampleNameTaken),
            services => services.AddErrorCodes(typeof(SampleErrorCodes)));

        string document = await host.EmitAsync(ContractDocuments.Public);

        document.ShouldContain("\"sample_name_taken\": {\n      \"status\": 409,\n      \"description\": \"A sample with this name already exists.\"");
    }

    [Fact, Trait("Spec", "0014:CLT-2")]
    public async Task Host_refuses_to_start_when_two_operations_share_an_sdk_name()
    {
        ContractDeclarationException error = await Should.ThrowAsync<ContractDeclarationException>(() =>
            ContractTestHost.StartAsync(routes =>
            {
                Public(routes, "/one").WithSdk("samples", "get").WithErrors();
                Public(routes, "/two").WithSdk("samples", "get").WithErrors();
            }));

        error.Message.ShouldContain("/v1/two: .WithSdk(\"samples\", \"get\") is already used by /v1/one");
    }

    [Fact, Trait("Spec", "0014:XTN-2")]
    public async Task Host_starts_when_only_console_and_outside_routes_omit_the_metadata()
    {
        await using ContractTestHost host = await ContractTestHost.StartAsync(routes =>
        {
            ApiRoutes.Create(routes).Console.MapGet("/organizations/{organizationId:guid}/x", () => "x")
                .RequireConsolePermission(ConsolePermission.OrgRead);
            routes.MapGet("/outside", () => "x").AllowPublicAccess();
        });

        host.Services.GetRequiredService<EndpointDataSource>().Endpoints.Count.ShouldBe(2);
    }

    [Fact, Trait("Spec", "0014:PRB-7")]
    public void Catalogue_refuses_a_code_declared_twice()
    {
        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            new ErrorCatalogue([new ErrorCodeSource(typeof(DuplicateErrorCodes))]));

        error.Message.ShouldContain("'not_found', which another catalogue already declares");
    }

    private static RouteHandlerBuilder Public(IEndpointRouteBuilder routes, string path) =>
        ApiRoutes.Create(routes).Instance.MapGet(path, () => Results.Ok()).AllowPublicAccess().WithSdk("samples", path.Trim('/'));
}
