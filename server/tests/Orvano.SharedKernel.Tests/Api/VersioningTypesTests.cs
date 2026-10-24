using System.Text.Json;

using Microsoft.AspNetCore.Http;

using Orvano.SharedKernel.Api;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Api;

// Spec 0014 JSW-9, CNC-1 to CNC-3: Optional<T> on the wire, the version format, and reading If-Match.
public sealed class VersioningTypesTests
{
    private sealed record Patch(Optional<string> Name, Optional<string?> Nickname, Optional<int> Rank);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact, Trait("Spec", "0014:JSW-9")]
    public void Optional_is_absent_when_the_property_is_missing()
    {
        Patch patch = JsonSerializer.Deserialize<Patch>("{}", Web)!;

        patch.Name.HasValue.ShouldBeFalse();
        patch.Nickname.Or("kept").ShouldBe("kept");
    }

    [Fact, Trait("Spec", "0014:JSW-9")]
    public void Optional_holds_a_sent_null_and_a_sent_value()
    {
        Patch patch = JsonSerializer.Deserialize<Patch>("""{ "nickname": null, "name": "b", "rank": 3 }""", Web)!;

        patch.Nickname.HasValue.ShouldBeTrue();
        patch.Nickname.Or("kept").ShouldBeNull();
        patch.Name.Value.ShouldBe("b");
        patch.Rank.Value.ShouldBe(3);
    }

    [Fact, Trait("Spec", "0014:JSW-9")]
    public void Optional_value_type_keeps_a_sent_null_apart_from_its_default()
    {
        Patch patch = JsonSerializer.Deserialize<Patch>("""{ "rank": null }""", Web)!;

        patch.Rank.HasValue.ShouldBeTrue();
        patch.Rank.SentNull.ShouldBeTrue();
        new Optional<int>(0).SentNull.ShouldBeFalse();
    }

    [Fact, Trait("Spec", "0014:CNC-2")]
    public void Version_is_base64url_of_8_big_endian_bytes()
    {
        ResourceVersion.Format(1).ShouldBe("AAAAAAAAAAE");
        ResourceVersion.Format(0x0102030405060708).ShouldBe("AQIDBAUGBwg");
        ResourceVersion.ETag(1).ShouldBe("\"AAAAAAAAAAE\"");
    }

    [Theory, Trait("Spec", "0014:CNC-3")]
    [InlineData("\"AAAAAAAAAAE\"", "AAAAAAAAAAE", false)]
    [InlineData(" \"AAAAAAAAAAE\" ", "AAAAAAAAAAE", false)]
    [InlineData("*", null, true)]
    [InlineData("\"a\", \"b\"", null, true)]
    [InlineData("W/\"a\"", null, true)]
    [InlineData("a", null, true)]
    [InlineData("\"\"", null, true)]
    public void If_match_reads_one_strong_etag_and_refuses_the_rest(string header, string? version, bool malformed)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = header;

        var ifMatch = IfMatch.Read(context.Request);

        ifMatch.Version.ShouldBe(version);
        ifMatch.Malformed.ShouldBe(malformed);
    }

    [Fact, Trait("Spec", "0014:CNC-3")]
    public void If_match_check_passes_without_a_header_and_fails_version_conflict_on_a_stale_one()
    {
        var context = new DefaultHttpContext();
        IfMatch.Read(context.Request).Check(5).Error.ShouldBeNull();

        context.Request.Headers.IfMatch = ResourceVersion.ETag(4);
        Error conflict = IfMatch.Read(context.Request).Check(5).Error!;

        conflict.Code.ShouldBe(ErrorCodes.VersionConflict);
        conflict.Kind.ShouldBe(ErrorKind.PreconditionFailed);
        conflict.Extensions!["currentVersion"].ShouldBe(ResourceVersion.Format(5));
        context.Request.Headers.IfMatch = ResourceVersion.ETag(5);
        IfMatch.Read(context.Request).Check(5).Error.ShouldBeNull();
    }
}
