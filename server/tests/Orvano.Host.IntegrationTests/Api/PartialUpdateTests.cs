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
