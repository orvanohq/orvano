using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Api;

// Spec 0014 build plan step 12: partial updates and row versions through the real pipeline, on the test only
// /v1/test/notes/{noteId}.
public sealed class PartialUpdateTests(WireServerFixture server) : IClassFixture<WireServerFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private FixtureNotes Notes => server.Factory.Services.GetRequiredService<FixtureNotes>();

    // --- AC-9: partial updates -------------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_changes_nothing_when_the_body_is_empty()
    {
        NoteResponse note = Notes.Add("first", "nick", ["a"], new NoteAddress("Oslo", "0150"));

        JsonElement body = await OkAsync(await PatchAsync(note.Id, "{}"));

        body.GetProperty("name").GetString().ShouldBe("first");
        body.GetProperty("nickname").GetString().ShouldBe("nick");
        body.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["a"]);
        body.GetProperty("address").GetProperty("zip").GetString().ShouldBe("0150");
    }

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_clears_a_nullable_field_when_it_is_sent_as_null()
    {
        NoteResponse note = Notes.Add("first", "nick");

        JsonElement body = await OkAsync(await PatchAsync(note.Id, """{ "nickname": null }"""));

        body.GetProperty("nickname").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("name").GetString().ShouldBe("first");
    }

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_replaces_arrays_and_nested_objects_whole()
    {
        NoteResponse note = Notes.Add("first", tags: ["a", "b"], address: new NoteAddress("Oslo", "0150"));

        JsonElement body = await OkAsync(await PatchAsync(note.Id, """{ "tags": ["c"], "address": { "city": "Bergen", "zip": null } }"""));

        body.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["c"]);
        body.GetProperty("address").GetProperty("city").GetString().ShouldBe("Bergen");
        body.GetProperty("address").GetProperty("zip").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    [InlineData("name")]
    [InlineData("tags")]
    [InlineData("rank")]
    public async Task Patch_fails_required_when_a_non_nullable_field_is_sent_as_null(string field)
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, $$"""{ "{{field}}": null }""");

        JsonElement problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        JsonElement error = problem.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("field").GetString().ShouldBe(field);
        error.GetProperty("location").GetString().ShouldBe("body");
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.Required);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("first");
    }

    // [OptionalValue<T>] on an Optional<T> field checks the value sent, not the Optional wrapper; an absent field and a null
    // on a nullable field skip them (JSW-9, PRB-5).
    [Theory, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9"), Trait("Spec", "0014:PRB-5")]
    [InlineData("""{ "name": "a much too long name" }""", "name", FieldCodes.TooLong)]
    [InlineData("""{ "nickname": "x" }""", "nickname", FieldCodes.TooShort)]
    [InlineData("""{ "tags": ["a", "b", "c", "d"] }""", "tags", FieldCodes.TooLong)]
    [InlineData("""{ "rank": 0 }""", "rank", FieldCodes.OutOfRange)]
    [InlineData("""{ "rank": 101 }""", "rank", FieldCodes.OutOfRange)]
    [InlineData("""{ "email": "not an email" }""", "email", FieldCodes.InvalidEmail)]
    public async Task Patch_fails_the_attribute_code_when_an_optional_field_breaks_its_constraint(string json, string field, string code)
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, json);

        JsonElement problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        JsonElement error = problem.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("field").GetString().ShouldBe(field);
        error.GetProperty("location").GetString().ShouldBe("body");
        error.GetProperty("code").GetString().ShouldBe(code);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("first");
    }

    [Theory, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9"), Trait("Spec", "0014:PRB-5")]
    [InlineData("""{ "name": "second", "nickname": "nick", "tags": ["a", "b", "c"], "rank": 100, "email": "ada@example.com" }""")]
    [InlineData("""{ "nickname": null, "email": null }""")]
    [InlineData("""{ "address": { "city": "Bergen", "zip": null } }""")]
    [InlineData("{}")]
    public async Task Patch_accepts_constrained_optional_fields_when_valid_absent_or_cleared(string json)
    {
        NoteResponse note = Notes.Add("first", "nick");

        await OkAsync(await PatchAsync(note.Id, json));
    }

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9"), Trait("Spec", "0014:PRB-5")]
    public async Task Patch_reports_every_failing_optional_field_when_several_break_their_constraints()
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, """{ "name": "a much too long name", "rank": 0, "email": "nope" }""");

        JsonElement problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").EnumerateArray()
            .Select(error => $"{error.GetProperty("field").GetString()}:{error.GetProperty("code").GetString()}")
            .ShouldBe(["name:too_long", "rank:out_of_range", "email:invalid_email"], ignoreOrder: true);
    }

    // --- AC-11: row versions and If-Match ----------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-1"), Trait("Spec", "0014:CNC-2")]
    public async Task Read_sends_a_strong_etag_equal_to_the_version_field()
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await GetAsync(note.Id);

        string version = (await OkAsync(response)).GetProperty("version").GetString()!;
        version.ShouldBe("AAAAAAAAAAE"); // version 1 as 8 big endian bytes, base64url without padding
        response.Headers.ETag!.Tag.ShouldBe($"\"{version}\"");
        response.Headers.ETag.IsWeak.ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-3")]
    public async Task Second_write_with_the_same_if_match_fails_version_conflict_with_the_current_version()
    {
        NoteResponse note = Notes.Add("first");
        string etag = $"\"{note.Version}\"";

        using HttpResponseMessage first = await PatchAsync(note.Id, """{ "name": "second" }""", etag);
        string current = (await OkAsync(first)).GetProperty("version").GetString()!;
        using HttpResponseMessage second = await PatchAsync(note.Id, """{ "name": "third" }""", etag);

        first.Headers.ETag!.Tag.ShouldBe($"\"{current}\"");
        current.ShouldNotBe(note.Version);
        JsonElement problem = await ProblemAsync(second, HttpStatusCode.PreconditionFailed, ErrorCodes.VersionConflict);
        problem.GetProperty("currentVersion").GetString().ShouldBe(current);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("second");
    }

    [Fact, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-3")]
    public async Task Write_proceeds_when_no_if_match_is_sent()
    {
        NoteResponse note = Notes.Add("first");
        await OkAsync(await PatchAsync(note.Id, """{ "name": "second" }"""));

        JsonElement body = await OkAsync(await PatchAsync(note.Id, """{ "name": "third" }"""));

        body.GetProperty("name").GetString().ShouldBe("third");
    }

    [Theory, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-3")]
    [InlineData("*")]
    [InlineData("\"AAAAAAAAAAE\", \"AAAAAAAAAAI\"")]
    [InlineData("W/\"AAAAAAAAAAE\"")]
    [InlineData("AAAAAAAAAAE")]
    public async Task Write_fails_invalid_request_when_if_match_is_not_one_strong_etag(string ifMatch)
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, """{ "name": "second" }""", ifMatch);

        await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("first");
    }

    [Fact, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-3")]
    public async Task Delete_fails_version_conflict_when_if_match_is_stale()
    {
        NoteResponse note = Notes.Add("first");
        await OkAsync(await PatchAsync(note.Id, """{ "name": "second" }"""));

        using HttpResponseMessage stale = await SendAsync(HttpMethod.Delete, $"/v1/test/notes/{note.Id}", null, $"\"{note.Version}\"");

        await ProblemAsync(stale, HttpStatusCode.PreconditionFailed, ErrorCodes.VersionConflict);
    }

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-7"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_fails_unknown_field_when_the_body_names_a_field_the_request_does_not_have()
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, """{ "name": "second", "colour": "red" }""");

        JsonElement problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        JsonElement error = problem.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("field").GetString().ShouldBe("colour");
        error.GetProperty("code").GetString().ShouldBe(FieldCodes.UnknownField);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("first");
    }

    [Theory, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    [InlineData("""{ "nickname": 5 }""")]
    [InlineData("""{ "rank": "high" }""")]
    [InlineData("""{ "tags": "a" }""")]
    public async Task Patch_fails_invalid_request_when_a_field_has_the_wrong_type(string json)
    {
        NoteResponse note = Notes.Add("first", "nick", ["a"]);

        using HttpResponseMessage response = await PatchAsync(note.Id, json);

        await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
        JsonElement body = await OkAsync(await GetAsync(note.Id));
        body.GetProperty("nickname").GetString().ShouldBe("nick");
        body.GetProperty("version").GetString().ShouldBe(note.Version);
    }

    [Fact, Trait("Spec", "0014:AC-9"), Trait("Spec", "0014:JSW-9")]
    public async Task Patch_clears_a_nullable_nested_object_when_it_is_sent_as_null()
    {
        NoteResponse note = Notes.Add("first", address: new NoteAddress("Oslo", "0150"));

        JsonElement body = await OkAsync(await PatchAsync(note.Id, """{ "address": null }"""));

        body.GetProperty("address").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("name").GetString().ShouldBe("first");
    }

    [Fact, Trait("Spec", "0014:AC-11"), Trait("Spec", "0014:CNC-2"), Trait("Spec", "0014:CNC-3")]
    public async Task Write_fails_version_conflict_when_if_match_is_a_strong_etag_the_server_never_made()
    {
        NoteResponse note = Notes.Add("first");

        using HttpResponseMessage response = await PatchAsync(note.Id, """{ "name": "second" }""", "\"not-a-version\"");

        JsonElement problem = await ProblemAsync(response, HttpStatusCode.PreconditionFailed, ErrorCodes.VersionConflict);
        problem.GetProperty("currentVersion").GetString().ShouldBe(note.Version);
        (await OkAsync(await GetAsync(note.Id))).GetProperty("name").GetString().ShouldBe("first");
    }

    private Task<HttpResponseMessage> GetAsync(Guid id) => SendAsync(HttpMethod.Get, $"/v1/test/notes/{id}", null, null);

    private Task<HttpResponseMessage> PatchAsync(Guid id, string json, string? ifMatch = null) =>
        SendAsync(HttpMethod.Patch, $"/v1/test/notes/{id}", json, ifMatch);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, string? ifMatch)
    {
        using HttpClient client = server.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(OrvanoHeaders.IfMatch, ifMatch);
        }

        return await client.SendAsync(request, Token);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(status, text);
        JsonElement problem = JsonDocument.Parse(text).RootElement.Clone();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }
}
