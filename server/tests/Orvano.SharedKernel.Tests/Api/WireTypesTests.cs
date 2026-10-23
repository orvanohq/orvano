using System.Text.Json;

using Microsoft.AspNetCore.Http;

using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Api;

// Spec 0014 build plan step 1: the shared wire types in Orvano.SharedKernel/Api.
public sealed class WireTypesTests
{
    [Theory]
    [Trait("Spec", "0014:VRS-1")]
    [InlineData("2026-10-22", true)]
    [InlineData("1999-01-01", true)]
    [InlineData("2026-13-01", false)]
    [InlineData("2026-1-01", false)]
    [InlineData("banana", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Version_is_well_formed_only_when_it_is_a_real_yyyy_mm_dd_date(string? value, bool expected) =>
        ApiVersions.IsWellFormed(value).ShouldBe(expected);

    [Fact]
    [Trait("Spec", "0014:VRS-1"), Trait("Spec", "0014:VRS-4")]
    public void Supported_holds_only_current_before_beta()
    {
        ApiVersions.Supported.ShouldBe([ApiVersions.Current]);
        ApiVersions.IsSupported(ApiVersions.Current).ShouldBeTrue();
        ApiVersions.IsSupported("1999-01-01").ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "0014:PRB-7"), Trait("Spec", "0014:PRB-8")]
    public void Every_shared_code_is_well_formed_and_unique()
    {
        string[] codes = [.. ErrorCodes.All.Select(entry => entry.Code)];

        codes.ShouldAllBe(code => ErrorCodes.IsWellFormed(code));
        codes.ShouldBeUnique();
        codes.ShouldNotContain("unknown"); // reserved for SDKs (spec 0004 NET-10)
        ErrorCodes.All.ShouldAllBe(entry => !string.IsNullOrWhiteSpace(entry.Entry.Description));
    }

    public static TheoryData<string, int> SharedCodeStatuses => new()
    {
        { "validation_failed", 400 }, { "invalid_request", 400 }, { "invalid_cursor", 400 }, { "unsupported_api_version", 400 },
        { "unauthorized", 401 }, { "session_expired", 401 }, { "forbidden", 403 }, { "insufficient_scope", 403 },
        { "not_found", 404 }, { "method_not_allowed", 405 }, { "conflict", 409 }, { "idempotency_in_progress", 409 },
        { "version_conflict", 412 }, { "payload_too_large", 413 }, { "unsupported_media_type", 415 },
        { "idempotency_key_reused", 422 }, { "rate_limited", 429 }, { "internal", 500 }, { "unavailable", 503 }, { "timeout", 503 },
    };

    [Theory]
    [Trait("Spec", "0014:PRB-8")]
    [MemberData(nameof(SharedCodeStatuses))]
    public void Shared_code_answers_its_catalogued_status(string code, int status) =>
        ErrorCodes.All.Single(entry => entry.Code == code).Entry.Status.ShouldBe(status);

    [Fact]
    [Trait("Spec", "0014:PRB-8")]
    public void Catalogue_holds_exactly_the_shared_codes() =>
        ErrorCodes.All.Select(entry => entry.Code).ShouldBe(SharedCodeStatuses.Select(row => row.Data.Item1), ignoreOrder: true);

    [Theory]
    [Trait("Spec", "0014:PRB-4")]
    [InlineData(400, "invalid_request")]
    [InlineData(404, "not_found")]
    [InlineData(405, "method_not_allowed")]
    [InlineData(413, "payload_too_large")]
    [InlineData(415, "unsupported_media_type")]
    [InlineData(418, "invalid_request")]
    [InlineData(500, "internal")]
    [InlineData(502, "internal")]
    [InlineData(503, "unavailable")]
    public void Status_only_response_gets_its_code(int status, string code) => ErrorCodes.ForStatus(status).ShouldBe(code);

    [Theory]
    [Trait("Spec", "0014:PRB-7")]
    [InlineData("user_email_taken", true)]
    [InlineData("a1", true)]
    [InlineData("User_email", false)]
    [InlineData("1abc", false)]
    [InlineData("has-hyphen", false)]
    [InlineData("", false)]
    public void Code_is_well_formed_only_when_snake_case(string code, bool expected) => ErrorCodes.IsWellFormed(code).ShouldBe(expected);

    [Fact]
    [Trait("Spec", "0014:PRB-7")]
    public void Code_longer_than_64_characters_is_not_well_formed() => ErrorCodes.IsWellFormed(new string('a', 65)).ShouldBeFalse();

    [Fact]
    [Trait("Spec", "0014:HDR-1"), Trait("Spec", "0014:HDR-2")]
    public void Headers_have_no_x_prefix_and_csrf_stays_out_of_cors()
    {
        OrvanoHeaders.CorsRequestHeaders.Concat(OrvanoHeaders.CorsExposedHeaders)
            .ShouldAllBe(name => !name.StartsWith("X-", StringComparison.OrdinalIgnoreCase));
        OrvanoHeaders.CorsRequestHeaders.ShouldBe(
            ["Authorization", "Content-Type", "Orvano-Key", "Orvano-Api-Version", "Orvano-Client", "Idempotency-Key", "If-Match", "traceparent", "tracestate"]);
        OrvanoHeaders.CorsExposedHeaders.ShouldBe(["Orvano-Request-Id", "Orvano-Api-Version", "ETag", "Retry-After", "Orvano-Idempotent-Replayed"]);
        OrvanoHeaders.CorsRequestHeaders.ShouldNotContain(OrvanoHeaders.Csrf);
    }

    [Theory]
    [Trait("Spec", "0014:PTH-1")]
    [InlineData("/v1", true)]
    [InlineData("/v1/health", true)]
    [InlineData("/V1/health", true)]
    [InlineData("/v10/health", false)]
    [InlineData("/health/live", false)]
    [InlineData("/console/v1", false)]
    public void Api_path_is_anything_under_v1(string path, bool expected) => ApiPaths.IsApi(new PathString(path)).ShouldBe(expected);

    [Theory]
    [Trait("Spec", "0014:JSW-3")]
    [InlineData("2026-10-22T11:14:03.1205580+02:00", "\"2026-10-22T09:14:03.120558Z\"")]
    [InlineData("2026-10-22T09:14:03Z", "\"2026-10-22T09:14:03Z\"")]
    [InlineData("2026-10-22T09:14:03.5-01:00", "\"2026-10-22T10:14:03.5Z\"")]
    public void Timestamp_is_written_as_utc_with_z_and_at_most_six_fraction_digits(string input, string expected)
    {
        JsonSerializerOptions options = Options();

        DateTimeOffset read = JsonSerializer.Deserialize<DateTimeOffset>($"\"{input}\"", options);

        read.Offset.ShouldBe(TimeSpan.Zero);
        JsonSerializer.Serialize(read, options).ShouldBe(expected);
    }

    [Theory]
    [Trait("Spec", "0014:JSW-3")]
    [InlineData("2026-10-22")]
    [InlineData("2026-10-22T09:14:03")]
    [InlineData("22/10/2026 09:14")]
    public void Timestamp_without_an_rfc_3339_offset_is_refused(string input) =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>($"\"{input}\"", Options()));

