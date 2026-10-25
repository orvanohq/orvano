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
                services.AddSingleton<FixtureRows>();
                services.AddSingleton<FixtureNotes>();
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

/// <summary>Where a test note's author is; replaced whole by a PATCH (spec 0014 JSW-9).</summary>
public sealed record NoteAddress(string City, string? Zip);

/// <summary>A test only versioned resource (spec 0014 CNC-1).</summary>
public sealed record NoteResponse(Guid Id, string Name, string? Nickname, IReadOnlyList<string> Tags, NoteAddress? Address, [property: System.ComponentModel.ReadOnly(true)] string Version);

/// <summary>A partial update of a test note (spec 0014 JSW-9).</summary>
public sealed record UpdateNoteRequest(
    [property: OptionalValue<MaxLengthAttribute>(10, ErrorMessage = FieldCodes.TooLong)] Optional<string> Name,
    [property: OptionalValue<MinLengthAttribute>(2, ErrorMessage = FieldCodes.TooShort)] Optional<string?> Nickname,
    [property: OptionalValue<MaxLengthAttribute>(3, ErrorMessage = FieldCodes.TooLong)] Optional<IReadOnlyList<string>> Tags,
    Optional<NoteAddress?> Address,
    [property: OptionalValue<RangeAttribute>(1, 100, ErrorMessage = FieldCodes.OutOfRange)] Optional<int> Rank,
    [property: OptionalValue<EmailAddressAttribute>(ErrorMessage = FieldCodes.InvalidEmail)] Optional<string?> Email);

/// <summary>The notes behind <c>/v1/test/notes</c>, each with a version that every write moves on.</summary>
public sealed class FixtureNotes
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, (NoteResponse Note, ulong Version)> _notes = [];

    public NoteResponse Add(string name, string? nickname = null, IReadOnlyList<string>? tags = null, NoteAddress? address = null)
    {
        lock (_lock)
        {
            var id = Guid.CreateVersion7();
            var note = new NoteResponse(id, name, nickname, tags ?? [], address, ResourceVersion.Format(1));
            _notes[id] = (note, 1);
            return note;
        }
    }

    /// <summary>Runs <paramref name="write"/> on the note under the lock, so the If-Match check and the write are one step (CNC-3).</summary>
    public IResult Write(Guid id, IfMatch ifMatch, Func<NoteResponse, NoteResponse?> write, HttpResponse response)
    {
        lock (_lock)
        {
            if (!_notes.TryGetValue(id, out (NoteResponse Note, ulong Version) entry))
            {
                return Error.NotFound(ErrorCodes.NotFound, "No such note.").ToProblem();
            }

            if (ifMatch.Check(entry.Version).Error is { } conflict)
            {
                return conflict.ToProblem();
            }

            NoteResponse? changed = write(entry.Note);
            if (changed is null)
            {
                _notes.Remove(id);
                return TypedResults.NoContent();
            }

            ulong version = entry.Version + 1;
            changed = changed with { Version = ResourceVersion.Format(version) };
            _notes[id] = (changed, version);
            ResourceVersion.SetETag(response, version);
            return TypedResults.Ok(changed);
        }
    }

    public IResult Read(Guid id, HttpResponse response)
    {
        lock (_lock)
        {
            if (!_notes.TryGetValue(id, out (NoteResponse Note, ulong Version) entry))
            {
                return Error.NotFound(ErrorCodes.NotFound, "No such note.").ToProblem();
            }

            ResourceVersion.SetETag(response, entry.Version);
            return TypedResults.Ok(entry.Note);
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
        test.MapGet("/notes/{noteId:guid}", (Guid noteId, FixtureNotes notes, HttpResponse response) => notes.Read(noteId, response))
            .WithSdk("test", "getNote");
        test.MapPatch("/notes/{noteId:guid}", (Guid noteId, UpdateNoteRequest request, IfMatch ifMatch, FixtureNotes notes, HttpResponse response) =>
            notes.Write(noteId, ifMatch, note => note with
            {
                Name = request.Name.Or(note.Name),
                Nickname = request.Nickname.Or(note.Nickname),
                Tags = request.Tags.Or(note.Tags),
                Address = request.Address.Or(note.Address),
            }, response)).WithConcurrency().WithSdk("test", "updateNote");
        test.MapDelete("/notes/{noteId:guid}", (Guid noteId, IfMatch ifMatch, FixtureNotes notes, HttpResponse response) =>
            notes.Write(noteId, ifMatch, _ => null, response)).WithConcurrency().WithSdk("test", "deleteNote");
        // IDM-2: a marked operation; the replay store is not built yet (IDM-4), so this only checks the key.
        test.MapPost("/charges", (HttpRequest request) =>
            TypedResults.Created("/v1/test/charges/1", new { key = request.Headers[OrvanoHeaders.IdempotencyKey].ToString() }))
            .WithIdempotency().WithSdk("test", "charge");
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
            Result<CursorKeys> keys = byName
                ? CursorCodec.Decode(page.Cursor, query, CursorKey.Text, CursorKey.Uuid)
                : CursorCodec.Decode(page.Cursor, query, CursorKey.Timestamp, CursorKey.Uuid);
            if (keys.Error is { } error)
            {
                return error.ToProblem();
            }

            Guid lastId = keys.Value!.GetUuid(1);
            if (byName)
            {
                string lastName = keys.Value.GetText(0);
                ordered = ordered.Where(row => string.CompareOrdinal(row.Name, lastName) is var order && (order > 0 || (order == 0 && row.Id.CompareTo(lastId) > 0)));
            }
            else
            {
                DateTimeOffset lastCreated = keys.Value.GetTimestamp(0);
                ordered = ordered.Where(row => row.CreatedAt.CompareTo(lastCreated) is var order && (order > 0 || (order == 0 && row.Id.CompareTo(lastId) > 0)));
            }
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
