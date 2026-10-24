using System.ComponentModel.DataAnnotations;

using System.Text.Json.Nodes;

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
                services.AddSingleton<FixtureRows>();
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

/// <summary>A row of the test only paged list (spec 0014 PAG-4).</summary>
public sealed record FixtureRow(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <summary>The rows behind <c>GET /v1/test/rows</c>: 120 to start with, and tests may add more while they page.</summary>
public sealed class FixtureRows
{
    private readonly Lock _lock = new();
    private readonly List<FixtureRow> _rows = [];

    public FixtureRows()
    {
        DateTimeOffset start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < 120; index++)
        {
            // Names repeat every 10 rows, so the row ID decides the order among equal names (PAG-4).
            Add(new FixtureRow(Guid.CreateVersion7(start.AddSeconds(index)), $"row {index % 10:D2}", start.AddSeconds(index)));
        }
    }

    public void Add(FixtureRow row)
    {
        lock (_lock)
        {
            _rows.Add(row);
        }
    }

    public FixtureRow[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _rows];
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
        test.MapGet("/rows", ListRows).WithPaging(total: true).WithSdk("test", "rows");
        test.MapGet("/header", ([FromHeader(Name = "Orvano-Client")][MaxLength(3, ErrorMessage = FieldCodes.TooLong)] string? client) =>
            TypedResults.Ok(new { client })).WithSdk("test", "header");
    };

    // A keyset list over FixtureRows (spec 0014 PAG-1 to PAG-5): ordered by `sort` (name, or else created),
    // then by ID; the cursor holds the last row's sort keys.
    private static IResult ListRows(HttpRequest request, FixtureRows rows, [AsParameters] PageQuery page, string? sort = null)
    {
        bool byName = sort == "name";
        string query = CursorCodec.Query(request);
        IEnumerable<FixtureRow> ordered = byName
            ? rows.Snapshot().OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Id)
            : rows.Snapshot().OrderBy(row => row.CreatedAt).ThenBy(row => row.Id);
        FixtureRow[] all = rows.Snapshot();
        if (page.Cursor is not null)
        {
            Result<JsonArray> keys = CursorCodec.Decode(page.Cursor, query);
            if (keys.Error is { } error)
            {
                return error.ToProblem();
            }

            var lastId = Guid.Parse(keys.Value![^1]!.GetValue<string>());
            ordered = byName
                ? ordered.Where(row => string.CompareOrdinal(row.Name, keys.Value![0]!.GetValue<string>()) is var order && (order > 0 || (order == 0 && row.Id.CompareTo(lastId) > 0)))
                : ordered.Where(row => row.CreatedAt.CompareTo(DateTimeOffset.Parse(keys.Value![0]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)) is var order && (order > 0 || (order == 0 && row.Id.CompareTo(lastId) > 0)));
        }

        FixtureRow[] fetched = [.. ordered.Take(page.Size + 1)];
        var result = Page.From(
            fetched,
            page.Size,
            query,
            row => [byName ? row.Name : row.CreatedAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture), row.Id.ToString()],
            row => row,
            total: all.Length);
        return TypedResults.Ok(result);
    }
}
