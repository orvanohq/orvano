using Orvano.SharedKernel.Signals;

using Shouldly;

using Xunit;

namespace Orvano.SharedKernel.Tests.Signals;

// Spec 0013 build plan step 1: the wire format (SND-7, AC-5).
public sealed class SignalPayloadTests
{
    [Theory]
    [Trait("Spec", "0013:SND-7")]
    [InlineData(SignalKind.Cache, "cache")]
    [InlineData(SignalKind.Session, "session")]
    public void Encode_writes_version_kind_and_values_in_order_when_the_batch_is_small(SignalKind kind, string wire) =>
        SignalPayload.Encode(kind, ["b", "a", "c"]).ShouldBe($$"""{"v":1,"k":"{{wire}}","d":["b","a","c"]}""");

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Encode_writes_an_empty_list_when_the_kind_is_job() =>
        SignalPayload.Encode(SignalKind.Job, ["ignored"]).ShouldBe("""{"v":1,"k":"job","d":[]}""");

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Encode_writes_the_heartbeat_with_kind_hb() =>
        SignalPayload.EncodeHeartbeat().ShouldBe("""{"v":1,"k":"hb","d":[]}""");

    [Theory]
    [Trait("Spec", "0013:SND-7"), Trait("Spec", "0013:AC-5")]
    [InlineData(SignalKind.Cache)]
    [InlineData(SignalKind.Session)]
    public void Encode_collapses_to_everything_when_60_tags_of_36_characters_are_over_999_bytes(SignalKind kind)
    {
        string[] tags = [.. Enumerable.Range(0, 60).Select(i => $"tag:{i:D32}")];
        tags[0].Length.ShouldBe(36);

        SignalPayload.Encode(kind, tags).ShouldBe($$"""{"v":1,"k":"{{SignalPayload.WireName(kind)}}","d":["*"]}""");
        SignalPayload.WireValues(kind, tags).ShouldBe(["*"]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Encode_collapses_to_everything_when_the_values_hold_a_star() =>
        SignalPayload.WireValues(SignalKind.Cache, ["a", "*", "b"]).ShouldBe(["*"]);

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Encode_keeps_the_values_when_the_payload_is_exactly_999_bytes()
    {
        // {"v":1,"k":"cache","d":["…"]} is 28 bytes around the value.
        string[] values = [new string('x', 999 - 28)];

        string payload = SignalPayload.Encode(SignalKind.Cache, values);

        System.Text.Encoding.UTF8.GetByteCount(payload).ShouldBe(999);
        SignalPayload.WireValues(SignalKind.Cache, values).ShouldBe(values);
        SignalPayload.WireValues(SignalKind.Cache, [values[0] + "x"]).ShouldBe(["*"]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Encode_measures_the_escaped_bytes_when_values_hold_characters_the_encoder_escapes()
    {
        // The encoder writes each + as a 6 byte escape, so 200 of them in a 971 byte budget overflow although 200 chars fit.
        string[] values = [new string('+', 200)];

        SignalPayload.WireValues(SignalKind.Cache, values).ShouldBe(["*"]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Decode_reads_back_what_encode_wrote_when_the_payload_is_valid()
    {
        SignalPayload.TryDecode(SignalPayload.Encode(SignalKind.Session, ["u1", "u+2"]), out string kind, out IReadOnlyList<string> values)
            .ShouldBeTrue();

        kind.ShouldBe("session");
        values.ShouldBe(["u1", "u+2"]);
    }

    [Fact]
    [Trait("Spec", "0013:SND-7")]
    public void Decode_ignores_unknown_properties_and_returns_an_unknown_kind_as_text()
    {
        SignalPayload.TryDecode("""{"x":true,"v":1,"k":"later","d":["a"],"y":{}}""", out string kind, out IReadOnlyList<string> values)
            .ShouldBeTrue();

        kind.ShouldBe("later");
        values.ShouldBe(["a"]);
        SignalPayload.TryParseKind(kind, out _).ShouldBeFalse();
        SignalPayload.TryParseKind(SignalPayload.HeartbeatKind, out _).ShouldBeFalse();
    }

    [Theory]
    [Trait("Spec", "0013:SND-7"), Trait("Spec", "0013:AC-5")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"v":2,"k":"cache","d":["a"]}""")]
    [InlineData("""{"v":"1","k":"cache","d":["a"]}""")]
    [InlineData("""{"k":"cache","d":["a"]}""")]
    [InlineData("""{"v":1,"d":["a"]}""")]
    [InlineData("""{"v":1,"k":3,"d":["a"]}""")]
    [InlineData("""{"v":1,"k":"cache"}""")]
    [InlineData("""{"v":1,"k":"cache","d":"a"}""")]
    [InlineData("""{"v":1,"k":"cache","d":["a",null]}""")]
    [InlineData("""{"v":1,"k":"cache","d":["a",2]}""")]
    public void Decode_fails_when_the_payload_is_malformed(string payload) =>
        SignalPayload.TryDecode(payload, out _, out _).ShouldBeFalse();

    [Theory]
    [Trait("Spec", "0013:BUS-1")]
    [InlineData(SignalKind.Job, "job")]
    [InlineData(SignalKind.Cache, "cache")]
    [InlineData(SignalKind.Session, "session")]
    public void Wire_names_round_trip_for_every_kind(SignalKind kind, string wire)
    {
        SignalPayload.WireName(kind).ShouldBe(wire);
        SignalPayload.TryParseKind(wire, out SignalKind parsed).ShouldBeTrue();
        parsed.ShouldBe(kind);
    }
}