    [Fact]
    [Trait("Spec", "0014:JSW-2"), Trait("Spec", "0014:JSW-6"), Trait("Spec", "0014:JSW-7")]
    public void Wire_json_writes_snake_case_enums_and_nulls_and_refuses_unknown_members()
    {
        JsonSerializerOptions options = Options();

        JsonSerializer.Serialize(new Sample(SampleShade.DeepBlue, null), options).ShouldBe("""{"shade":"deep_blue","note":null}""");
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Sample>("""{"shade":"deep_blue","extra":1}""", options));
    }

    [Fact]
    [Trait("Spec", "0014:PRB-3")]
    public void Field_location_is_written_in_lower_case() =>
        JsonSerializer.Serialize(new FieldError("tags[2]", FieldLocation.Query, FieldCodes.Required, "m"))
            .ShouldBe("""{"Field":"tags[2]","Location":"query","Code":"required","Message":"m"}""");

    [Fact]
    [Trait("Spec", "0014:PRB-5")]
    public void Field_validation_result_carries_code_and_message_in_its_error_message()
    {
        var result = new FieldValidationResult("tag_reserved", "That tag is reserved.", ["Tags"]);

        result.ErrorMessage.ShouldBe("tag_reserved:That tag is reserved.");
        result.MemberNames.ShouldBe(["Tags"]);
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        return options;
    }

    public enum SampleShade
    {
        Light,
        DeepBlue,
    }

    public sealed record Sample(SampleShade Shade, string? Note);
}
