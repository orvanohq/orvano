using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel;
using Orvano.SharedKernel.Access;
using Orvano.SharedKernel.Api;
using Orvano.Testing;

namespace Orvano.Host.IntegrationTests.Api;

/// <summary>One in memory server with the test only wire endpoints below, shared by a test class.</summary>
public sealed class WireServerFixture : IDisposable
{
    public WireServerFixture(PostgresFixture postgres)
    {
        Console = new ConsoleBuild();
        Factory = new WireServer(OrvanoApiFactory.DefaultSettings(postgres), Console.ContentRoot);
    }

    public ConsoleBuild Console { get; }

    public OrvanoApiFactory Factory { get; }

    public void Dispose()
    {
        Factory.Dispose();
        Console.Dispose();
    }

    private sealed class WireServer(IReadOnlyDictionary<string, string> settings, string contentRoot) : OrvanoApiFactory(settings, contentRoot)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter, WireEndpoints>();
                services.AddValidation(); // spec 0002 VAL-1: generates the validators for this project's request types
            });
        }
    }
}

public enum Shade
{
    Light,
    DeepBlue,
}

public sealed record WireAddress([property: Required(ErrorMessage = FieldCodes.Required)] string? City, string? Zip);

public sealed record WireRequest(
    [property: Required(ErrorMessage = FieldCodes.Required)] string? Name,
    [property: MaxLength(5, ErrorMessage = FieldCodes.TooLong)] string? Nickname,
    [property: EmailAddress(ErrorMessage = FieldCodes.InvalidEmail)] string? Email,
    WireAddress? Address,
    Shade? Shade,
    DateTimeOffset? At,
    IReadOnlyList<string>? Tags) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Tags is { Count: > 0 } && Tags.Contains("forbidden"))
        {
            yield return new FieldValidationResult("tag_reserved", "That tag is reserved.", [nameof(Tags)]);
        }
    }
}

public sealed record WireResponse(string? Name, string? Nickname, Shade? Shade, DateTimeOffset? At, Guid Id);

/// <summary>
/// Test only endpoints under the real <c>/v1</c> groups, mapped on the host's own route builder after it configured
/// its pipeline, so requests run through the wire format middleware, the exception handler, and the access layer.
/// </summary>
internal sealed class WireEndpoints : IStartupFilter
{
    public const string ProjectPing = "/test/ping";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        IEndpointRouteBuilder routes = app.Properties.Values.OfType<IEndpointRouteBuilder>().Single();
        var api = ApiRoutes.Create(routes);

        // A module maps its own group under the one it is given (spec 0014 PTH-2).
        // Public /v1 endpoints carry their contract metadata, or the host does not start (spec 0014 XTN-2).
        RouteGroupBuilder module = api.Project.MapGroup("/test").WithErrors();
        module.MapGet("/ping", (Guid projectId) => TypedResults.Ok(new { projectId })).AllowPublicAccess().WithSdk("test", "projectPing");
        module.MapGet("/secured", () => "ran").RequireScope(Scopes.StorageRead).WithSdk("test", "projectSecured");

        RouteGroupBuilder test = api.Instance.MapGroup("/test").AllowPublicAccess().WithErrors();
        test.MapGet("/conflict", () => Error.Conflict("name_taken", "That name is taken.").ToProblem()).WithSdk("test", "conflict");
        test.MapGet("/limited", () => Error.RateLimited(ErrorCodes.RateLimited, "Slow down.", TimeSpan.FromSeconds(7)).ToProblem()).WithSdk("test", "limited");
        test.MapGet("/throw", (RequestDelegate)(_ => throw new InvalidOperationException("Password=hunter2"))).WithSdk("test", "throw");
        test.MapGet("/timeout", (RequestDelegate)(_ => throw new OperationCanceledException("query timed out"))).WithSdk("test", "timeout");
        test.MapPost("/too-large", (RequestDelegate)(_ => throw new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge))).WithSdk("test", "tooLarge");
        test.MapGet("/cached", () => "same for all").WithCacheControl("public, max-age=60").WithSdk("test", "cached");
        test.MapGet("/version", (IApiVersionContext version) => TypedResults.Ok(new { version = version.Version })).WithSdk("test", "version");
        test.MapPost("/bodies", (WireRequest request) =>
            TypedResults.Ok(new WireResponse(request.Name, request.Nickname, request.Shade, request.At, Guid.Parse("0199F0A2-0000-7000-8000-00000000ABCD")))).WithSdk("test", "bodies");
        test.MapGet("/query", ([Range(1, 100, ErrorMessage = FieldCodes.OutOfRange)] int limit) => TypedResults.Ok(new { limit })).WithSdk("test", "query");
        test.MapGet("/header", ([FromHeader(Name = "Orvano-Client")][MaxLength(3, ErrorMessage = FieldCodes.TooLong)] string? client) =>
            TypedResults.Ok(new { client })).WithSdk("test", "header");
    };
}
