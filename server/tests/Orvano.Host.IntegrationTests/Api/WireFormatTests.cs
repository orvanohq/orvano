using System.Net;
using System.Text;
using System.Text.Json;

using Orvano.SharedKernel.Api;

namespace Orvano.Host.IntegrationTests.Api;

// Spec 0014 build plan step 1: the /v1 wire format through the real pipeline.
public sealed class WireFormatTests(WireServerFixture server) : IClassFixture<WireServerFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // --- AC-3: URL layout -------------------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-3"), Trait("Spec", "0014:PTH-2")]
    public async Task Module_endpoint_answers_under_the_project_group_it_was_given()
    {
        var projectId = Guid.CreateVersion7();

        using HttpResponseMessage response = await GetAsync($"/v1/projects/{projectId}/test/ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("projectId").GetString().ShouldBe(projectId.ToString());
    }

    [Fact, Trait("Spec", "0014:AC-3"), Trait("Spec", "0014:PTH-2")]
    public async Task Project_route_answers_not_found_when_the_project_id_is_not_a_uuid()
    {
        using HttpResponseMessage response = await GetAsync("/v1/projects/not-a-uuid/test/ping");

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact, Trait("Spec", "0014:AC-3"), Trait("Spec", "0014:PTH-4")]
    public async Task Trailing_slash_matches_the_same_endpoint()
    {
        using HttpResponseMessage response = await GetAsync($"/v1/projects/{Guid.CreateVersion7()}/test/ping/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- AC-4: the version header -----------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:VRS-4")]
    public async Task Request_without_a_version_is_served_as_current()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues(OrvanoHeaders.ApiVersion).ShouldBe([ApiVersions.Current]);
        (await JsonAsync(response)).GetProperty("version").GetString().ShouldBe(ApiVersions.Current);
    }

    [Fact, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:VRS-5")]
    public async Task Request_with_a_supported_version_is_served_as_that_version()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/version", (OrvanoHeaders.ApiVersion, ApiVersions.Current));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues(OrvanoHeaders.ApiVersion).ShouldBe([ApiVersions.Current]);
        (await JsonAsync(response)).GetProperty("version").GetString().ShouldBe(ApiVersions.Current);
    }

    [Theory, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:VRS-4")]
    [InlineData("1999-01-01")]
    [InlineData("banana")]
    [InlineData("2026-13-45")]
    public async Task Malformed_or_unsupported_version_answers_400_with_the_supported_versions(string version)
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/version", (OrvanoHeaders.ApiVersion, version));

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.UnsupportedApiVersion);
        problem.GetProperty("supportedVersions").EnumerateArray().Select(v => v.GetString()).ShouldBe(ApiVersions.Supported);
        response.Headers.GetValues(OrvanoHeaders.ApiVersion).ShouldBe([ApiVersions.Current]);
    }

    [Fact, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:VRS-4")]
    public async Task Version_sent_twice_answers_400_even_when_both_values_are_supported()
    {
        using HttpResponseMessage response = await GetAsync(
            "/v1/test/version", (OrvanoHeaders.ApiVersion, ApiVersions.Current), (OrvanoHeaders.ApiVersion, ApiVersions.Current));

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.UnsupportedApiVersion);
    }

    [Fact, Trait("Spec", "0014:AC-4"), Trait("Spec", "0014:VRS-4")]
    public async Task Unsupported_version_answers_400_even_when_the_path_matches_no_endpoint()
    {
        using HttpResponseMessage response = await GetAsync("/v1/nothing-here", (OrvanoHeaders.ApiVersion, "banana"));

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.UnsupportedApiVersion);
    }

    [Fact, Trait("Spec", "0014:VRS-1")]
    public void Current_version_is_a_supported_date()
    {
        ApiVersions.IsWellFormed(ApiVersions.Current).ShouldBeTrue();
        ApiVersions.Supported.ShouldContain(ApiVersions.Current);
    }

    // --- AC-5: headers ----------------------------------------------------------------------------------------------

    [Theory, Trait("Spec", "0014:AC-5"), Trait("Spec", "0014:HDR-5"), Trait("Spec", "0014:HDR-6")]
    [InlineData("/v1/test/version")]
    [InlineData("/v1/test/conflict")]
    [InlineData("/v1/test/throw")]
    [InlineData("/v1/nothing-here")]
    public async Task Every_v1_response_carries_the_request_id_and_no_store(string path)
    {
        using HttpResponseMessage response = await GetAsync(path);

        string requestId = response.Headers.GetValues(OrvanoHeaders.RequestId).ShouldHaveSingleItem();
        requestId.Length.ShouldBe(32);
        requestId.ShouldAllBe(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        if (!response.IsSuccessStatusCode)
        {
            (await JsonAsync(response)).GetProperty("traceId").GetString().ShouldBe(requestId);
        }
    }

    [Fact, Trait("Spec", "0014:AC-5"), Trait("Spec", "0014:HDR-5")]
    public async Task Endpoint_with_its_own_cache_control_keeps_it()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/cached");

        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeFalse();
        response.Headers.CacheControl.MaxAge.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact, Trait("Spec", "0014:HDR-1")]
    public async Task Response_outside_v1_gets_no_orvano_headers()
    {
        using HttpResponseMessage response = await GetAsync("/health/live");

        response.Headers.Contains(OrvanoHeaders.RequestId).ShouldBeFalse();
        response.Headers.Contains(OrvanoHeaders.ApiVersion).ShouldBeFalse();
    }

    // --- AC-6: problem bodies ---------------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-1")]
    public async Task Handler_error_answers_the_full_problem_shape()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/conflict");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.Conflict, "name_taken");
        problem.EnumerateObject().Select(p => p.Name).ShouldBe(["type", "title", "status", "detail", "code", "traceId"]);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
        problem.GetProperty("title").GetString().ShouldBe("Conflict");
        problem.GetProperty("detail").GetString().ShouldBe("That name is taken.");
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-6")]
    public async Task Rate_limited_error_answers_retry_after_in_the_body_and_the_header()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/limited");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited);
        problem.GetProperty("retryAfter").GetInt32().ShouldBe(7);
        response.Headers.RetryAfter?.Delta.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Unmatched_v1_path_answers_404_not_found_problem()
    {
        using HttpResponseMessage response = await GetAsync("/v1/nothing-here");

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Wrong_method_on_a_v1_route_answers_405_method_not_allowed_problem()
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, "/v1/test/conflict");

        await ShouldBeProblemAsync(response, HttpStatusCode.MethodNotAllowed, ErrorCodes.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("GET");
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Unhandled_exception_answers_500_internal_without_the_exception_text()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/throw");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.InternalServerError, ErrorCodes.Internal);
        problem.GetRawText().ShouldNotContain("hunter2");
        problem.GetRawText().ShouldNotContain("InvalidOperationException");
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Server_side_cancellation_answers_503_timeout()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/timeout");

        await ShouldBeProblemAsync(response, HttpStatusCode.ServiceUnavailable, ErrorCodes.Timeout);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Oversized_body_answers_413_payload_too_large()
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/too-large", "{}");

        await ShouldBeProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, ErrorCodes.PayloadTooLarge);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Malformed_json_answers_400_invalid_request()
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/bodies", "{\"name\": ");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Wrong_body_type_answers_400_invalid_request()
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/bodies", "{\"name\": 5}");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-4")]
    public async Task Wrong_content_type_answers_415_unsupported_media_type()
    {
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/v1/test/bodies", content);

        await ShouldBeProblemAsync(response, HttpStatusCode.UnsupportedMediaType, ErrorCodes.UnsupportedMediaType);
    }

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0012:DEN-4")]
    public async Task Access_layer_refusal_answers_the_same_problem_shape()
    {
        using HttpResponseMessage response = await GetAsync($"/v1/projects/{Guid.CreateVersion7()}/test/secured");

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    // --- AC-6 and AC-8: validation ----------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-6"), Trait("Spec", "0014:PRB-3"), Trait("Spec", "0014:PRB-5")]
    public async Task Validation_failures_answer_field_errors_with_codes()
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/v1/test/bodies", """{"nickname": "toolongname", "email": "not-an-email", "address": {"zip": "1"}, "tags": ["forbidden"]}""");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        FieldErrorView[] errors = Errors(problem);
        errors.ShouldContain(new FieldErrorView("name", "body", FieldCodes.Required, "'name' is required."));
        errors.ShouldContain(new FieldErrorView("nickname", "body", FieldCodes.TooLong, "'nickname' is too long."));
        errors.ShouldContain(new FieldErrorView("email", "body", FieldCodes.InvalidEmail, "'email' is not a valid email address."));
        errors.ShouldContain(new FieldErrorView("address.city", "body", FieldCodes.Required, "'address.city' is required."));
    }

    [Fact, Trait("Spec", "0014:PRB-5")]
    public async Task Validatable_object_rule_answers_its_own_code_and_message()
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/bodies", """{"name": "a", "tags": ["forbidden"]}""");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Errors(problem).ShouldBe([new FieldErrorView("tags", "body", "tag_reserved", "That tag is reserved.")]);
    }

    [Fact, Trait("Spec", "0014:PRB-3")]
    public async Task Query_parameter_failure_names_the_query_location()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/query?limit=101");

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Errors(problem).ShouldBe([new FieldErrorView("limit", "query", FieldCodes.OutOfRange, "'limit' is out of range.")]);
    }

    [Fact, Trait("Spec", "0014:PRB-3")]
    public async Task Header_failure_names_the_header_location_and_wire_name()
    {
        using HttpResponseMessage response = await GetAsync("/v1/test/header", (OrvanoHeaders.Client, "client/1.0.0"));

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Errors(problem).ShouldBe([new FieldErrorView("Orvano-Client", "header", FieldCodes.TooLong, "'Orvano-Client' is too long.")]);
    }

    [Theory, Trait("Spec", "0014:AC-8"), Trait("Spec", "0014:JSW-7")]
    [InlineData("""{"name": "a", "projectId": "x"}""", "projectId")]
    [InlineData("""{"name": "a", "Name": "b"}""", "Name")]
    [InlineData("""{"name": "a", "address": {"city": "c", "street": "s"}}""", "address.street")]
    public async Task Unknown_body_property_answers_validation_failed_with_unknown_field(string body, string field)
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/bodies", body);

        JsonElement problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Errors(problem).ShouldBe([new FieldErrorView(field, "body", FieldCodes.UnknownField, $"'{field}' is not a known field.")]);
    }

    // --- AC-8: JSON rules -------------------------------------------------------------------------------------------

    [Fact, Trait("Spec", "0014:AC-8"), Trait("Spec", "0014:JSW-2"), Trait("Spec", "0014:JSW-3"), Trait("Spec", "0014:JSW-6")]
    public async Task Response_json_is_camel_case_with_snake_case_enums_utc_timestamps_and_present_nulls()
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/v1/test/bodies", """{"name": "a", "shade": "deep_blue", "at": "2026-10-22T11:14:03.1205580+02:00"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        string body = await response.Content.ReadAsStringAsync(Token);
        body.ShouldBe("""{"name":"a","nickname":null,"shade":"deep_blue","at":"2026-10-22T09:14:03.120558Z","id":"0199f0a2-0000-7000-8000-00000000abcd"}""");
    }

    [Fact, Trait("Spec", "0014:JSW-2")]
    public async Task Integer_enum_value_answers_invalid_request()
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/test/bodies", """{"name": "a", "shade": 1}""");

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.InvalidRequest);
    }

    // --- helpers ----------------------------------------------------------------------------------------------------

    private sealed record FieldErrorView(string Field, string Location, string Code, string Message);

    private static FieldErrorView[] Errors(JsonElement problem) =>
    [
        .. problem.GetProperty("errors").EnumerateArray().Select(e => new FieldErrorView(
            e.GetProperty("field").GetString()!, e.GetProperty("location").GetString()!,
            e.GetProperty("code").GetString()!, e.GetProperty("message").GetString()!)),
    ];

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await JsonAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("type").GetString().ShouldBe("about:blank");
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldBe(response.Headers.GetValues(OrvanoHeaders.RequestId).Single());
        return problem;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return document.RootElement.Clone();
    }

    private Task<HttpResponseMessage> GetAsync(string path, params (string Name, string Value)[] headers) =>
        SendAsync(HttpMethod.Get, path, content: null, headers);

    private Task<HttpResponseMessage> PostJsonAsync(string path, string json) =>
        SendAsync(HttpMethod.Post, path, new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpContent? content = null, params (string Name, string Value)[] headers)
    {
        using HttpClient client = server.Factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        foreach ((string name, string value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, Token);
    }
}
