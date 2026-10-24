using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Api;

// Spec 0014 XTN-1, XTN-2: the contract metadata endpoints declare, and the audience their access declaration implies.
public sealed class ContractMetadataTests
{
    [Fact, Trait("Spec", "0014:XTN-1")]
    public void Extensions_add_their_metadata_when_called()
    {
        Endpoint endpoint = Build(builder => builder
            .WithSdk("rows", "list")
            .WithErrors(ErrorCodes.NotFound, ErrorCodes.Conflict, ErrorCodes.NotFound)
            .WithMeter("databases.rows.read")
            .WithSdkTemplate("typed-rows")
            .WithPaging(total: true)
            .WithConcurrency()
            .WithIdempotency()
            .AllowPublicAccess());

        endpoint.Metadata.GetMetadata<SdkMetadata>().ShouldBe(new SdkMetadata("rows", "list"));
        endpoint.Metadata.GetMetadata<ErrorsMetadata>()!.Codes.ShouldBe([ErrorCodes.NotFound, ErrorCodes.Conflict]);
        endpoint.Metadata.GetMetadata<MeterMetadata>().ShouldBe(new MeterMetadata("databases.rows.read"));
        endpoint.Metadata.GetMetadata<SdkTemplateMetadata>().ShouldBe(new SdkTemplateMetadata("typed-rows"));
        endpoint.Metadata.GetMetadata<PagingMetadata>().ShouldBe(new PagingMetadata(Total: true));
        endpoint.Metadata.GetMetadata<ConcurrencyMetadata>().ShouldNotBeNull();
        endpoint.Metadata.GetMetadata<IdempotencyMetadata>().ShouldNotBeNull();
    }

    [Theory, Trait("Spec", "0014:XTN-1")]
    [InlineData("Rows", "list")]
    [InlineData("rows", "list_all")]
    [InlineData("", "list")]
    public void WithSdk_refuses_a_name_that_is_not_camelCase(string service, string method) =>
        Should.Throw<ArgumentException>(() => Build(builder => builder.WithSdk(service, method)));

    [Theory, Trait("Spec", "0014:PRB-7")]
    [InlineData("NotFound")]
    [InlineData("not-found")]
    public void WithErrors_refuses_a_malformed_code(string code) =>
        Should.Throw<ArgumentException>(() => Build(builder => builder.WithErrors(code)));

    [Theory, Trait("Spec", "0014:XTN-4")]
    [InlineData("rows")]
    [InlineData("Databases.Rows")]
    public void WithMeter_refuses_a_name_that_is_not_dotted_lowercase(string name) =>
        Should.Throw<ArgumentException>(() => Build(builder => builder.WithMeter(name)));

    [Fact, Trait("Spec", "0014:XTN-1")]
    public void Audience_follows_the_access_declaration()
    {
        AccessDeclaration.Audience(Build(builder => builder.RequireClientApi(Scopes.ClientDatabases))).ShouldBe([Audiences.Client]);
        AccessDeclaration.Audience(Build(builder => builder.RequireScope(Scopes.DatabasesRead))).ShouldBe([Audiences.Server]);
        AccessDeclaration.Audience(Build(builder => builder.AllowPublicAccess())).ShouldBe([Audiences.Client, Audiences.Server]);
        AccessDeclaration.Audience(Build(builder => builder.RequireConsolePermission(ConsolePermission.OrgRead))).ShouldBeNull();
        AccessDeclaration.Audience(Build(_ => { })).ShouldBeNull(); // declares nothing, so the access check refuses it
    }

    private static Endpoint Build(Action<RouteHandlerBuilder> configure)
    {
        WebApplication app = WebApplication.CreateBuilder().Build();
        RouteHandlerBuilder builder = app.MapGet("/organizations/{organizationId}/x", () => "x");
        configure(builder);
        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Single();
    }
}
